"""Скелет-рыцарь: готовый скелет из art/enemies/source/Skeleton + железная броня + синие глаза + меч.

  blender -b --factory-startup -P tools/blender/skeleton_knight.py -- [--preview build/enemies/knight]

Исходники (не меняются):
  art/enemies/source/Skeleton/skeleton.blend, base.png  -- скачанный скелет (меш + риг metarig)
  art/weapons/LowPolyWeaponPack/*.fbx                    -- оружие Kickin' It Studios
Результат:
  art/enemies/skeleton_knight.blend
  Assets/Game/Assets/enemies/SkeletonKnight/SkeletonKnight.fbx  -- для Unity (риг Humanoid)
  Assets/Game/Assets/enemies/SkeletonKnight/Textures/*.png

Что делает скрипт:
  - переносит меш и риг скелета в метры (рост HEIGHT, стопы на z = 0, лицо к -Y), кости получают имена,
    понятные автонастройке Humanoid в Unity (Hips, Spine, Chest, UpperChest, Head, UpperArm.L, ...);
  - глаза: на текстуре скелета тёмные глазницы перекрашиваются в синее свечение (ядро светлее), рядом
    кладётся маска свечения SK_Bone_Emission.png -- её подхватывает EnemyModelImporter;
  - броня (отдельный меш Armor): железные кираса, наплечники, наручи, поножи, пояс с кожаными полами --
    каждая деталь жёстко на своей кости;
  - оружие -- отдельный объект на кости WeaponSocket (ребёнок Hand.R): в Unity это дочерний объект руки,
    его можно заменить другим оружием.
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Euler, Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "art", "enemies", "source", "Skeleton")
WEAPONS = os.path.join(ROOT, "art", "weapons", "LowPolyWeaponPack")
OUT_DIR = os.path.join(ROOT, "Assets", "Game", "Assets", "enemies", "SkeletonKnight")
TEX_DIR = os.path.join(OUT_DIR, "Textures")
BLEND = os.path.join(ROOT, "art", "enemies", "skeleton_knight.blend")
HEIGHT = 1.85
WEAPON = "Arming_Sword"

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PREVIEW = os.path.abspath(argv[argv.index("--preview") + 1]) if "--preview" in argv else None
MEASURE = "--measure" in argv

RENAME = {
    "spine": "Hips", "spine.001": "Spine", "spine.002": "Chest", "spine.003": "UpperChest", "spine.004": "Head",
    "shoulder": "Shoulder", "upper_arm": "UpperArm", "forearm": "LowerArm", "hand": "Hand",
    "pelvis": "Pelvis", "thigh": "UpperLeg", "shin": "LowerLeg", "foot": "Foot",
}


def new_name(old):
    base, _, side = old.partition(".")
    if old in RENAME:
        return RENAME[old]
    return f"{RENAME[base]}.{side}"


# ---------------------------------------------------------------- исходный скелет -> метры
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
with bpy.data.libraries.load(os.path.join(SRC, "skeleton.blend")) as (src, dst):
    dst.objects = ["Cube", "metarig"]
src_body, src_rig = dst.objects
for o in (src_body, src_rig):
    scene.collection.objects.link(o)
src_rig.data.pose_position = "REST"
bpy.context.view_layer.update()

rest = bpy.data.meshes.new_from_object(src_body.evaluated_get(bpy.context.evaluated_depsgraph_get()))
rest_world = [src_body.matrix_world @ v.co for v in rest.vertices]
z_min = min(c.z for c in rest_world)
z_max = max(c.z for c in rest_world)
SCALE = HEIGHT / (z_max - z_min)
TO_M = Matrix.Translation((0, 0, -z_min * SCALE)) @ Matrix.Scale(SCALE, 4)   # мир исходника -> метры

# риг
arm_data = bpy.data.armatures.new("Rig")
rig = bpy.data.objects.new("Rig", arm_data)
scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="EDIT")
for b in src_rig.data.bones:
    eb = arm_data.edit_bones.new(new_name(b.name))
    eb.head = TO_M @ (src_rig.matrix_world @ b.head_local)
    eb.tail = TO_M @ (src_rig.matrix_world @ b.tail_local)
    z_axis = (src_rig.matrix_world.to_3x3() @ b.matrix_local.to_3x3().col[2]).normalized()
    eb.align_roll(z_axis)
for b in src_rig.data.bones:
    if b.parent:
        arm_data.edit_bones[new_name(b.name)].parent = arm_data.edit_bones[new_name(b.parent.name)]
bpy.ops.object.mode_set(mode="OBJECT")
BONE = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in arm_data.bones}

# тело: меш в позе покоя, в метрах, группы вершин -- по новым именам костей
body_mesh = rest
body_mesh.name = "SkeletonBody"
body_mesh.transform(TO_M @ src_body.matrix_world)
body = bpy.data.objects.new("Body", body_mesh)
scene.collection.objects.link(body)
for g in body.vertex_groups:            # веса уже в меше, переименовываем группы
    g.name = new_name(g.name)
src_img_path = os.path.join(SRC, "base.png")
bpy.data.objects.remove(src_body)
bpy.data.objects.remove(src_rig)


BONE_NAMES = [b.name for b in arm_data.bones]


def bone_pos(name, t=0.0):
    """Точка на кости: t = 0 -- голова, 1 -- хвост."""
    h, tl = BONE[name]
    return h.lerp(tl, t)


# ---------------------------------------------------------------- текстуры (PS1: 64x64, грязь, 15 бит, дизеринг)
rng = np.random.default_rng(11)


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
GRIME = np.array([0.40, 0.36, 0.26])    # буро-зелёная грязь


def ps1(rgb, grime=0.4):
    """Огрубление «под PS1»: пятна грязи, зерно, цветовой шум, 15-битный цвет с упорядоченным дизерингом."""
    n = rgb.shape[0]
    dirt = noise(n, 1.0)[..., None] * grime
    rgb = rgb * (1 - dirt) + GRIME * rgb.mean(-1, keepdims=True) * dirt
    rgb = rgb * (0.84 + 0.32 * rng.random((n, n, 1)))          # зерно
    rgb = rgb + (rng.random((n, n, 3)) - 0.5) * 0.06           # грязный цветовой шум
    levels = 31                                                # 5 бит на канал, как у PS1
    dither = np.tile(BAYER, (n // 4, n // 4))[..., None] / levels
    return np.floor(np.clip(rgb + dither, 0, 1) * levels + 0.5) / levels


def save_texture(name, rgb):
    h, w = rgb.shape[:2]
    img = bpy.data.images.new(name, w, h)
    px = np.ones((h, w, 4))
    px[:, :, :3] = np.clip(rgb, 0, 1)
    img.pixels = px[::-1].ravel()   # Blender хранит строки снизу вверх
    os.makedirs(TEX_DIR, exist_ok=True)
    img.filepath_raw = os.path.join(TEX_DIR, name + ".png")
    img.file_format = "PNG"
    img.save()
    return img


N = 64


def rivets(base, rows, step=8, color=(0.62, 0.6, 0.55)):
    """Заклёпки: светлая точка с тенью снизу-справа, ряды по v (доли высоты текстуры, 0 -- низ)."""
    for v in rows:
        y = int((1 - v) * N) % N
        for x in range(step // 2, N, step):
            base[(y + 1) % N, (x + 1) % N] *= 0.35
            base[y % N, x] = color
    return base


def tex_iron():
    """Кованое железо: тёмно-серое, пятна ковки, ржавые подтёки, царапины."""
    n = noise(N, 1.0)
    base = np.array([0.36, 0.36, 0.37]) * (0.6 + 0.55 * n)[..., None]
    hammer = noise(N, 1.0)
    base[hammer > 0.62] *= 1.18                                # блики от ударов молота
    base[hammer < 0.25] *= 0.7
    rust = noise(N, 1.0) > 0.74
    base[rust] = base[rust] * 0.55 + np.array([0.30, 0.17, 0.09]) * 0.45
    for _ in range(14):                                        # царапины
        x, y = rng.integers(0, N, 2)
        for k in range(rng.integers(4, 10)):
            base[y % N, (x + k) % N] *= 1.45
            y += rng.integers(0, 2)
    return base


def tex_iron_band():
    """Железная полоса (пластина брони): кромки светлее, ряды заклёпок у верхнего и нижнего края."""
    base = tex_iron()
    base[:3] *= 1.35
    base[-3:] *= 0.55
    return rivets(base, (0.88, 0.14))


def tex_leather():
    n = noise(N, 1.0)
    base = np.array([0.30, 0.19, 0.11]) * (0.55 + 0.6 * n)[..., None]
    base[noise(N, 1.0) > 0.7] *= 0.5                           # потёртости
    base[:, ::16] *= 0.45                                      # швы
    base[::4, 7::16] = [0.50, 0.42, 0.28]                      # стежки
    return base


def tex_strap():
    n = noise(N, 1.0)
    base = np.array([0.17, 0.11, 0.07]) * (0.65 + 0.45 * n)[..., None]
    base[:, :3] *= 1.6
    base[:, -3:] *= 1.6                                        # светлые кромки ремня
    return base


def tex_steel():
    n = noise(N, 1.0)
    base = np.array([0.6, 0.62, 0.64]) * (0.75 + 0.3 * n)[..., None]
    base[noise(N, 1.0) > 0.68] *= np.array([0.7, 0.55, 0.45])  # ржавые пятна на клинке
    base[::5, :] *= 1.12                                       # следы заточки
    return base


def tex_grip():
    """Рукоять: обмотка кожаным шнуром -- косые полосы."""
    yy, xx = np.mgrid[0:N, 0:N]
    base = np.array([0.22, 0.13, 0.07]) * (0.7 + 0.4 * noise(N, 1.0))[..., None]
    base[((xx + yy * 2) % 8) < 2] *= 0.45
    return base


def tex_horn():
    """Рог: желтовато-серый, поперечные кольца, тёмные прожилки."""
    base = np.array([0.52, 0.46, 0.34]) * (0.75 + 0.35 * noise(N, 1.0))[..., None]
    base[::6] *= 0.62
    base[noise(N, 1.0) > 0.75] *= 0.7
    return base


EYE_R = (0.034, 0.03)   # полуоси глазницы, м


def eye_pixels(w, h):
    """Какие пиксели текстуры видны в глазницах: лучи спереди в голову (только грани кости Head).
    Центр глаза -- самое тёмное место глазницы с каждой стороны. Ответ: {(x, y): расстояние до центра 0..1}."""
    me = body_mesh
    me.calc_loop_triangles()
    head_i = body.vertex_groups["Head"].index
    head_v = {v.index for v in me.vertices if v.groups and max(v.groups, key=lambda g: g.weight).group == head_i}
    tris = [t for t in me.loop_triangles if all(i in head_v for i in t.vertices)]
    bvh = BVHTree.FromPolygons([v.co for v in me.vertices], [t.vertices for t in tris])
    uv = me.uv_layers.active.data
    hits = {}
    for x in np.arange(-0.12, 0.12, 0.0015):
        for z in np.arange(1.50, 1.72, 0.0015):
            loc, _, ti, _ = bvh.ray_cast(Vector((x, -1, z)), Vector((0, 1, 0)))
            if loc is None:
                continue
            t = tris[ti]
            a, b, c = (me.vertices[i].co for i in t.vertices)
            ua, ub, uc = (Vector((*uv[l].uv, 0)) for l in t.loops)
            p = barycentric_transform(loc, a, b, c, ua, ub, uc)
            key = (int(p.x % 1.0 * w), h - 1 - int(p.y % 1.0 * h))
            hits.setdefault(key, []).append((x, z))
    return hits


def tex_bone_with_eyes():
    """Текстура скелета x2 (пиксели те же) с синими глазами в глазницах; вторая картинка -- маска свечения."""
    src = bpy.data.images.load(src_img_path)
    w, h = src.size
    px = np.array(src.pixels[:]).reshape(h, w, 4)[::-1, :, :3]   # строки сверху вниз
    rgb = px.repeat(2, 0).repeat(2, 1)
    h, w = rgb.shape[:2]
    glow = np.zeros_like(rgb)
    hits = {k: (sum(a[0] for a in l) / len(l), sum(a[1] for a in l) / len(l)) for k, l in eye_pixels(w, h).items()}
    for s in (1, -1):
        dark = [(x, z) for (px_, py), (x, z) in hits.items() if 0.015 < s * x < 0.11 and rgb[py, px_].mean() < 0.12]
        cx = sum(x for x, _ in dark) / len(dark)
        cz = sum(z for _, z in dark) / len(dark)
        print(f"[Knight] глаз {'L' if s > 0 else 'R'}: x={cx:+.3f} z={cz:.3f} ({len(dark)} тёмных пикселей)")
        for (x_, y_), (x, z) in hits.items():
            if s * x <= 0:
                continue
            d = math.hypot((x - cx) / EYE_R[0], (z - cz) / EYE_R[1])
            lum = rgb[y_, x_].mean()
            if d < 0.28:
                col = np.array([0.80, 0.95, 1.0])                  # белёсое ядро
            elif d < 0.56:
                col = np.array([0.15, 0.55, 1.0])                  # синий огонь
            elif d < 1.0 and (lum < 0.3 or d < 0.75):
                col = np.array([0.02, 0.04, 0.12])                 # глубина глазницы: почти чёрная, с синевой
            else:
                continue
            rgb[y_, x_] = col
            glow[y_, x_] = col if d < 0.56 else col * 0.3
    return rgb, glow


def material(name, img=None, emission_img=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.85
    if img is not None:
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image, t.interpolation = img, "Closest"          # PSX: пиксели без сглаживания
        nt.links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
    if emission_img is not None:
        e = nt.nodes.new("ShaderNodeTexImage")
        e.image, e.interpolation = emission_img, "Closest"
        nt.links.new(e.outputs["Color"], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = 6.0
    return m


bone_rgb, bone_glow = tex_bone_with_eyes()
MATS = [
    material("SK_Bone", save_texture("SK_Bone", bone_rgb), save_texture("SK_Bone_Emission", bone_glow)),
    material("SK_Iron", save_texture("SK_Iron", ps1(tex_iron(), 0.35))),
    material("SK_IronBand", save_texture("SK_IronBand", ps1(tex_iron_band(), 0.35))),
    material("SK_Leather", save_texture("SK_Leather", ps1(tex_leather()))),
    material("SK_Strap", save_texture("SK_Strap", ps1(tex_strap()))),
    material("SK_Steel", save_texture("SK_Steel", ps1(tex_steel(), 0.25))),
    material("SK_Grip", save_texture("SK_Grip", ps1(tex_grip(), 0.3))),
    material("SK_Horn", save_texture("SK_Horn", ps1(tex_horn(), 0.3))),
]
BONE_MAT, IRON, BAND, LEATHER, STRAP, STEEL, GRIP, HORN = range(len(MATS))
FLAT = {IRON, BAND, STEEL}   # металл -- с гранями, без сглаживания

body_mesh.materials.clear()
body_mesh.materials.append(MATS[BONE_MAT])


# ---------------------------------------------------------------- геометрия брони
bm = deform = uv_layer = None


def start_mesh():
    global bm, deform, uv_layer
    bm = bmesh.new()
    deform = bm.verts.layers.deform.verify()
    uv_layer = bm.loops.layers.uv.verify()


def end_mesh(name):
    """bmesh -> объект на риге (модификатор Armature, группы вершин по именам костей)."""
    me = bpy.data.meshes.new(name)
    obj = bpy.data.objects.new(name, me)
    scene.collection.objects.link(obj)
    for b in BONE_NAMES:
        obj.vertex_groups.new(name=b)
    bm.to_mesh(me)
    bm.free()
    for m in MATS:
        me.materials.append(m)
    attach_to_rig(obj)
    return obj


def attach_to_rig(obj):
    obj.parent = rig
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = rig


def finish(verts, mat, bone, uv_scale=6.0, keep_uv=False):
    """Материал, жёсткая привязка к кости и кубическая проекция UV для только что созданных вершин."""
    gi = BONE_NAMES.index(bone)
    vs = set(verts)
    for v in vs:
        v[deform].clear()
        v[deform][gi] = 1.0
    for f in {f for v in vs for f in v.link_faces}:
        f.material_index = mat
        f.smooth = mat not in FLAT
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


def loft(sections, mat, bone, thick=0.012, segs=12, arc=(-180, 180), power=2.0, band=False, uv_scale=5.0):
    """Пластина-оболочка по сечениям снизу вверх: [(центр, полуширина, вперёд, назад)].
    Сечение -- суперэллипс в плоскости, поперечной пути: угол 0 -- перёд (-Y), 90 -- наружу (+X у торса и ног,
    от тела у левой руки). arc -- дуга в градусах (неполная -- открытая пластина с торцами).
    У пластины есть толщина: внешняя и внутренняя стенки, края закрыты. band=True -- UV по высоте 0..1
    (заклёпки текстуры SK_IronBand ложатся на кромки)."""
    full = arc[1] - arc[0] >= 360
    n_a = segs if full else segs + 1
    cs = [Vector(c) for c, *_ in sections]
    outer, inner = [], []
    for i, (c, w, front, back) in enumerate(sections):
        c = Vector(c)
        d = (cs[min(i + 1, len(cs) - 1)] - cs[max(i - 1, 0)]).normalized()
        u = Vector((0, 1, 0)).cross(d).normalized()
        v = d.cross(u).normalized()
        o_row, i_row = [], []
        for k in range(n_a):
            a = math.radians(arc[0] + (arc[1] - arc[0]) * k / segs)
            sa, ca = math.sin(a), math.cos(a)
            ex = math.copysign(abs(sa) ** (2 / power), sa)
            ey = math.copysign(abs(ca) ** (2 / power), ca)
            depth = front if ca > 0 else back
            off = u * (w * ex) - v * (depth * ey)
            r = max(off.length, 1e-4)
            o_row.append(bm.verts.new(c + off))
            i_row.append(bm.verts.new(c + off * max(0.0, 1 - thick / r)))
        outer.append(o_row)
        inner.append(i_row)
    faces = []
    rows = len(sections)
    ks = range(segs)
    nxt = (lambda k: (k + 1) % n_a) if full else (lambda k: k + 1)
    for i in range(rows - 1):
        for k in ks:
            faces.append(bm.faces.new((outer[i][k], outer[i][nxt(k)], outer[i + 1][nxt(k)], outer[i + 1][k])))
            faces.append(bm.faces.new((inner[i][k], inner[i + 1][k], inner[i + 1][nxt(k)], inner[i][nxt(k)])))
    for k in ks:   # нижняя и верхняя кромки
        faces.append(bm.faces.new((outer[0][k], inner[0][k], inner[0][nxt(k)], outer[0][nxt(k)])))
        faces.append(bm.faces.new((outer[-1][k], outer[-1][nxt(k)], inner[-1][nxt(k)], inner[-1][k])))
    if not full:   # боковые торцы
        for i in range(rows - 1):
            faces.append(bm.faces.new((outer[i][0], outer[i + 1][0], inner[i + 1][0], inner[i][0])))
            faces.append(bm.faces.new((outer[i][-1], inner[i][-1], inner[i + 1][-1], outer[i + 1][-1])))
    # UV: по кругу -- длина дуги, по высоте -- длина пути (или 0..1 у полосы)
    lengths = [0.0]
    for a, b in zip(cs, cs[1:]):
        lengths.append(lengths[-1] + (b - a).length)
    vmap = {}
    for i in range(rows):
        for k in range(n_a):
            vv = i / (rows - 1) if band else lengths[i] * uv_scale
            uu = k / segs * (arc[1] - arc[0]) / 360 * 2 * math.pi * sections[i][1] * uv_scale
            vmap[outer[i][k]] = vmap[inner[i][k]] = (uu, vv)
    for f in faces:
        for loop in f.loops:
            loop[uv_layer].uv = vmap[loop.vert]
    verts = [x for row in outer + inner for x in row]
    finish(verts, mat, bone, keep_uv=True)
    return verts


def dome(center, radii, mat, bone, rot=(0, 0, 0), cut=0.0, rings=4, segs=12, thick=0.012, band=False):
    """Купол-оболочка (наплечник, наколенник, макушка): полусфера над плоскостью z = cut (доля радиуса)."""
    secs = []
    lat0 = math.asin(max(-0.99, min(0.99, cut)))
    for j in range(rings + 1):
        lat = lat0 + (math.pi / 2 - lat0) * j / rings
        k = max(math.cos(lat), 0.04)
        secs.append(((0, 0, radii[2] * math.sin(lat)), radii[0] * k, radii[1] * k, radii[1] * k))
    v = loft(secs, mat, bone, thick=thick, segs=segs, band=band)
    place(v, loc=center, rot=rot)
    return v


def mirrored(build):
    """Строит деталь левой стороны и её зеркальную копию справа (кости .L -> .R)."""
    before = set(bm.verts)
    build()
    new = [v for v in bm.verts if v not in before]
    faces = list({f for v in new for f in v.link_faces})
    edges = list({e for f in faces for e in f.edges})
    geom = bmesh.ops.duplicate(bm, geom=new + edges + faces)["geom"]
    dup_v = [g for g in geom if isinstance(g, bmesh.types.BMVert)]
    dup_f = [g for g in geom if isinstance(g, bmesh.types.BMFace)]
    for v in dup_v:
        v.co.x = -v.co.x
        for gi, w in list(v[deform].items()):
            name = BONE_NAMES[gi]
            if name.endswith(".L"):
                del v[deform][gi]
                v[deform][BONE_NAMES.index(name[:-2] + ".R")] = w
    bmesh.ops.reverse_faces(bm, faces=dup_f)
    for f in dup_f:
        f.normal_update()


def along(bone, t0, t1, r0, r1, depth0=None, depth1=None, n=2):
    """Сечения вдоль кости от t0 к t1 (снизу вверх задаёт вызывающий): [(точка, ширина, вперёд, назад)]."""
    out = []
    for j in range(n):
        f = j / (n - 1)
        r = r0 + (r1 - r0) * f
        d = (depth0 if depth0 is not None else r0) + ((depth1 if depth1 is not None else r1) -
                                                      (depth0 if depth0 is not None else r0)) * f
        out.append((bone_pos(bone, t0 + (t1 - t0) * f), r, d, d))
    return out


def torso(rows, cy=0.0):
    return [((0, cy, z), w, f, b) for z, w, f, b in rows]


start_mesh()
# --- кираса: грудная пластина (кость Spine -- на ней рёбра скелета) и три поясных пластины
loft(torso([
    (1.17, 0.228, 0.262, 0.240),
    (1.26, 0.238, 0.272, 0.252),
    (1.35, 0.238, 0.262, 0.245),
    (1.44, 0.226, 0.235, 0.222),
    (1.50, 0.190, 0.195, 0.185),
    (1.53, 0.135, 0.150, 0.150),
]), IRON, "Spine", segs=16, power=2.4)
for z0, z1 in ((1.10, 1.19), (1.03, 1.12)):
    loft(torso([(z0, 0.236, 0.272, 0.25), (z1, 0.226, 0.262, 0.24)]), BAND, "Spine", segs=16, power=2.4, band=True)
# --- пояс с пряжкой, кожаные полы спереди и сзади (кость Hips)
loft(torso([(0.96, 0.245, 0.255, 0.235), (1.03, 0.238, 0.25, 0.23)], cy=0.0), STRAP, "Hips", segs=16, power=2.4,
     band=True)
box((0.06, 0.02, 0.06), IRON, "Hips", loc=(0, -0.262, 0.995))
loft(torso([(0.70, 0.27, 0.285, 0.0), (0.97, 0.24, 0.255, 0.0)]), LEATHER, "Hips", arc=(-36, 36), segs=4)
loft(torso([(0.72, 0.27, 0.0, 0.27), (0.97, 0.24, 0.0, 0.24)]), LEATHER, "Hips", arc=(144, 216), segs=4)


def left_side():
    # наплечник: купол на плече + две пластины по плечу (кость UpperArm -- поднимаются с рукой)
    dome((0.235, 0.03, 1.455), (0.135, 0.155, 0.10), IRON, "Shoulder.L", rot=(0, 28, 0), cut=-0.15, segs=14)
    loft(along("UpperArm.L", 0.36, 0.12, 0.098, 0.088), BAND, "UpperArm.L", arc=(-25, 205), segs=10, band=True)
    loft(along("UpperArm.L", 0.56, 0.32, 0.092, 0.085), BAND, "UpperArm.L", arc=(-25, 205), segs=10, band=True)
    # наручи
    loft(along("LowerArm.L", 0.97, 0.42, 0.074, 0.064), BAND, "LowerArm.L", segs=10, band=True)
    # набедренные пластины (тассеты) сбоку -- на бедре, идут за ногой
    loft(torso([(0.72, 0.285, 0.29, 0.29), (0.97, 0.25, 0.26, 0.24)]), BAND, "UpperLeg.L", arc=(40, 120), segs=4,
         band=True)
    # поножи: пластина на голени спереди, наколенник, кожаная обмотка над стопой, железный носок
    loft(along("LowerLeg.L", 0.92, 0.14, 0.09, 0.084, depth0=0.13, depth1=0.1), IRON, "LowerLeg.L", arc=(-95, 95), segs=8, power=2.2)
    dome(bone_pos("LowerLeg.L", 0.03) + Vector((0, -0.06, 0)), (0.075, 0.07, 0.06), IRON, "LowerLeg.L",
         rot=(90, 0, 0), segs=10, rings=3)
    loft(along("LowerLeg.L", 1.0, 0.84, 0.088, 0.082, depth0=0.135, depth1=0.12), LEATHER, "LowerLeg.L", segs=10)
    dome((BONE["Foot.L"][0].x, 0.035, 0.02), (0.07, 0.1, 0.075), IRON, "Foot.L", segs=10, rings=3)


mirrored(left_side)
armor = end_mesh("Armor")

# --- шлем отдельным мешем (можно спрятать в Unity): купол по форме черепа, нащёчники, рога
start_mesh()
HELM_Y = -0.01
loft([((0, HELM_Y, z), w, f, b) for z, w, f, b in (
    (1.655, 0.148, 0.195, 0.182),
    (1.72, 0.152, 0.208, 0.188),
    (1.79, 0.145, 0.170, 0.176),
    (1.84, 0.108, 0.128, 0.135),
    (1.875, 0.058, 0.072, 0.075),
    (1.89, 0.010, 0.012, 0.012),
)], IRON, "Head", segs=16, power=2.3)
loft([((0, HELM_Y, z), w, f, b) for z, w, f, b in ((1.645, 0.158, 0.205, 0.192), (1.69, 0.156, 0.212, 0.194))],
     BAND, "Head", segs=16, power=2.3, band=True)                                   # обод с заклёпками


def helmet_side():
    loft([((0.136, -0.05, 1.555), 0.008, 0.055, 0.045), ((0.15, -0.04, 1.66), 0.01, 0.075, 0.06)], IRON, "Head",
         segs=6)                                                                    # нащёчник
    # рог: кольца вдоль дуги -- наружу, вверх и вперёд, к концу тоньше
    path = [Vector((0.13, 0.0, 1.76)), Vector((0.21, -0.01, 1.80)), Vector((0.27, -0.04, 1.87)),
            Vector((0.29, -0.09, 1.94)), Vector((0.275, -0.14, 1.99))]
    radii = [0.042, 0.036, 0.027, 0.017, 0.004]
    loft([(p, r, r, r) for p, r in zip(path, radii)], HORN, "Head", segs=7, thick=0.004)


mirrored(helmet_side)
helmet = end_mesh("Helmet")


attach_to_rig(body)

# ---------------------------------------------------------------- оружие на кости WeaponSocket
GRIP_POS = Vector((-0.632, 0.085, 0.835))                  # кулак правой руки
BLADE_DIR = Vector((0, -1, 0.32)).normalized()              # клинок вперёд и чуть вверх
EDGE_DIR = (Vector((0, 0, 1)) - BLADE_DIR * BLADE_DIR.z).normalized()   # лезвия -- вверх и вниз

bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="EDIT")
eb = arm_data.edit_bones.new("WeaponSocket")
eb.head, eb.tail = GRIP_POS, GRIP_POS + BLADE_DIR * 0.12
eb.align_roll(EDGE_DIR)
eb.parent = arm_data.edit_bones["Hand.R"]
eb.use_deform = False
bpy.ops.object.mode_set(mode="OBJECT")


def load_weapon(name, length):
    """Оружие из набора: рукоять (материал Wood) в начале координат, клинок по +Y, лезвия по Z;
    длина -- length м. Материалы набора заменяются на PSX-текстуры, UV -- кубическая проекция."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(WEAPONS, name + ".fbx"))
    obj = next(o for o in bpy.data.objects if o not in before and o.type == "MESH")
    for o in set(bpy.data.objects) - before - {obj}:
        bpy.data.objects.remove(o)
    obj.parent = None
    me = obj.data
    me.transform(obj.matrix_world)
    obj.matrix_world = Matrix()
    wood = [i for i, s in enumerate(obj.material_slots) if s.material and "Wood" in s.material.name]
    grip = [me.vertices[v].co for p in me.polygons if p.material_index in wood for v in p.vertices]
    g0 = sum(grip, Vector()) / len(grip)
    tip = max((v.co for v in me.vertices), key=lambda c: (c - g0).length)
    axis = (tip - g0).normalized()
    side = Vector((1, 0, 0)) - axis * axis.x
    side.normalize()
    rot = Matrix((axis.cross(side), axis, side)).to_4x4()   # строки -- новые оси X, Y, Z
    scale = length / max((v.co - g0).dot(axis) for v in me.vertices)
    me.transform(Matrix.Scale(scale, 4) @ rot @ Matrix.Translation(-g0))
    remap = {"Wood": GRIP, "Dark Metal": IRON, "Metal": STEEL}
    new_idx = [next(i for k, i in remap.items() if k in (s.material.name if s.material else "Metal"))
               for s in obj.material_slots]
    old_idx = [p.material_index for p in me.polygons]
    me.materials.clear()                       # сбрасывает индексы граней -- ставим после
    for m in MATS:
        me.materials.append(m)
    for p, i in zip(me.polygons, old_idx):
        p.material_index = new_idx[i]
        p.use_smooth = False
    uv = me.uv_layers.active or me.uv_layers.new()
    for p in me.polygons:
        nx, ny, nz = (abs(c) for c in p.normal)
        for li in p.loop_indices:
            c = me.vertices[me.loops[li].vertex_index].co
            a, b = (c.y, c.z) if nx >= ny and nx >= nz else (c.x, c.z) if ny >= nz else (c.x, c.y)
            uv.data[li].uv = (a * 6, b * 6)
    obj.name = "Weapon_" + name
    me.name = obj.name
    return obj


weapon = load_weapon(WEAPON, 0.95)
weapon.parent = rig
weapon.parent_type = "BONE"
weapon.parent_bone = "WeaponSocket"
bpy.context.view_layer.update()
sock = rig.matrix_world @ rig.data.bones["WeaponSocket"].matrix_local
weapon.matrix_world = sock   # рукоять в кулаке, клинок по оси кости

# ---------------------------------------------------------------- итог
parts = [body, armor, helmet, weapon]
tris = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in parts}
print(f"[Knight] треугольников: {tris} = {sum(tris.values())}, костей: {len(rig.data.bones)}")

os.makedirs(os.path.dirname(BLEND), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=BLEND, compress=True)
if os.path.exists(BLEND + "1"):
    os.remove(BLEND + "1")   # резервная копия Blender не нужна: модель пересобирается скриптом

bpy.ops.object.select_all(action="DESELECT")
for o in parts + [rig]:
    o.select_set(True)
bpy.context.view_layer.objects.active = rig
fbx = os.path.join(OUT_DIR, "SkeletonKnight.fbx")
bpy.ops.export_scene.fbx(
    filepath=fbx, use_selection=True, object_types={"ARMATURE", "MESH"},
    apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
    add_leaf_bones=False, mesh_smooth_type="FACE", path_mode="STRIP", bake_anim=False)
print(f"[Knight] FBX: {fbx}")
print(f"[Knight] blend: {BLEND}")

# ---------------------------------------------------------------- превью
if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x, scene.render.resolution_y = 360, 480
    world = bpy.data.worlds.new("Night")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[0].default_value = (0.05, 0.03, 0.12, 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.6
    scene.world = world
    for name, energy, col, pos in (("Key", 900, (0.85, 0.8, 1.0), (1.5, -2.5, 2.6)),
                                   ("Rim", 600, (0.4, 0.5, 1.0), (-2, 2, 2.2)),
                                   ("Fill", 160, (1.0, 0.8, 0.6), (-2, -2, 0.6))):
        ld = bpy.data.lights.new(name, "POINT")
        ld.energy, ld.color = energy, col
        lo = bpy.data.objects.new(name, ld)
        lo.location = pos
        scene.collection.objects.link(lo)
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    cam.data.lens = 50
    scene.collection.objects.link(cam)
    scene.camera = cam

    def shot(path, angle_deg, dist=3.8, height=1.1, target_z=0.95):
        a = math.radians(angle_deg)
        cam.location = (math.sin(a) * dist, -math.cos(a) * dist, height)
        cam.rotation_euler = (Vector((0, 0, target_z)) - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)

    for ang in (0, 35, 90, 180):
        shot(os.path.join(PREVIEW, f"view_{ang}.png"), ang)
    shot(os.path.join(PREVIEW, "face.png"), 0, dist=0.9, height=1.68, target_z=1.66)
    shot(os.path.join(PREVIEW, "face_side.png"), 50, dist=0.9, height=1.68, target_z=1.66)
    helmet.hide_render = True
    shot(os.path.join(PREVIEW, "face_nohelm.png"), 0, dist=0.9, height=1.68, target_z=1.66)
    print(f"[Knight] превью: {PREVIEW}")
