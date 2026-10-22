"""Скелет-воин в PSX-стиле: модель, текстуры, риг и анимации -- всё генерируется этим скриптом.

  blender -b --factory-startup -P tools/blender/skeleton_warrior.py -- [--preview build/enemies]

Результат:
  art/enemies/skeleton_warrior.blend                       -- исходник для ручной доводки в Blender
  Assets/Game/Assets/enemies/SkeletonWarrior/SkeletonWarrior.fbx  -- для Unity (риг Humanoid, 5 анимаций)
  Assets/Game/Assets/enemies/SkeletonWarrior/Textures/*.png       -- пиксельные текстуры 64x64
  --preview DIR: картинки модели с трёх сторон и кадры анимаций

Стиль PS1: мало полигонов, жёсткий скиннинг (каждая деталь целиком на одной кости), сглаживание по вершинам
(гуро-шейдинг), текстуры 64x64 с грязью, зерном и 15-битным цветом с дизерингом, point-фильтр.
Детали: голова -- один кусок, вылепленный по горизонтальным сечениям (SKULL_PROFILE: свод, виски, скулы, верхняя
и нижняя челюсть); глазницы и носовая полость -- дыры, за ними тёмная полость черепа; синие светящиеся миндалины с
ореолом в глубине глазниц; низ лица на кости Jaw; зубы -- на текстуре SW_Skull; кожаная броня -- накидка на плечи с круглыми наплечниками до ворота, подтяжки (спереди по рёбрам, сзади
крест), пояс с полами, наручи, обмотки; сандалии по форме стопы; меч. Координаты -- метры, Z вверх, лицо к -Y (как принято в Blender).
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Euler, Matrix, Quaternion, Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT_DIR = os.path.join(ROOT, "Assets", "Game", "Assets", "enemies", "SkeletonWarrior")
TEX_DIR = os.path.join(OUT_DIR, "Textures")
BLEND = os.path.join(ROOT, "art", "enemies", "skeleton_warrior.blend")
FPS = 24

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PREVIEW = os.path.abspath(argv[argv.index("--preview") + 1]) if "--preview" in argv else None

# ---------------------------------------------------------------- сцена
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.fps = FPS
rng = np.random.default_rng(7)

# ---------------------------------------------------------------- текстуры
def noise(n, scale):
    """Гладкий шум: сумма октав случайных решёток с билинейной интерполяцией."""
    out = np.zeros((n, n))
    for octave, amp in ((4, 0.5), (8, 0.3), (16, 0.2)):
        g = rng.random((octave + 1, octave + 1))
        g[-1, :], g[:, -1] = g[0, :], g[:, 0]   # бесшовно
        xs = np.linspace(0, octave, n, endpoint=False)
        i, f = xs.astype(int), xs - xs.astype(int)
        x0, x1 = g[i][:, i], g[i][:, i + 1]
        y0 = x0 * (1 - f)[None, :] + x1 * f[None, :]
        x0b, x1b = g[i + 1][:, i], g[i + 1][:, i + 1]
        y1 = x0b * (1 - f)[None, :] + x1b * f[None, :]
        out += amp * (y0 * (1 - f)[:, None] + y1 * f[:, None])
    return (out - out.min()) / (np.ptp(out) + 1e-6) * scale


BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) / 16.0 - 0.5
GRIME = np.array([0.42, 0.40, 0.26])    # буро-зелёная грязь


def ps1(rgb, grime=0.4):
    """Огрубление «под PS1»: пятна грязи, зерно, цветовой шум, 15-битный цвет с упорядоченным дизерингом."""
    n = rgb.shape[0]
    dirt = noise(n, 1.0)[..., None] * grime
    rgb = rgb * (1 - dirt) + GRIME * rgb.mean(-1, keepdims=True) * dirt
    rgb = rgb * (0.82 + 0.36 * rng.random((n, n, 1)))          # зерно
    rgb = rgb + (rng.random((n, n, 3)) - 0.5) * 0.07           # грязный цветовой шум
    levels = 31                                                # 5 бит на канал, как у PS1
    dither = np.tile(BAYER, (n // 4, n // 4))[..., None] / levels
    return np.floor(np.clip(rgb + dither, 0, 1) * levels + 0.5) / levels


def save_texture(name, rgb, alpha=None, raw=False, grime=0.4):
    h, w = rgb.shape[:2]
    if not raw:
        rgb = ps1(rgb, grime)
    img = bpy.data.images.new(name, w, h, alpha=alpha is not None)
    px = np.ones((h, w, 4))
    px[:, :, :3] = np.clip(rgb, 0, 1)
    if alpha is not None:
        px[:, :, 3] = alpha
    img.pixels = px[::-1].ravel()   # Blender хранит строки снизу вверх
    os.makedirs(TEX_DIR, exist_ok=True)
    img.filepath_raw = os.path.join(TEX_DIR, name + ".png")
    img.file_format = "PNG"
    img.save()
    return img


N = 64
def tex_bone():
    n = noise(N, 1.0)
    base = np.array([0.70, 0.64, 0.46]) * (0.8 + 0.25 * n)[..., None]
    base[noise(N, 1.0) > 0.8] *= 0.6                           # тёмные пятна
    base[noise(N, 1.0) > 0.86] *= np.array([0.85, 0.88, 0.7])  # зеленоватая плесень
    for _ in range(9):   # трещины
        x, y = rng.integers(0, N, 2)
        for _ in range(rng.integers(6, 16)):
            base[y % N, x % N] *= 0.3
            x += rng.integers(-1, 2); y += 1
    return base


def tex_leather():
    n = noise(N, 1.0)
    base = np.array([0.30, 0.19, 0.11]) * (0.55 + 0.6 * n)[..., None]
    base[noise(N, 1.0) > 0.7] *= 0.5                           # потёртости
    spots = noise(N, 1.0) > 0.82
    base[spots] = base[spots] * 0.5 + 0.04                     # пятна
    base[:, ::16] *= 0.45                                      # швы
    base[::4, 7::16] = [0.50, 0.42, 0.28]                      # стежки
    return base


def tex_strap():
    n = noise(N, 1.0)
    base = np.array([0.17, 0.11, 0.07]) * (0.65 + 0.45 * n)[..., None]
    base[:, :3] *= 1.6; base[:, -3:] *= 1.6                    # светлые кромки ремня
    for y in range(6, N, 16):                                  # заклёпки
        base[y:y + 3, 30:33] = [0.45, 0.42, 0.36]
    return base


def tex_metal():
    n = noise(N, 1.0)
    base = np.array([0.32, 0.31, 0.30]) * (0.65 + 0.5 * n)[..., None]
    rust = noise(N, 1.0) > 0.55
    base[rust] = base[rust] * 0.35 + np.array([0.38, 0.18, 0.07]) * 0.65
    base[::8, :] *= 1.15                                       # царапины
    return base


def tex_steel():
    n = noise(N, 1.0)
    base = np.array([0.55, 0.56, 0.58]) * (0.7 + 0.4 * n)[..., None]
    base[noise(N, 1.0) > 0.72] *= np.array([0.7, 0.55, 0.4])   # ржавые пятна
    base[:, N // 2 - 1:N // 2 + 1] *= 1.25                     # дол посередине клинка
    return base


def tex_cloth():
    n = noise(N, 1.0)
    base = np.array([0.10, 0.09, 0.11]) * (0.55 + 0.7 * n)[..., None]
    base[noise(N, 1.0) > 0.78] *= 0.35                         # дыры и грязь
    return base


def tex_skull():
    """Зубы на передней части челюстей: u = 0.5 + x / 0.22, v = (z - 1.57) / 0.1 (см. head()).
    Ряды зубов с промежутками и тёмной линией смыкания, вокруг -- кость."""
    base = tex_bone()
    yy, xx = np.mgrid[0:N, 0:N]
    u, v = (xx + 0.5) / N, 1 - (yy + 0.5) / N
    teeth = (v > 0.36) & (v < 0.78) & (np.abs(u - 0.5) < 0.22)
    base[teeth] = np.array([0.76, 0.7, 0.52]) * (0.85 + 0.2 * noise(N, 1.0)[teeth])[:, None]
    base[teeth & (((u - 0.5) / 0.062 % 1) < 0.26)] *= 0.35     # промежутки между зубами (1 пиксель)
    base[teeth & (np.abs(v - 0.62) < 0.025)] = np.array([0.06, 0.05, 0.04])  # смыкание рядов
    base[teeth & ((v < 0.4) | (v > 0.74))] *= 0.75             # дёсны темнее
    return base


def tex_glow(n=16):
    """Мягкий синий ореол глаза: яркий центр, гаснет к краю (в Unity -- аддитивно)."""
    yy, xx = np.mgrid[0:n, 0:n]
    r = np.sqrt(((xx + 0.5) / n - 0.5) ** 2 / 0.25 + ((yy + 0.5) / n - 0.5) ** 2 / 0.12)
    k = np.clip(1 - r, 0, 1) ** 1.6
    rgb = np.array([0.1, 0.45, 1.0]) * k[..., None] + np.clip(k - 0.6, 0, 1)[..., None] * 1.2
    rgb = np.floor(np.clip(rgb, 0, 1) * 31 + 0.5) / 31
    return rgb, rgb.max(axis=-1)


def material(name, img=None, emission=None, strength=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.9
    if img is not None:
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = img
        t.interpolation = "Closest"          # PSX: пиксели без сглаживания
        nt.links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
    if emission is not None:
        bsdf.inputs["Base Color"].default_value = (*emission, 1)
        bsdf.inputs["Emission Color"].default_value = (*emission, 1)
        bsdf.inputs["Emission Strength"].default_value = strength
    return m


def flame_material(name, img, strength=4.0):
    """Светящийся полупрозрачный ореол: цвет текстуры -- свечение, яркость -- прозрачность."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    t = nt.nodes.new("ShaderNodeTexImage")
    t.image, t.interpolation = img, "Closest"
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Strength"].default_value = strength
    tr = nt.nodes.new("ShaderNodeBsdfTransparent")
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(t.outputs["Color"], em.inputs["Color"])
    nt.links.new(t.outputs["Alpha"], mix.inputs["Fac"])
    nt.links.new(tr.outputs[0], mix.inputs[1])
    nt.links.new(em.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs["Surface"])
    if hasattr(m, "surface_render_method"):
        m.surface_render_method = "BLENDED"
    m.use_backface_culling = False
    return m


glow_rgb, glow_alpha = tex_glow()
MATS = [
    material("SW_Bone", save_texture("SW_Bone", tex_bone(), grime=0.22)),
    material("SW_Leather", save_texture("SW_Leather", tex_leather())),
    material("SW_Strap", save_texture("SW_Strap", tex_strap())),
    material("SW_Metal", save_texture("SW_Metal", tex_metal())),
    material("SW_Cloth", save_texture("SW_Cloth", tex_cloth())),
    material("SW_Steel", save_texture("SW_Steel", tex_steel())),
    material("SW_EyeFire", emission=(0.1, 0.5, 1.0), strength=10.0),
    flame_material("SW_EyeGlow", save_texture("SW_EyeGlow", glow_rgb, alpha=glow_alpha, raw=True), strength=5.0),
    material("SW_Skull", save_texture("SW_Skull", tex_skull(), grime=0.22)),
    material("SW_Cavity", emission=(0.02, 0.015, 0.02), strength=0.0),
]
BONE, LEATHER, STRAP, METAL, CLOTH, STEEL, EYE, FLAME, SKULL, CAVITY = range(len(MATS))
FLAT = {METAL, STEEL, FLAME}   # без сглаживания: металл с гранями, плоский ореол глаз

# ---------------------------------------------------------------- кости рига (голова, хвост) -- T-поза
L, R = 1, -1
BONES = {
    "Hips":          ((0, 0, 0.98), (0, 0, 1.10), None),
    "Spine":         ((0, 0, 1.10), (0, 0, 1.26), "Hips"),
    "Chest":         ((0, 0, 1.26), (0, 0, 1.48), "Spine"),
    "Neck":          ((0, 0, 1.48), (0, 0, 1.58), "Chest"),
    "Head":          ((0, 0, 1.58), (0, 0, 1.84), "Neck"),
    "Jaw":           ((0, -0.03, 1.63), (0, -0.11, 1.60), "Head"),
}
for s, side in ((L, "L"), (R, "R")):
    BONES.update({
        f"Shoulder.{side}": ((s * 0.04, 0, 1.44), (s * 0.18, 0, 1.45), "Chest"),
        f"UpperArm.{side}": ((s * 0.18, 0, 1.45), (s * 0.46, 0, 1.45), f"Shoulder.{side}"),
        f"LowerArm.{side}": ((s * 0.46, 0, 1.45), (s * 0.72, 0, 1.45), f"UpperArm.{side}"),
        f"Hand.{side}":     ((s * 0.72, 0, 1.45), (s * 0.84, 0, 1.45), f"LowerArm.{side}"),
        f"UpperLeg.{side}": ((s * 0.10, 0, 0.98), (s * 0.10, 0, 0.54), "Hips"),
        f"LowerLeg.{side}": ((s * 0.10, 0, 0.54), (s * 0.10, 0, 0.10), f"UpperLeg.{side}"),
        f"Foot.{side}":     ((s * 0.10, 0, 0.10), (s * 0.10, -0.14, 0.03), f"LowerLeg.{side}"),
    })
BONES["Sword"] = ((-0.79, 0, 1.45), (-0.79, -0.25, 1.45), "Hand.R")
BONE_NAMES = list(BONES)

# ---------------------------------------------------------------- геометрия
bm = bmesh.new()
deform = bm.verts.layers.deform.verify()
uv_layer = bm.loops.layers.uv.verify()


def finish(verts, mat, bone, uv_scale=4.0, keep_uv=False):
    """Материал, жёсткая привязка к кости и кубическая проекция UV для только что созданных вершин."""
    gi = BONE_NAMES.index(bone)
    vs = set(verts)
    for v in vs:
        v[deform][gi] = 1.0
    faces = {f for v in vs for f in v.link_faces}
    for f in faces:
        f.material_index = mat
        f.normal_update()
        if keep_uv:
            continue
        nx, ny, nz = (abs(c) for c in f.normal)
        for loop in f.loops:
            c = loop.vert.co
            u, w = (c.y, c.z) if nx >= ny and nx >= nz else (c.x, c.z) if ny >= nz else (c.x, c.y)
            loop[uv_layer].uv = (u * uv_scale, w * uv_scale)


def place(verts, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    if isinstance(scale, (int, float)):
        scale = (scale, scale, scale)
    m = Matrix.LocRotScale(Vector(loc), Euler([math.radians(a) for a in rot]), Vector(scale))
    bmesh.ops.transform(bm, matrix=m, verts=list(verts))


def box(size, mat, bone, **kw):
    v = bmesh.ops.create_cube(bm, size=1.0)["verts"]
    place(v, scale=size)
    place(v, **kw)
    finish(v, mat, bone)
    return v


def blob(radius, mat, bone, segs=8, rings=5, **kw):
    v = bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=1.0)["verts"]
    place(v, scale=radius)
    place(v, **kw)
    finish(v, mat, bone)
    return v


def rod(p1, p2, r1, mat, bone, r2=None, segs=5):
    """Цилиндр (или усечённый конус) от p1 до p2."""
    p1, p2 = Vector(p1), Vector(p2)
    d = p2 - p1
    v = bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r1, radius2=r2 if r2 is not None else r1,
                              depth=d.length)["verts"]
    rot = Vector((0, 0, 1)).rotation_difference(d.normalized())
    bmesh.ops.transform(bm, matrix=Matrix.Translation((p1 + p2) / 2) @ rot.to_matrix().to_4x4(), verts=v)
    finish(v, mat, bone)
    return v


def ring(center, radius, minor, mat, bone, segs=8, scale=(1, 1, 1), rot=(0, 0, 0), arc=360):
    """Тор (ребро, кольцо пояса) из segs сегментов по 3 грани; arc < 360 -- открытая дуга."""
    verts, prev = [], None
    steps = segs if arc == 360 else segs + 1
    for i in range(steps):
        a = math.radians(-90 - arc / 2 + arc * i / segs) if arc != 360 else 2 * math.pi * i / segs
        c, s = math.cos(a), math.sin(a)
        cur = []
        for j in range(3):
            b = 2 * math.pi * j / 3
            rr = radius + minor * math.cos(b)
            cur.append(bm.verts.new((rr * c, rr * s, minor * math.sin(b))))
        if prev:
            for j in range(3):
                bm.faces.new((prev[j], prev[(j + 1) % 3], cur[(j + 1) % 3], cur[j]))
        verts += cur
        prev = cur
    if arc == 360:
        first = verts[:3]
        for j in range(3):
            bm.faces.new((prev[j], prev[(j + 1) % 3], first[(j + 1) % 3], first[j]))
    place(verts, scale=scale)
    place(verts, loc=center, rot=rot)
    finish(verts, mat, bone)
    return verts


def strap(points, width, thick, mat, bone):
    """Плоский ремень по ломаной: по бруску на каждый отрезок, ширина -- по оси X мира."""
    for p1, p2 in zip(points, points[1:]):
        p1, p2 = Vector(p1), Vector(p2)
        d = p2 - p1
        v = bmesh.ops.create_cube(bm, size=1.0)["verts"]
        bmesh.ops.scale(bm, vec=(width, thick, d.length * 1.08), verts=v)
        rot = Vector((0, 0, 1)).rotation_difference(d.normalized())
        bmesh.ops.transform(bm, matrix=Matrix.Translation((p1 + p2) / 2) @ rot.to_matrix().to_4x4(), verts=v)
        finish(v, mat, bone, uv_scale=6.0)


def sole(cx, outline, z0, z1, mat, bone):
    """Подошва сандалии: контур стопы (x, y) выдавлен от z0 до z1."""
    bot = [bm.verts.new((cx + x, y, z0)) for x, y in outline]
    top = [bm.verts.new((cx + x, y, z1)) for x, y in outline]
    bm.faces.new(top)
    bm.faces.new(list(reversed(bot)))
    n = len(outline)
    for k in range(n):
        bm.faces.new((bot[k], bot[(k + 1) % n], top[(k + 1) % n], top[k]))
    finish(bot + top, mat, bone)


def glow_quad(center, w, h, bone):
    """Плоский ореол глаза лицом вперёд (-Y), UV во всю площадь."""
    cx, cy, cz = center
    quad = [bm.verts.new((cx - w / 2, cy, cz - h / 2)), bm.verts.new((cx + w / 2, cy, cz - h / 2)),
            bm.verts.new((cx + w / 2, cy, cz + h / 2)), bm.verts.new((cx - w / 2, cy, cz + h / 2))]
    f = bm.faces.new(quad)
    for loop, uv in zip(f.loops, ((0, 0), (1, 0), (1, 1), (0, 1))):
        loop[uv_layer].uv = uv
    finish(quad, FLAME, bone, keep_uv=True)


# Череп лепится по горизонтальным сечениям снизу вверх: (z, полуширина, вперёд, назад). Между строками --
# плавная интерполяция; сечение -- суперэллипс (не круг и не квадрат), спереди и сзади разная глубина.
SKULL_PROFILE = [
    (1.572, 0.012, 0.050, 0.010),   # низ подбородка
    (1.582, 0.034, 0.072, 0.024),   # подбородок
    (1.598, 0.052, 0.086, 0.034),   # нижняя челюсть
    (1.616, 0.062, 0.094, 0.042),   # нижние зубы
    (1.632, 0.066, 0.100, 0.050),   # смыкание зубов
    (1.650, 0.074, 0.104, 0.072),   # верхняя челюсть выступает вперёд
    (1.672, 0.099, 0.097, 0.094),   # скулы -- самое широкое место лица
    (1.700, 0.093, 0.094, 0.106),   # глаза
    (1.726, 0.094, 0.100, 0.114),   # надбровья
    (1.760, 0.099, 0.093, 0.120),   # свод -- самое широкое место черепа
    (1.800, 0.087, 0.078, 0.106),
    (1.828, 0.060, 0.052, 0.076),
    (1.845, 0.022, 0.018, 0.030),   # макушка
]
SKULL_Y = -0.004                     # центр сечений по глубине
JAW_Z = 1.626                        # ниже -- нижняя челюсть (кость Jaw)
EYES = [Vector((s * 0.043, 0, 1.703)) for s in (L, R)]
EYE_HOLE = (0.025, 0.024)            # полуоси глазницы (x, z)
NOSE = (Vector((0, 0, 1.664)), 0.009, 0.016)  # носовое отверстие: груша, шире внизу


def profile_at(z):
    rows = SKULL_PROFILE
    for a, b in zip(rows, rows[1:]):
        if a[0] <= z <= b[0]:
            t = (z - a[0]) / (b[0] - a[0])
            t = t * t * (3 - 2 * t)
            return [a[k] + (b[k] - a[k]) * t for k in range(1, 4)]
    return list(rows[-1][1:])


def head(levels=34, around=40, power=2.6):
    """Череп одним куском из сечений; глазницы и носовая полость -- дыры, за ними тёмная полость черепа;
    в глубине глазниц светятся миндалины с ореолом. Низ лица (ниже JAW_Z) привязан к кости Jaw."""
    z0, z1 = SKULL_PROFILE[0][0], SKULL_PROFILE[-1][0]
    rings = []
    for i in range(levels):
        z = z0 + (z1 - z0) * i / (levels - 1)
        w, f, b = profile_at(z)
        ring_ = []
        for k in range(around):
            a = 2 * math.pi * k / around                 # 0 -- лицо (-Y), по часовой
            sx, cy = math.sin(a), math.cos(a)
            ex = math.copysign(abs(sx) ** (2 / power), sx)
            ey = math.copysign(abs(cy) ** (2 / power), cy)
            depth = f if cy > 0 else b
            x, y = w * ex, SKULL_Y - depth * ey
            # скулы чуть выступают вбок-вперёд, виски впалые
            side = abs(sx)
            if 1.66 < z < 1.69:
                x *= 1 + 0.06 * side * max(0, cy)
            if 1.715 < z < 1.775:
                x *= 1 - 0.05 * side * (1 - abs(cy))
            ring_.append(bm.verts.new((x, y, z)))
        rings.append(ring_)
    faces = []
    for r0, r1 in zip(rings, rings[1:]):
        for k in range(around):
            faces.append(bm.faces.new((r0[k], r0[(k + 1) % around], r1[(k + 1) % around], r1[k])))
    bottom, top = bm.verts.new((0, SKULL_Y + 0.02, z0 - 0.004)), bm.verts.new((0, SKULL_Y + 0.01, z1 + 0.004))
    for k in range(around):
        faces.append(bm.faces.new((rings[0][(k + 1) % around], rings[0][k], bottom)))
        faces.append(bm.faces.new((rings[-1][k], rings[-1][(k + 1) % around], top)))
    verts = [v for r in rings for v in r] + [bottom, top]

    # глазницы и нос -- вырезаем грани передней части, чей центр попал в эллипс
    def in_hole(c):
        if c.y > SKULL_Y - 0.04:
            return False
        for e in EYES:
            if ((c.x - e.x) / EYE_HOLE[0]) ** 2 + ((c.z - e.z) / EYE_HOLE[1]) ** 2 < 1:
                return True
        n, rx, rz = NOSE
        t = (c.z - n.z) / rz                             # -1 внизу .. 1 вверху
        return abs(t) < 1 and abs(c.x) < rx * (1.15 - 0.55 * t) * math.sqrt(1 - t * t) + 0.002
    holes = [f for f in faces if in_hole(f.calc_center_median())]
    bmesh.ops.delete(bm, geom=holes, context="FACES_ONLY")
    verts = [v for v in verts if v.is_valid and v.link_faces]
    # края дыр -- на ровный эллипс (иначе ступеньки по сетке)
    for v in verts:
        if not v.is_boundary:
            continue
        for e in EYES:
            dx, dz = (v.co.x - e.x) / EYE_HOLE[0], (v.co.z - e.z) / EYE_HOLE[1]
            r = math.hypot(dx, dz)
            if 0.5 < r < 1.6:
                v.co.x, v.co.z = e.x + dx / r * EYE_HOLE[0], e.z + dz / r * EYE_HOLE[1]
        n, rx, rz = NOSE                             # нос -- груша, шире внизу
        dx, dz = v.co.x / rx, (v.co.z - n.z) / rz
        r = math.hypot(dx, dz)
        if 0.4 < r < 1.8 and abs(v.co.x) < 0.02:
            ux, uz = dx / r, dz / r
            v.co.x, v.co.z = ux * rx * (1.15 - 0.55 * uz), n.z + uz * rz

    finish(verts, BONE, "Head")
    head_i, jaw_i = BONE_NAMES.index("Head"), BONE_NAMES.index("Jaw")
    for v in verts:
        if v.co.z < JAW_Z:
            del v[deform][head_i]
            v[deform][jaw_i] = 1.0
    # лицо (передние грани у зубов) -- текстура с зубами, проекция спереди
    for f in {f for v in verts for f in v.link_faces}:
        f.normal_update()
        if f.normal.y < -0.4 and f.calc_center_median().z < 1.66:
            f.material_index = SKULL
            for loop in f.loops:
                c = loop.vert.co
                loop[uv_layer].uv = (0.5 + c.x / 0.22, (c.z - 1.57) / 0.1)

    # тёмная полость черепа за глазницами и носом
    blob((0.072, 0.07, 0.07), CAVITY, "Head", segs=10, rings=6, loc=(0, SKULL_Y + 0.012, 1.705))
    for e in EYES:
        s = 1 if e.x > 0 else -1
        y_face = SKULL_Y - profile_at(e.z)[1] * 0.97
        blob((0.013, 0.004, 0.0055), EYE, "Head", segs=8, rings=4, loc=(e.x, y_face + 0.026, e.z - 0.003),
             rot=(0, s * 18, 0))
        glow_quad(Vector((e.x, y_face + 0.022, e.z - 0.003)), 0.04, 0.026, "Head")


# --- голова: череп из сечений, глазницы и нос -- дыры, глаза светятся в глубине
head()

# --- позвоночник, рёбра, грудина, таз
rod((0, 0, 1.0), (0, 0, 1.5), 0.022, BONE, "Spine", segs=6)
rod((0, 0, 1.48), (0, 0, 1.6), 0.02, BONE, "Neck", segs=6)
for z in (1.52, 1.56):
    blob((0.03, 0.026, 0.012), BONE, "Neck", segs=6, rings=3, loc=(0, 0.004, z))           # позвонки шеи
for i, (z, rad) in enumerate(((1.42, 0.13), (1.37, 0.14), (1.32, 0.14), (1.27, 0.13), (1.22, 0.11))):
    ring((0, -0.02, z), rad, 0.014, BONE, "Chest" if i < 3 else "Spine", segs=12, scale=(1, 0.75, 1), arc=320)
rod((0, -0.122, 1.43), (0, -0.12, 1.24), 0.016, BONE, "Chest", segs=5)                     # грудина
for s in (L, R):
    rod((s * 0.02, -0.06, 1.46), (s * 0.17, 0.0, 1.455), 0.013, BONE, "Chest", segs=5)     # ключица
blob((0.14, 0.09, 0.07), BONE, "Hips", segs=10, rings=5, loc=(0, 0, 0.99))                 # таз

# --- руки и ноги
for s, side in ((L, "L"), (R, "R")):
    rod((s * 0.18, 0, 1.45), (s * 0.46, 0, 1.45), 0.024, BONE, f"UpperArm.{side}", segs=6)
    blob(0.026, BONE, f"LowerArm.{side}", segs=6, rings=4, loc=(s * 0.46, 0, 1.45))         # локоть
    rod((s * 0.46, 0, 1.45), (s * 0.72, 0, 1.45), 0.02, BONE, f"LowerArm.{side}", segs=6)
    blob((0.04, 0.03, 0.015), BONE, f"Hand.{side}", segs=6, rings=4, loc=(s * 0.765, 0, 1.45))
    for dy in (-0.018, 0.0, 0.018):
        rod((s * 0.8, dy, 1.45), (s * 0.86, dy - 0.004, 1.43), 0.008, BONE, f"Hand.{side}", r2=0.002, segs=4)
    rod((s * 0.79, -0.03, 1.45), (s * 0.82, -0.06, 1.44), 0.008, BONE, f"Hand.{side}", r2=0.002, segs=4)

    rod((s * 0.10, 0, 0.97), (s * 0.10, 0, 0.55), 0.03, BONE, f"UpperLeg.{side}", segs=6)
    rod((s * 0.10, 0, 0.54), (s * 0.10, 0, 0.1), 0.024, BONE, f"LowerLeg.{side}", segs=6)
    blob(0.04, BONE, f"LowerLeg.{side}", segs=8, rings=5, loc=(s * 0.10, -0.01, 0.54))     # колено

    # стопа: пятка, плюсна, пальцы -- кость; сандалия: подошва по форме стопы, ремешки
    x = s * 0.10
    blob((0.028, 0.038, 0.03), BONE, f"Foot.{side}", segs=6, rings=4, loc=(x, 0.012, 0.05))
    for dx in (-0.024, 0.0, 0.024):
        rod((x + dx * 0.6, -0.02, 0.058), (x + dx, -0.12, 0.03), 0.009, BONE, f"Foot.{side}", segs=5)
        rod((x + dx, -0.12, 0.03), (x + dx * 1.15, -0.168, 0.022), 0.007, BONE, f"Foot.{side}", r2=0.004, segs=4)
    outline = [(0, 0.055), (s * 0.034, 0.04), (s * 0.044, -0.05), (s * 0.052, -0.13), (s * 0.04, -0.18),
               (0, -0.192), (-s * 0.036, -0.178), (-s * 0.046, -0.12), (-s * 0.036, -0.04), (-s * 0.03, 0.038)]
    sole(x, outline if s == L else list(reversed(outline)), 0.0, 0.014, STRAP, f"Foot.{side}")
    ring((x, -0.1, 0.03), 0.042, 0.007, STRAP, f"Foot.{side}", segs=8, scale=(1.15, 1, 0.7), rot=(90, 0, 0))
    ring((x, -0.035, 0.05), 0.04, 0.007, STRAP, f"Foot.{side}", segs=8, scale=(1.1, 1, 0.9), rot=(70, 0, 0))
    ring((x, 0.0, 0.11), 0.034, 0.008, STRAP, f"LowerLeg.{side}", segs=8)                 # ремешок на щиколотке

    # кожаная броня рук и ног
    rod((s * 0.56, 0, 1.45), (s * 0.71, 0, 1.45), 0.042, LEATHER, f"LowerArm.{side}", r2=0.036, segs=8)  # наруч
    ring((s * 0.6, 0, 1.45), 0.044, 0.006, STRAP, f"LowerArm.{side}", segs=8, rot=(0, 90, 0))
    ring((s * 0.68, 0, 1.45), 0.039, 0.006, STRAP, f"LowerArm.{side}", segs=8, rot=(0, 90, 0))
    rod((s * 0.10, 0, 0.44), (s * 0.10, 0, 0.17), 0.048, LEATHER, f"LowerLeg.{side}", r2=0.04, segs=8)  # обмотка
    for z in (0.38, 0.26):
        ring((s * 0.10, 0, z), 0.05 - (0.38 - z) * 0.03, 0.006, STRAP, f"LowerLeg.{side}", segs=8)

    # наплечник: две изогнутые пластины, первая продолжает накидку, вторая лежит на плече
    blob((0.125, 0.12, 0.08), LEATHER, f"Shoulder.{side}", segs=10, rings=5,
         loc=(s * 0.19, 0, 1.48), rot=(0, s * 20, 0))                                      # наплечник до ворота
    blob((0.085, 0.09, 0.05), STRAP, f"UpperArm.{side}", segs=10, rings=5,
         loc=(s * 0.235, 0, 1.515), rot=(0, s * 25, 0))                                    # второй слой
    for dy in (-0.06, 0.06):
        blob(0.011, METAL, f"Shoulder.{side}", segs=5, rings=3, loc=(s * 0.2, dy, 1.548))   # заклёпки

    # подтяжки: спереди по рёбрам до пояса, сзади крест-накрест
    strap([(s * 0.085, -0.07, 1.5), (s * 0.085, -0.13, 1.43), (s * 0.085, -0.15, 1.33),
           (s * 0.082, -0.142, 1.21), (s * 0.08, -0.125, 1.1)], 0.034, 0.008, STRAP, "Chest")
    box((0.04, 0.012, 0.03), METAL, "Hips", loc=(s * 0.08, -0.13, 1.105))                  # зажим подтяжки
    strap([(s * 0.085, 0.06, 1.5), (s * 0.03, 0.11, 1.32), (-s * 0.08, 0.115, 1.1)], 0.032, 0.008, STRAP, "Chest")

    # полы кожаной юбки: висят прямо из-под пояса, спереди, сзади и сбоку
    for fy, rx in ((-0.12, -6), (0.115, 6)):
        box((0.11, 0.016, 0.23), LEATHER, f"UpperLeg.{side}", loc=(s * 0.075, fy, 0.935), rot=(rx, 0, 0))
    box((0.016, 0.1, 0.2), LEATHER, f"UpperLeg.{side}", loc=(s * 0.155, 0.0, 0.95), rot=(0, s * -8, 0))

# --- корпус брони: накидка на плечи от шеи (с ней сходятся наплечники), ворот, пояс с пряжкой
yoke = bmesh.ops.create_cone(bm, cap_ends=False, segments=12, radius1=0.27, radius2=0.072, depth=0.1)["verts"]
place(yoke, scale=(1, 0.6, 1))
place(yoke, loc=(0, 0, 1.485))
finish(yoke, LEATHER, "Chest")
ring((0, 0, 1.535), 0.074, 0.016, STRAP, "Chest", segs=10, scale=(1, 0.7, 1))                # ворот
ring((0, 0, 1.08), 0.15, 0.025, LEATHER, "Hips", segs=12, scale=(1, 0.8, 1.4))               # пояс
box((0.06, 0.02, 0.05), METAL, "Hips", loc=(0, -0.125, 1.08))                                 # пряжка
box((0.26, 0.02, 0.16), CLOTH, "Hips", loc=(0, 0.125, 0.98), rot=(8, 0, 0))                    # рваная ткань сзади

# --- меч в правой руке (вдоль -Y от кисти)
rod((-0.79, 0.04, 1.45), (-0.79, -0.06, 1.45), 0.014, STRAP, "Sword", segs=6)                # рукоять
blob(0.02, METAL, "Sword", segs=6, rings=4, loc=(-0.79, 0.055, 1.45))                        # навершие
box((0.18, 0.025, 0.025), METAL, "Sword", loc=(-0.79, -0.07, 1.45))                           # гарда
v = box((0.06, 0.7, 0.012), STEEL, "Sword", loc=(-0.79, -0.43, 1.45))                        # клинок
tip = [x for x in v if x.co.y < -0.7]
for x in tip:
    x.co.x = -0.79

bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0001)
bmesh.ops.recalc_face_normals(bm, faces=[f for f in bm.faces if f.material_index != FLAME])
mesh = bpy.data.meshes.new("SkeletonWarrior")
bm.to_mesh(mesh)
bm.free()
for m in MATS:
    mesh.materials.append(m)
for p in mesh.polygons:
    p.use_smooth = p.material_index not in FLAT   # сглаживание по вершинам, как гуро-шейдинг PS1
body = bpy.data.objects.new("SkeletonWarrior", mesh)
scene.collection.objects.link(body)
for name in BONE_NAMES:
    body.vertex_groups.new(name=name)

# ---------------------------------------------------------------- риг
arm_data = bpy.data.armatures.new("SkeletonWarriorRig")
rig = bpy.data.objects.new("SkeletonWarriorRig", arm_data)
scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="EDIT")
for name, (head, tail, parent) in BONES.items():
    eb = arm_data.edit_bones.new(name)
    eb.head, eb.tail = head, tail
    eb.roll = 0
    if parent:
        eb.parent = arm_data.edit_bones[parent]
        eb.use_connect = False
bpy.ops.object.mode_set(mode="OBJECT")
body.parent = rig
mod = body.modifiers.new("Armature", "ARMATURE")
mod.object = rig

# ---------------------------------------------------------------- анимации
REST = {b.name: b.matrix_local.to_3x3() for b in arm_data.bones}
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))


def q(bone, *rots):
    """Поворот кости, заданный осями мира в позе покоя: q(bone, (X, 30), (Z, -10)) -- сначала первый."""
    total = Quaternion()
    inv = REST[bone].inverted()
    for axis, deg in rots:
        total = Quaternion(inv @ axis, math.radians(deg)) @ total
    return total


def loc(bone, world):
    return REST[bone].inverted() @ Vector(world)


def base_pose():
    """Боевая стойка: руки опущены, меч вперёд-вниз, колени чуть согнуты."""
    return {
        "UpperArm.L": [(Y, 72), (X, -10)], "LowerArm.L": [(Z, -25)],
        "UpperArm.R": [(Y, -70), (X, -20)], "LowerArm.R": [(Z, 35)], "Hand.R": [(X, -65)],
        "UpperLeg.L": [(X, -8)], "LowerLeg.L": [(X, 14)], "Foot.L": [(X, -6)],
        "UpperLeg.R": [(X, -8)], "LowerLeg.R": [(X, 14)], "Foot.R": [(X, -6)],
        "Spine": [(X, 6)], "Head": [(X, -4)],
    }


def key_pose(frame, extra, hips=(0, 0, 0)):
    pose = base_pose()
    for b, rots in extra.items():
        pose[b] = pose.get(b, []) + rots
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
        pb.rotation_quaternion = q(pb.name, *pose.get(pb.name, []))
        pb.keyframe_insert("rotation_quaternion", frame=frame)
        pb.location = loc(pb.name, hips) if pb.name == "Hips" else Vector()
        pb.keyframe_insert("location", frame=frame)


def action(name, keys, loop):
    rig.animation_data_create()
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    rig.animation_data.action = act
    for frame, extra, hips in keys:
        key_pose(frame, extra, hips)
    if loop:  # последний кадр = первый
        f0, e0, h0 = keys[0]
        key_pose(keys[-1][0] + (keys[1][0] - keys[0][0]), e0, h0)
    act.frame_range = (keys[0][0], max(k[0] for k in keys) + ((keys[1][0] - keys[0][0]) if loop else 0))
    return act


def sway(t):
    return math.sin(t * 2 * math.pi)


ACTIONS = []
# Idle: дыхание грудью, покачивание головы, челюсть клацает
ACTIONS.append(action("Idle", [
    (1,  {"Chest": [(X, -2)], "Jaw": [(X, 0)], "Head": [(Z, 4)]}, (0, 0, 0)),
    (13, {"Chest": [(X, 3)], "Jaw": [(X, 14)], "Head": [(Z, -3)]}, (0, 0, -0.015)),
    (25, {"Chest": [(X, -1)], "Jaw": [(X, 2)], "Head": [(Z, -5)]}, (0, 0, 0)),
    (37, {"Chest": [(X, 2)], "Jaw": [(X, 10)], "Head": [(Z, 2)]}, (0, 0, -0.01)),
], loop=True))

# Walk: шаг 1 с, ноги вперёд-назад, руки в противофазе, таз подпрыгивает
def walk(phase):
    s = sway(phase)
    return {
        "UpperLeg.L": [(X, -28 * s)], "LowerLeg.L": [(X, 30 * max(0, -sway(phase + 0.15)) + 6)],
        "UpperLeg.R": [(X, 28 * s)], "LowerLeg.R": [(X, 30 * max(0, sway(phase + 0.15)) + 6)],
        "Foot.L": [(X, 10 * s)], "Foot.R": [(X, -10 * s)],
        "UpperArm.L": [(X, 22 * s)], "Hips": [(Z, 6 * s)], "Chest": [(Z, -8 * s)],
        "Jaw": [(X, 6 + 6 * s)],
    }
ACTIONS.append(action("Walk", [(1 + i * 6, walk(i / 4), (0, 0, -0.03 * abs(sway(i / 4)))) for i in range(4)], loop=True))

# Attack: замах из-за головы и удар сверху вниз с поворотом корпуса
ACTIONS.append(action("Attack", [
    (1,  {}, (0, 0, 0)),
    (8,  {"UpperArm.R": [(X, -130)], "LowerArm.R": [(Z, 50)], "Chest": [(Z, -25), (X, -8)], "Head": [(X, -8)],
          "Jaw": [(X, 18)]}, (0, 0.03, 0)),
    (12, {"UpperArm.R": [(X, -140)], "LowerArm.R": [(Z, 40)], "Chest": [(Z, -30), (X, -10)], "Jaw": [(X, 20)]},
         (0, 0.04, 0)),
    (16, {"UpperArm.R": [(X, 20)], "LowerArm.R": [(Z, -10)], "Hand.R": [(X, 30)], "Chest": [(Z, 25), (X, 20)],
          "UpperLeg.R": [(X, -25)], "LowerLeg.R": [(X, 20)]}, (0, -0.1, -0.06)),
    (24, {"UpperArm.R": [(X, 10)], "Chest": [(Z, 15), (X, 12)]}, (0, -0.06, -0.04)),
    (32, {}, (0, 0, 0)),
], loop=False))

# Hit: отброс корпуса назад, голова запрокидывается
ACTIONS.append(action("Hit", [
    (1, {}, (0, 0, 0)),
    (4, {"Chest": [(X, -22)], "Spine": [(X, -10)], "Head": [(X, -25)], "Jaw": [(X, 25)],
         "UpperArm.L": [(X, 30)], "UpperArm.R": [(X, 20)]}, (0, 0.08, 0)),
    (12, {"Chest": [(X, -5)], "Head": [(X, -6)], "Jaw": [(X, 8)]}, (0, 0.03, 0)),
    (16, {}, (0, 0, 0)),
], loop=False))

# Death: колени подгибаются, падение на спину, голова и челюсть отваливаются вбок
ACTIONS.append(action("Death", [
    (1,  {}, (0, 0, 0)),
    (8,  {"Chest": [(X, -15)], "Head": [(X, -20)], "LowerLeg.L": [(X, 40)], "LowerLeg.R": [(X, 50)],
          "UpperLeg.L": [(X, -30)], "UpperLeg.R": [(X, -35)], "Jaw": [(X, 25)]}, (0, 0.05, -0.2)),
    (18, {"Hips": [(X, -60)], "Chest": [(X, -20)], "Head": [(X, -25), (Z, 30)], "UpperLeg.L": [(X, -50)],
          "UpperLeg.R": [(X, -40)], "LowerLeg.L": [(X, 60)], "LowerLeg.R": [(X, 50)],
          "UpperArm.L": [(X, -60)], "UpperArm.R": [(X, -50)], "Jaw": [(X, 30)]}, (0, 0.35, -0.6)),
    (26, {"Hips": [(X, -88)], "Chest": [(X, -2)], "Head": [(X, -10), (Z, 60)], "UpperLeg.L": [(X, 12)],
          "UpperLeg.R": [(X, 18)], "LowerLeg.L": [(X, 10)], "LowerLeg.R": [(X, 5)],
          "UpperArm.L": [(X, 30)], "UpperArm.R": [(X, 40)], "LowerArm.L": [(Z, 10)], "Hand.R": [(X, -20)],
          "Jaw": [(X, 35)]}, (0, 0.55, -0.86)),
    (36, {"Hips": [(X, -90)], "Head": [(Z, 70)], "UpperLeg.L": [(X, 10)], "UpperLeg.R": [(X, 16)],
          "LowerLeg.L": [(X, 4)], "LowerLeg.R": [(X, 2)], "UpperArm.L": [(X, 35)], "UpperArm.R": [(X, 45)],
          "Hand.R": [(X, -25)], "Jaw": [(X, 40)]}, (0, 0.58, -0.88)),
], loop=False))

rig.animation_data.action = ACTIONS[0]
scene.frame_start, scene.frame_end = 1, 48

tris = sum(len(p.vertices) - 2 for p in mesh.polygons)
print(f"[Skeleton] треугольников: {tris}, вершин: {len(mesh.vertices)}, костей: {len(BONE_NAMES)}, "
      f"анимаций: {[a.name for a in ACTIONS]}")

# ---------------------------------------------------------------- сохранение и экспорт
os.makedirs(os.path.dirname(BLEND), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=BLEND, compress=True)
if os.path.exists(BLEND + "1"):
    os.remove(BLEND + "1")   # резервная копия Blender не нужна: модель пересобирается скриптом

bpy.ops.object.select_all(action="DESELECT")
body.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
fbx = os.path.join(OUT_DIR, "SkeletonWarrior.fbx")
bpy.ops.export_scene.fbx(
    filepath=fbx, use_selection=True, object_types={"ARMATURE", "MESH"},
    apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
    add_leaf_bones=False, mesh_smooth_type="FACE", path_mode="STRIP",
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
print(f"[Skeleton] FBX: {fbx}")
print(f"[Skeleton] blend: {BLEND}")

# ---------------------------------------------------------------- превью
if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
    scene.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in engines else "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x, scene.render.resolution_y = 360, 480
    scene.render.film_transparent = False
    world = bpy.data.worlds.new("Night")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.03, 0.12, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.6
    scene.world = world
    for name, energy, col, pos in (("Key", 900, (0.75, 0.7, 1.0), (1.5, -2.5, 2.6)),
                                   ("Rim", 600, (0.4, 0.5, 1.0), (-2, 2, 2.2)),
                                   ("Fill", 120, (1.0, 0.8, 0.6), (-2, -2, 0.6))):
        ld = bpy.data.lights.new(name, "POINT")
        ld.energy, ld.color = energy, col
        lo = bpy.data.objects.new(name, ld)
        lo.location = pos
        scene.collection.objects.link(lo)
    cam_data = bpy.data.cameras.new("Cam")
    cam_data.lens = 50
    cam = bpy.data.objects.new("Cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam

    def shot(path, angle_deg, frame, dist=3.6, height=1.1, target_z=0.95):
        a = math.radians(angle_deg)
        cam.location = (math.sin(a) * dist, -math.cos(a) * dist, height)
        direction = Vector((0, 0, target_z)) - cam.location
        cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
        scene.frame_set(frame)
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)

    rig.animation_data.action = ACTIONS[0]
    for ang in (0, 35, 90):
        shot(os.path.join(PREVIEW, f"view_{ang}.png"), ang, 1)
    shot(os.path.join(PREVIEW, "face.png"), 0, 1, dist=0.75, height=1.72, target_z=1.7)
    shot(os.path.join(PREVIEW, "face_side.png"), 70, 1, dist=0.75, height=1.72, target_z=1.7)
    for act, frames in ((ACTIONS[1], (1, 7, 13, 19)), (ACTIONS[2], (1, 10, 16, 24)),
                        (ACTIONS[3], (1, 4)), (ACTIONS[4], (1, 10, 20, 36))):
        rig.animation_data.action = act
        for f in frames:
            shot(os.path.join(PREVIEW, f"{act.name}_{f:02d}.png"), 40, f)
    print(f"[Skeleton] превью: {PREVIEW}")
