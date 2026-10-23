"""Скелет в PSX-стиле из скачанной реалистичной модели (art/enemies/source/SkeletonRealistic).

  blender -b --factory-startup -P tools/blender/skeleton_psx.py -- [--preview build/enemies/skeleton]

Исходник (не меняется): Skeleton.blend (меш ~13 000 треугольников, риг с индонезийскими именами костей,
тело привязано «оболочками» костей -- Unity их не понимает) и Skeleton_C.png 2048x2048.
Результат:
  art/enemies/skeleton.blend
  Assets/Game/Assets/enemies/Skeleton/Skeleton.fbx       -- для Unity (риг Humanoid, кость WeaponSocket в руках)
  Assets/Game/Assets/enemies/Skeleton/Textures/*.png

Что делает скрипт (как выглядели бы модели PS1):
  - кости рига -- в имена для автонастройки Humanoid (com -> Hips, latas.l -> UpperArm.L, tel1.l -> Index1.L ...),
    контроллеры ног foot_con убираются, стопы садятся на голени;
  - жёсткий скиннинг: каждый кусок меша (отдельная кость скелета) целиком на одной кости рига;
  - сетка упрощается до ~TRIS треугольников (череп -- бережнее);
  - текстура: 2048 -> TEX_SIZE, 15-битный цвет с дизерингом, лёгкая грязь; карты нормалей и бликов не нужны;
  - в глазницах -- синие светящиеся глаза (материал SKL_EyeFire, его подсвечивает EnemyModelImporter).
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Euler, Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import intersect_point_line

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "art", "enemies", "source", "SkeletonRealistic")
OUT_DIR = os.path.join(ROOT, "Assets", "Game", "Assets", "enemies", "Skeleton")
TEX_DIR = os.path.join(OUT_DIR, "Textures")
BLEND = os.path.join(ROOT, "art", "enemies", "skeleton.blend")
TRIS = 3500
TEX_SIZE = 256

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PREVIEW = os.path.abspath(argv[argv.index("--preview") + 1]) if "--preview" in argv else None

RENAME = {
    "com": "Hips", "perut": "Spine", "dada": "Chest", "leher": "Neck", "kepala": "Head",
    "pundak": "Shoulder", "latas": "UpperArm", "lbawah": "LowerArm", "tangan": "Hand",
    "jemp": "Thumb", "tel": "Index", "jteng": "Middle", "jman": "Ring", "kel": "Little",
    "weapon": "WeaponSocket", "paha": "UpperLeg", "betis": "LowerLeg", "kaki": "Foot", "jkaki": "Toes",
}
DROP = ("foot_con",)   # IK-контроллеры ног: в игре не нужны


def new_name(old):
    base, _, side = old.partition(".")
    digits = base.lstrip("abcdefghijklmnopqrstuvwxyz")
    word = base[:len(base) - len(digits)]
    return RENAME[word] + digits + (f".{side.upper()}" if side else "")


# ---------------------------------------------------------------- исходник
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
with bpy.data.libraries.load(os.path.join(SRC, "Skeleton.blend")) as (src, dst):
    dst.objects = ["Skeleton", "Skeleton.001"]
src_body, src_rig = dst.objects
for o in (src_body, src_rig):
    scene.collection.objects.link(o)
src_rig.animation_data_clear()
src_rig.data.pose_position = "REST"
bpy.context.view_layer.update()

# риг в мировых координатах (у исходника масштаб 0.191 и поворот 90°)
arm_data = bpy.data.armatures.new("Rig")
rig = bpy.data.objects.new("Rig", arm_data)
scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="EDIT")
keep = [b for b in src_rig.data.bones if not b.name.startswith(DROP)]
for b in keep:
    eb = arm_data.edit_bones.new(new_name(b.name))
    eb.head = src_rig.matrix_world @ b.head_local
    eb.tail = src_rig.matrix_world @ b.tail_local
    eb.align_roll((src_rig.matrix_world.to_3x3() @ b.matrix_local.to_3x3().col[2]).normalized())
    eb.use_deform = not b.name.startswith("weapon")
for b in keep:
    eb = arm_data.edit_bones[new_name(b.name)]
    parent = b.parent
    if parent is not None and parent.name.startswith(DROP):
        side = b.name.split(".")[1]
        parent = src_rig.data.bones[f"betis.{side}"]   # стопа -- на голени
    if parent is not None:
        eb.parent = arm_data.edit_bones[new_name(parent.name)]
bpy.ops.object.mode_set(mode="OBJECT")
DEFORM = [b.name for b in arm_data.bones if b.use_deform]

# тело в мировых координатах, без старых групп
me = src_body.data.copy()
me.name = "Skeleton"
me.transform(src_body.matrix_world)
src_groups = {g.index: g.name for g in src_body.vertex_groups}
head_kind = {v.index: src_groups[max(v.groups, key=lambda g: g.weight).group] for v in src_body.data.vertices
             if v.groups}
bpy.data.objects.remove(src_body)
bpy.data.objects.remove(src_rig)
body = bpy.data.objects.new("Body", me)
scene.collection.objects.link(body)
body.vertex_groups.clear()
for name in DEFORM:
    body.vertex_groups.new(name=name)

# ---------------------------------------------------------------- жёсткий скиннинг по кускам меша
bm = bmesh.new()
bm.from_mesh(me)
bm.verts.ensure_lookup_table()
seen = set()
islands = []
for v in bm.verts:
    if v.index in seen:
        continue
    stack, isl = [v], []
    seen.add(v.index)
    while stack:
        cur = stack.pop()
        isl.append(cur.index)
        for e in cur.link_edges:
            o = e.other_vert(cur)
            if o.index not in seen:
                seen.add(o.index)
                stack.append(o)
    islands.append(isl)
centers = [sum((bm.verts[i].co for i in isl), Vector()) / len(isl) for isl in islands]
bm.free()


def nearest_bone(p):
    best, best_d = None, 1e9
    for name in DEFORM:
        b = arm_data.bones[name]
        q, t = intersect_point_line(p, b.head_local, b.tail_local)
        q = b.head_local if t < 0 else b.tail_local if t > 1 else q
        d = (p - q).length
        if d < best_d:
            best, best_d = name, d
    return best


assign = {}
for isl, c in zip(islands, centers):
    kinds = [head_kind[i] for i in isl if i in head_kind]
    if len(kinds) > len(isl) / 2:          # череп, челюсть, шейные позвонки -- как у автора модели
        bone = new_name(max(set(kinds), key=kinds.count))
    else:
        bone = nearest_bone(c)
    for i in isl:
        assign[i] = bone
for name in DEFORM:
    idx = [i for i, b in assign.items() if b == name]
    if idx:
        body.vertex_groups[name].add(idx, 1.0, "REPLACE")
print(f"[Skeleton] кусков меша: {len(islands)}")

# ---------------------------------------------------------------- упрощение сетки
tris0 = sum(len(p.vertices) - 2 for p in me.polygons)
protect = body.vertex_groups.new(name="_protect")
for name in DEFORM:   # вес группы -- «насколько можно упрощать»: череп меньше остального
    protect.add([i for i, b in assign.items() if b == name], 0.3 if name == "Head" else 1.0, "REPLACE")
dec = body.modifiers.new("Decimate", "DECIMATE")
dec.ratio = TRIS / tris0
dec.use_collapse_triangulate = True
dec.vertex_group = protect.name
dec.vertex_group_factor = 1.0
bpy.context.view_layer.objects.active = body
bpy.ops.object.modifier_apply(modifier=dec.name)
body.vertex_groups.remove(body.vertex_groups["_protect"])   # ссылка устарела после применения модификатора
for v in me.vertices:                              # после упрощения веса снова «один кусок -- одна кость»
    if len(v.groups) > 1:
        top = max(v.groups, key=lambda g: g.weight).group
        for g in list(v.groups):
            if g.group != top:
                body.vertex_groups[g.group].remove([v.index])
    for g in v.groups:
        g.weight = 1.0
for p in me.polygons:
    p.use_smooth = True
head_gi = body.vertex_groups["Head"].index
head_tris = sum(1 for p in me.polygons if all(me.vertices[i].groups and me.vertices[i].groups[0].group == head_gi
                                              for i in p.vertices))
print(f"[Skeleton] треугольников: {tris0} -> {sum(len(p.vertices) - 2 for p in me.polygons)} (череп {head_tris})")

# ---------------------------------------------------------------- текстура PS1
rng = np.random.default_rng(5)
BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) / 16.0 - 0.5
GRIME = np.array([0.40, 0.36, 0.26])    # буро-зелёная грязь


def noise(n, scale):
    """Гладкий шум: сумма октав случайных решёток с билинейной интерполяцией."""
    out = np.zeros((n, n))
    for octave, amp in ((4, 0.5), (8, 0.3), (16, 0.2)):
        g = rng.random((octave + 1, octave + 1))
        xs = np.linspace(0, octave, n, endpoint=False)
        i, f = xs.astype(int), xs - xs.astype(int)
        y0 = g[i][:, i] * (1 - f)[None, :] + g[i][:, i + 1] * f[None, :]
        y1 = g[i + 1][:, i] * (1 - f)[None, :] + g[i + 1][:, i + 1] * f[None, :]
        out += amp * (y0 * (1 - f)[:, None] + y1 * f[:, None])
    return (out - out.min()) / (np.ptp(out) + 1e-6) * scale


def ps1(rgb, grime=0.25):
    """Огрубление «под PS1»: пятна грязи, зерно, 15-битный цвет с упорядоченным дизерингом."""
    n = rgb.shape[0]
    dirt = noise(n, 1.0)[..., None] * grime
    rgb = rgb * (1 - dirt) + GRIME * rgb.mean(-1, keepdims=True) * dirt
    rgb = rgb * (0.9 + 0.2 * rng.random((n, n, 1)))            # зерно
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


def bone_texture():
    """Исходный атлас 2048 -> TEX_SIZE усреднением блоков, чуть теплее и контрастнее, затем ps1()."""
    src = bpy.data.images.load(os.path.join(SRC, "Skeleton_C.png"))
    w, h = src.size
    px = np.array(src.pixels[:]).reshape(h, w, 4)[::-1, :, :3]
    k = w // TEX_SIZE
    small = px.reshape(TEX_SIZE, k, TEX_SIZE, k, 3).mean(axis=(1, 3))
    small = np.clip((small - 0.5) * 1.15 + 0.5, 0, 1) * np.array([1.0, 0.95, 0.82])   # пожелтевшая кость
    return ps1(small)


def material(name, img=None, emission=None, strength=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.9
    if img is not None:
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image, t.interpolation = img, "Closest"          # PSX: пиксели без сглаживания
        nt.links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
    if emission is not None:
        bsdf.inputs["Base Color"].default_value = (*emission, 1)
        bsdf.inputs["Emission Color"].default_value = (*emission, 1)
        bsdf.inputs["Emission Strength"].default_value = strength
    return m


me.materials.clear()
me.materials.append(material("SKL_Bone", save_texture("SKL_Bone", bone_texture())))
me.materials.append(material("SKL_EyeFire", emission=(0.1, 0.5, 1.0), strength=10.0))
for p in me.polygons:
    p.material_index = 0

# ---------------------------------------------------------------- глаза в глазницах
def find_sockets():
    """Глазницы: лучи спереди в череп; там, где луч уходит глубоко, -- глазница. Центр и глубина дна."""
    me.calc_loop_triangles()
    head_i = body.vertex_groups["Head"].index
    head_v = {v.index for v in me.vertices if v.groups and v.groups[0].group == head_i}
    tris = [t for t in me.loop_triangles if all(i in head_v for i in t.vertices)]
    bvh = BVHTree.FromPolygons([v.co for v in me.vertices], [t.vertices for t in tris])
    hb = arm_data.bones["Head"]
    zc = hb.head_local.z
    grid = {}
    xs = np.arange(-0.08, 0.0801, 0.002)
    for z in np.arange(zc, zc + 0.16, 0.002):
        for x in xs:
            loc, *_ = bvh.ray_cast(Vector((x, -1, z)), Vector((0, 1, 0)))
            grid[(round(x, 3), round(z, 3))] = loc.y if loc is not None else None
    front = min(y for y in grid.values() if y is not None)
    out = []
    for s in (1, -1):
        # глазница: луч уходит глубоко или насквозь, а вокруг -- кость: снаружи (висок), сверху (надбровье)
        # и снизу (скула), не дальше 4 см
        def solid(x, z):
            y = grid.get((round(x, 3), round(z, 3)))
            return y is not None and y < front + 0.03

        near = np.arange(0.004, 0.04, 0.002)
        cells = []
        for (x, z), y in grid.items():
            if not 0.012 < s * x < 0.065 or (y is not None and y < front + 0.03):
                continue
            if (any(solid(x + s * d, z) for d in near) and any(solid(x, z + d) for d in near)
                    and any(solid(x, z - d) for d in near)):
                cells.append((x, z, y))
        cx = sum(a[0] for a in cells) / len(cells)
        cz = sum(a[1] for a in cells) / len(cells)
        walls = [a[2] for a in cells if a[2] is not None and math.hypot(a[0] - cx, a[1] - cz) < 0.015]
        floor = float(np.median(walls)) if walls else front + 0.045
        out.append((Vector((cx, front, cz)), floor))
        print(f"[Skeleton] глазница {'L' if s > 0 else 'R'}: x={cx:+.3f} z={cz:.3f} лицо y={front:.3f} дно y={floor:.3f} "
              f"({len(cells)} лучей)")
    return out


bm = bmesh.new()
bm.from_mesh(me)
deform = bm.verts.layers.deform.verify()
head_gi = body.vertex_groups["Head"].index
for c, floor in find_sockets():
    s = 1 if c.x > 0 else -1
    # миндалина: утоплена между лицом и дном глазницы, внешний край чуть выше -- «злой» прищур
    v = bmesh.ops.create_uvsphere(bm, u_segments=8, v_segments=4, radius=1.0)["verts"]
    bmesh.ops.transform(bm, verts=v, matrix=Matrix.LocRotScale(
        Vector((c.x, c.y + (floor - c.y) * 0.55, c.z)), Euler((0, math.radians(s * -14), 0)),
        Vector((0.011, 0.004, 0.0055))))
    for vert in v:
        vert[deform][head_gi] = 1.0
        for f in vert.link_faces:
            f.material_index = 1
            f.smooth = True
bm.to_mesh(me)
bm.free()

body.parent = rig
mod = body.modifiers.new("Armature", "ARMATURE")
mod.object = rig

# ---------------------------------------------------------------- сохранение и экспорт
print(f"[Skeleton] треугольников итого: {sum(len(p.vertices) - 2 for p in me.polygons)}, костей: {len(arm_data.bones)}")
os.makedirs(os.path.dirname(BLEND), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=BLEND, compress=True)
if os.path.exists(BLEND + "1"):
    os.remove(BLEND + "1")   # резервная копия Blender не нужна: модель пересобирается скриптом

bpy.ops.object.select_all(action="DESELECT")
body.select_set(True)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
fbx = os.path.join(OUT_DIR, "Skeleton.fbx")
bpy.ops.export_scene.fbx(
    filepath=fbx, use_selection=True, object_types={"ARMATURE", "MESH"},
    apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
    add_leaf_bones=False, mesh_smooth_type="FACE", path_mode="STRIP", bake_anim=False)
print(f"[Skeleton] FBX: {fbx}")

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

    def shot(path, angle_deg, dist=4.4, height=1.0, target_z=0.9):
        a = math.radians(angle_deg)
        cam.location = (math.sin(a) * dist, -math.cos(a) * dist, height)
        cam.rotation_euler = (Vector((0, 0, target_z)) - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)

    for ang in (0, 35, 90):
        shot(os.path.join(PREVIEW, f"view_{ang}.png"), ang)
    shot(os.path.join(PREVIEW, "face.png"), 0, dist=0.8, height=1.72, target_z=1.7)
    shot(os.path.join(PREVIEW, "face_side.png"), 40, dist=0.8, height=1.72, target_z=1.7)
    shot(os.path.join(PREVIEW, "torso.png"), 20, dist=1.4, height=1.3, target_z=1.2)
    print(f"[Skeleton] превью: {PREVIEW}")
