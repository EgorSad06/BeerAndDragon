"""Скелет-воин в PSX-стиле: модель, текстуры, риг и анимации -- всё генерируется этим скриптом.

  blender -b --factory-startup -P tools/blender/skeleton_warrior.py -- [--preview build/enemies]

Результат:
  art/enemies/skeleton_warrior.blend                       -- исходник для ручной доводки в Blender
  Assets/Game/Assets/enemies/SkeletonWarrior/SkeletonWarrior.fbx  -- для Unity (риг Humanoid, 5 анимаций)
  Assets/Game/Assets/enemies/SkeletonWarrior/Textures/*.png       -- пиксельные текстуры 64x64
  --preview DIR: картинки модели с трёх сторон и кадры анимаций

Стиль PS1: мало полигонов, жёсткий скиннинг (каждая деталь целиком на одной кости), текстуры с point-фильтром.
Детали -- кости скелета и кожаная броня (наплечники, ремни, пояс с полами, наручи, поножи), ржавый меч,
синее пламя в глазницах. Координаты -- метры, Z вверх, лицо к -Y (как принято в Blender).
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


def save_texture(name, rgb):
    n = rgb.shape[0]
    img = bpy.data.images.new(name, n, n, alpha=False)
    px = np.ones((n, n, 4))
    px[:, :, :3] = np.clip(rgb, 0, 1)
    img.pixels = px[::-1].ravel()   # Blender хранит строки снизу вверх
    os.makedirs(TEX_DIR, exist_ok=True)
    img.filepath_raw = os.path.join(TEX_DIR, name + ".png")
    img.file_format = "PNG"
    img.save()
    return img


N = 64
def tex_bone():
    n = noise(N, 1.0)
    base = np.array([0.82, 0.76, 0.58]) * (0.75 + 0.35 * n)[..., None]
    grime = noise(N, 1.0) > 0.72
    base[grime] *= 0.55
    for _ in range(6):   # трещины
        x, y = rng.integers(0, N, 2)
        for _ in range(rng.integers(6, 14)):
            base[y % N, x % N] *= 0.35
            x += rng.integers(-1, 2); y += 1
    return base


def tex_leather():
    n = noise(N, 1.0)
    base = np.array([0.36, 0.22, 0.12]) * (0.65 + 0.5 * n)[..., None]
    base[noise(N, 1.0) > 0.75] *= 0.6        # потёртости
    base[:, ::16] *= 0.5                     # швы
    base[::4, 7::16] = [0.62, 0.52, 0.36]    # стежки
    return base


def tex_strap():
    n = noise(N, 1.0)
    base = np.array([0.20, 0.12, 0.07]) * (0.7 + 0.4 * n)[..., None]
    base[:, :3] *= 1.6; base[:, -3:] *= 1.6  # светлые кромки ремня
    for y in range(6, N, 16):                # заклёпки
        base[y:y + 3, 30:33] = [0.55, 0.5, 0.42]
    return base


def tex_metal():
    n = noise(N, 1.0)
    base = np.array([0.36, 0.36, 0.38]) * (0.7 + 0.45 * n)[..., None]
    rust = noise(N, 1.0) > 0.6
    base[rust] = base[rust] * 0.4 + np.array([0.42, 0.2, 0.08]) * 0.6
    base[::8, :] *= 1.15                     # царапины
    return base


def tex_steel():
    n = noise(N, 1.0)
    base = np.array([0.62, 0.64, 0.68]) * (0.75 + 0.35 * n)[..., None]
    base[noise(N, 1.0) > 0.78] *= 0.55       # пятна и зазубрины
    base[:, N // 2 - 1:N // 2 + 1] *= 1.25   # дол посередине клинка
    return base


def tex_cloth():
    n = noise(N, 1.0)
    base = np.array([0.10, 0.09, 0.12]) * (0.6 + 0.6 * n)[..., None]
    base[noise(N, 1.0) > 0.8] *= 0.4         # дыры и грязь
    return base


def material(name, img=None, emission=None, strength=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.85
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


MATS = [
    material("SW_Bone", save_texture("SW_Bone", tex_bone())),
    material("SW_Leather", save_texture("SW_Leather", tex_leather())),
    material("SW_Strap", save_texture("SW_Strap", tex_strap())),
    material("SW_Metal", save_texture("SW_Metal", tex_metal())),
    material("SW_Cloth", save_texture("SW_Cloth", tex_cloth())),
    material("SW_Steel", save_texture("SW_Steel", tex_steel())),
    material("SW_Socket", emission=(0.0, 0.0, 0.0), strength=0.0),
    material("SW_EyeFire", emission=(0.05, 0.3, 1.0), strength=4.0),
    material("SW_EyeFlame", emission=(0.15, 0.55, 1.0), strength=3.0),
]
BONE, LEATHER, STRAP, METAL, CLOTH, STEEL, SOCKET, EYE, FLAME = range(len(MATS))

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


def finish(verts, mat, bone, uv_scale=2.0):
    """Материал, жёсткая привязка к кости и кубическая проекция UV для только что созданных вершин."""
    gi = BONE_NAMES.index(bone)
    vs = set(verts)
    for v in vs:
        v[deform][gi] = 1.0
    faces = {f for v in vs for f in v.link_faces}
    for f in faces:
        f.material_index = mat
        f.normal_update()
        nx, ny, nz = (abs(c) for c in f.normal)
        for loop in f.loops:
            c = loop.vert.co
            u, w = (c.y, c.z) if nx >= ny and nx >= nz else (c.x, c.z) if ny >= nz else (c.x, c.y)
            loop[uv_layer].uv = (u * uv_scale, w * uv_scale)


def place(verts, loc=(0, 0, 0), rot=(0, 0, 0), scale=(1, 1, 1)):
    if isinstance(scale, (int, float)):
        scale = (scale, scale, scale)
    m =Matrix.LocRotScale(Vector(loc), Euler([math.radians(a) for a in rot]), Vector(scale))
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


# --- голова: череп (свод, надбровья, скулы, верхняя челюсть с зубами), глазницы с синим пламенем, челюсть
blob((0.1, 0.112, 0.105), BONE, "Head", segs=10, rings=7, loc=(0, 0.005, 1.735))           # свод черепа
box((0.15, 0.035, 0.03), BONE, "Head", loc=(0, -0.095, 1.745), rot=(-10, 0, 0))             # надбровья
for s in (L, R):
    box((0.035, 0.05, 0.03), BONE, "Head", loc=(s * 0.062, -0.075, 1.69), rot=(0, 0, s * 15))  # скулы
box((0.1, 0.05, 0.04), BONE, "Head", loc=(0, -0.085, 1.66))                                 # верхняя челюсть
for k in range(6):
    box((0.012, 0.012, 0.02), BONE, "Head", loc=(-0.035 + k * 0.014, -0.11, 1.637))         # верхние зубы
for s in (L, R):
    blob((0.034, 0.02, 0.03), SOCKET, "Head", segs=6, rings=4, loc=(s * 0.042, -0.097, 1.71))
    blob(0.012, EYE, "Head", segs=6, rings=4, loc=(s * 0.042, -0.108, 1.71))
    rod((s * 0.042, -0.108, 1.718), (s * 0.04, -0.1, 1.76), 0.009, FLAME, "Head", r2=0.0, segs=4)
rod((0, -0.118, 1.69), (0, -0.112, 1.665), 0.012, SOCKET, "Head", r2=0.004, segs=3)         # носовая дыра
box((0.1, 0.08, 0.03), BONE, "Jaw", loc=(0, -0.07, 1.6))
for k in range(6):
    box((0.012, 0.012, 0.018), BONE, "Jaw", loc=(-0.035 + k * 0.014, -0.105, 1.62))         # нижние зубы

# --- позвоночник, рёбра, грудина, таз
rod((0, 0, 1.0), (0, 0, 1.5), 0.022, BONE, "Spine", segs=5)
rod((0, 0, 1.48), (0, 0, 1.6), 0.02, BONE, "Neck", segs=5)
for i, (z, rad) in enumerate(((1.42, 0.13), (1.37, 0.14), (1.32, 0.14), (1.27, 0.13), (1.22, 0.11))):
    ring((0, -0.02, z), rad, 0.014, BONE, "Chest" if i < 3 else "Spine", segs=10, scale=(1, 0.75, 1), arc=320)
box((0.03, 0.02, 0.2), BONE, "Chest", loc=(0, -0.12, 1.33))                 # грудина
box((0.3, 0.05, 0.03), BONE, "Chest", loc=(0, 0.01, 1.455))                 # ключицы
blob((0.14, 0.09, 0.07), BONE, "Hips", segs=8, rings=4, loc=(0, 0, 0.99))   # таз

# --- руки: плечевые и локтевые кости, кисти с когтями
for s, side in ((L, "L"), (R, "R")):
    rod((s * 0.18, 0, 1.45), (s * 0.46, 0, 1.45), 0.024, BONE, f"UpperArm.{side}")
    rod((s * 0.46, 0, 1.45), (s * 0.72, 0, 1.45), 0.02, BONE, f"LowerArm.{side}")
    box((0.08, 0.05, 0.025), BONE, f"Hand.{side}", loc=(s * 0.765, 0, 1.45))
    for k, dy in enumerate((-0.018, 0.0, 0.018)):
        rod((s * 0.8, dy, 1.45), (s * 0.86, dy - 0.004, 1.43), 0.008, BONE, f"Hand.{side}", r2=0.002, segs=4)
    rod((s * 0.79, -0.03, 1.45), (s * 0.82, -0.06, 1.44), 0.008, BONE, f"Hand.{side}", r2=0.002, segs=4)

    # ноги: бедро и голень -- кость, стопа в сапоге
    rod((s * 0.10, 0, 0.97), (s * 0.10, 0, 0.55), 0.03, BONE, f"UpperLeg.{side}")
    rod((s * 0.10, 0, 0.54), (s * 0.10, 0, 0.12), 0.024, BONE, f"LowerLeg.{side}")
    blob(0.04, BONE, f"LowerLeg.{side}", segs=6, rings=4, loc=(s * 0.10, -0.01, 0.54))   # колено

    # --- кожаная броня
    blob((0.11, 0.12, 0.08), LEATHER, f"UpperArm.{side}", segs=8, rings=4,
         loc=(s * 0.21, 0, 1.475), rot=(0, s * 20, 0))                                    # наплечник
    blob((0.08, 0.09, 0.05), STRAP, f"UpperArm.{side}", segs=8, rings=4,
         loc=(s * 0.23, 0, 1.51), rot=(0, s * 25, 0))                                     # второй слой
    rod((s * 0.56, 0, 1.45), (s * 0.71, 0, 1.45), 0.042, LEATHER, f"LowerArm.{side}", r2=0.036, segs=6)  # наруч
    ring((s * 0.6, 0, 1.45), 0.044, 0.006, STRAP, f"LowerArm.{side}", segs=6, rot=(0, 90, 0))
    rod((s * 0.10, 0, 0.42), (s * 0.10, 0, 0.14), 0.048, LEATHER, f"LowerLeg.{side}", r2=0.042, segs=6)  # поножи
    ring((s * 0.10, 0, 0.36), 0.05, 0.006, STRAP, f"LowerLeg.{side}", segs=6)
    box((0.1, 0.22, 0.08), LEATHER, f"Foot.{side}", loc=(s * 0.10, -0.05, 0.04))         # сапог
    box((0.1, 0.06, 0.06), STRAP, f"Foot.{side}", loc=(s * 0.10, -0.15, 0.03))            # носок
    # полы кожаной юбки, по две на ногу
    for fy, rx in ((-0.09, -8), (0.08, 8)):
        box((0.12, 0.02, 0.24), LEATHER, f"UpperLeg.{side}", loc=(s * 0.11, fy, 0.84), rot=(rx, 0, 0))

# --- корпус брони: ремни крест-накрест через рёбра, ворот, пояс с пряжкой
rod((0.16, -0.11, 1.46), (-0.12, -0.13, 1.14), 0.022, STRAP, "Chest", segs=4)
rod((-0.16, -0.11, 1.46), (0.10, -0.13, 1.2), 0.018, STRAP, "Chest", segs=4)
rod((0.16, 0.09, 1.46), (-0.12, 0.1, 1.14), 0.022, STRAP, "Chest", segs=4)
rod((0, 0, 1.43), (0, 0, 1.53), 0.14, LEATHER, "Chest", r2=0.07, segs=8)                 # ворот-капюшон
ring((0, 0, 1.08), 0.15, 0.025, LEATHER, "Hips", segs=10, scale=(1, 0.8, 1.4))          # пояс
box((0.06, 0.02, 0.05), METAL, "Hips", loc=(0, -0.125, 1.08))                            # пряжка
box((0.3, 0.02, 0.16), CLOTH, "Hips", loc=(0, 0.11, 0.98), rot=(10, 0, 0))               # рваная ткань сзади

# --- меч в правой руке (вдоль -Y от кисти)
rod((-0.79, 0.04, 1.45), (-0.79, -0.06, 1.45), 0.014, STRAP, "Sword", segs=5)            # рукоять
box((0.03, 0.025, 0.025), METAL, "Sword", loc=(-0.79, 0.055, 1.45))                      # навершие
box((0.18, 0.025, 0.025), METAL, "Sword", loc=(-0.79, -0.07, 1.45))                      # гарда
v = box((0.06, 0.7, 0.012), STEEL, "Sword", loc=(-0.79, -0.43, 1.45))                   # клинок
tip = [x for x in v if x.co.y < -0.7]
for x in tip:
    x.co.x = -0.79

bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0001)
mesh = bpy.data.meshes.new("SkeletonWarrior")
bm.to_mesh(mesh)
bm.free()
for m in MATS:
    mesh.materials.append(m)
for p in mesh.polygons:
    p.use_smooth = False
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
    shot(os.path.join(PREVIEW, "face.png"), 15, 1, dist=0.9, height=1.72, target_z=1.68)
    for act, frames in ((ACTIONS[1], (1, 7, 13, 19)), (ACTIONS[2], (1, 10, 16, 24)),
                        (ACTIONS[3], (1, 4)), (ACTIONS[4], (1, 10, 20, 36))):
        rig.animation_data.action = act
        for f in frames:
            shot(os.path.join(PREVIEW, f"{act.name}_{f:02d}.png"), 40, f)
    print(f"[Skeleton] превью: {PREVIEW}")
