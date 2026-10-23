"""Скелет в PSX-стиле из скачанной реалистичной модели (art/enemies/source/SkeletonRealistic).

  blender -b --factory-startup -P tools/blender/skeleton_psx.py -- [--preview build/enemies/skeleton]

Исходник (не меняется): Skeleton.blend (меш ~13 000 треугольников, риг с индонезийскими именами костей,
тело привязано «оболочками» костей -- Unity их не понимает) и Skeleton_C.png 2048x2048.
Результат:
  art/enemies/skeleton.blend
  Assets/Game/Assets/enemies/Skeleton/Skeleton.fbx       -- для Unity (риг Humanoid, анимации, оружие)
  Assets/Game/Assets/enemies/Skeleton/Textures/*.png

Что делает скрипт (как выглядели бы модели PS1):
  - кости рига -- в имена для автонастройки Humanoid (com -> Hips, latas.l -> UpperArm.L, tel1.l -> Index1.L ...),
    контроллеры ног foot_con убираются, стопы садятся на голени;
  - жёсткий скиннинг: каждый кусок меша (отдельная кость скелета) целиком на одной кости рига;
  - сетка упрощается до ~TRIS треугольников (череп -- бережнее);
  - текстура: 2048 -> TEX_SIZE, 15-битный цвет с дизерингом, лёгкая грязь; карты нормалей и бликов не нужны;
  - оружие: одноручное из art/weapons/LowPolyWeaponPack (список WEAPONS) -- отдельные объекты на кости
    WeaponSocket.R, хват в кулаке; в Unity включается одно (компонент SkeletonCreep);
  - анимации Idle, Walk (циклы), Attack, Death -- ключевые позы, заданные поворотами костей.
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector
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


def tex_flat(color, spread=0.3, stripes=0):
    """Простая PS1-текстура материала оружия: цвет + шум, по желанию поперечные полосы (обмотка, волокна)."""
    n = 32
    base = np.array(color) * (1 - spread / 2 + spread * noise(n, 1.0))[..., None]
    if stripes:
        base[::stripes] *= 0.6
    return ps1(base, 0.2)


me.materials.clear()
me.materials.append(material("SKL_Bone", save_texture("SKL_Bone", bone_texture())))
# материалы оружия из пака (по именам его материалов)
WEAPON_MATS = {
    "Dark Metal": material("SKL_Iron", save_texture("SKL_Iron", tex_flat((0.24, 0.23, 0.23), 0.5))),
    "Metal": material("SKL_Steel", save_texture("SKL_Steel", tex_flat((0.58, 0.58, 0.6), 0.35, stripes=5))),
    "Wood": material("SKL_Wood", save_texture("SKL_Wood", tex_flat((0.36, 0.22, 0.12), 0.5, stripes=7))),
    "Chord": material("SKL_Cord", save_texture("SKL_Cord", tex_flat((0.45, 0.38, 0.26), 0.4, stripes=3))),
    "Bone": material("SKL_Ivory", save_texture("SKL_Ivory", tex_flat((0.72, 0.66, 0.5), 0.3))),
    "OBSIDIAN": material("SKL_Obsidian", save_texture("SKL_Obsidian", tex_flat((0.08, 0.07, 0.1), 0.6))),
}
BUSINESS = ("Metal", "Bone", "OBSIDIAN")   # «рабочий» конец оружия -- клинок, головка; дерево -- рукоять

body.parent = rig
mod = body.modifiers.new("Armature", "ARMATURE")
mod.object = rig

# ---------------------------------------------------------------- оружие из пака на кости WeaponSocket.R
WEAPON_PACK = os.path.join(ROOT, "art", "weapons", "LowPolyWeaponPack")
WEAPONS = ["Arming_Sword", "Bearded_Axe", "Bone_Shiv", "Cutlass", "Dagger", "Falchion", "Hatchet", "Kopesh",
           "Kukri", "Leaf_Blade", "Macuahuitl", "Offset_Sword", "Orc_Sword", "Scimitar", "Spiked_Club", "War_Hammer"]
PACK_SCALE = 0.78     # масштаб пака под рост скелета (меч -- ~0.95 м); соотношение размеров оружия сохраняется
GRIP_FROM_BUTT = 0.16  # хват -- не дальше стольких единиц пака от торца рукояти (у топоров -- низ рукояти)


def mat_kind(name):
    return next((k for k in WEAPON_MATS if k in name), "Metal")


def load_weapon(name):
    """Оружие пака -> меш: хват в начале координат, рабочий конец по +Y, ширина клинка по X."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(WEAPON_PACK, name + ".fbx"))
    new = [o for o in bpy.data.objects if o not in before]
    obj = next(o for o in new if o.type == "MESH")
    for o in new:
        if o is not obj:
            bpy.data.objects.remove(o)
    obj.parent = None
    wm = obj.data
    wm.transform(obj.matrix_world)
    obj.matrix_world = Matrix()
    kinds = [mat_kind(obj.material_slots[p.material_index].material.name) for p in wm.polygons]
    wood = [wm.vertices[i].co for p, k in zip(wm.polygons, kinds) if k == "Wood" for i in p.vertices]
    biz = [wm.vertices[i].co for p, k in zip(wm.polygons, kinds) if k in BUSINESS or k == "Dark Metal"
           for i in p.vertices]
    wood_c = sum(wood, Vector()) / len(wood)
    biz_c = sum(biz, Vector()) / len(biz)
    d = 1 if biz_c.y > wood_c.y else -1                      # в паке всё оружие вытянуто по Y
    butt = min(v.co.y * d for v in wm.vertices)
    grip = Vector((wood_c.x, d * (butt + min(wood_c.y * d - butt, GRIP_FROM_BUTT)), wood_c.z))
    turn = Matrix.Rotation(math.pi, 4, "Z") if d < 0 else Matrix()
    wm.transform(Matrix.Scale(PACK_SCALE, 4) @ turn @ Matrix.Translation(-grip))
    wm.materials.clear()                                      # сбрасывает индексы граней -- ставим после
    order = list(WEAPON_MATS)
    for k in order:
        wm.materials.append(WEAPON_MATS[k])
    uv = wm.uv_layers.active or wm.uv_layers.new()
    for p, k in zip(wm.polygons, kinds):
        p.material_index = order.index(k)
        p.use_smooth = False
        nx, ny, nz = (abs(c) for c in p.normal)
        for li in p.loop_indices:
            c = wm.vertices[wm.loops[li].vertex_index].co
            a, b = (c.y, c.z) if nx >= ny and nx >= nz else (c.x, c.z) if ny >= nz else (c.x, c.y)
            uv.data[li].uv = (a * 4, b * 4)
    obj.name = wm.name = "Weapon_" + name
    return obj


sock = arm_data.bones["WeaponSocket.R"]
grip_dir = (sock.tail_local - sock.head_local).normalized()   # вперёд из кулака
edge_dir = Vector((0, 0, 1))                                  # ширина клинка -- вертикально
GRIP_WORLD = Matrix((edge_dir, grip_dir, edge_dir.cross(grip_dir))).transposed().to_4x4()   # столбцы -- оси X, Y, Z
GRIP_WORLD.translation = sock.head_local
weapons = []
for name in WEAPONS:
    w = load_weapon(name)
    w.parent = rig
    w.parent_type = "BONE"
    w.parent_bone = "WeaponSocket.R"
    bpy.context.view_layer.update()
    w.matrix_world = GRIP_WORLD
    weapons.append(w)
print(f"[Skeleton] оружие: {', '.join(f'{w.name[7:]} ({len(w.data.polygons)})' for w in weapons)}")

# ---------------------------------------------------------------- анимации
# Повороты задаются осями мира в T-позе (как если бы кость поворачивали в позе покоя), по порядку:
# [("Y", 78), ("X", -20)] -- сначала опустить руку, потом качнуть вперёд. Ребёнок наследует поворот родителя.
FPS = 24
scene.render.fps = FPS
AX = {"X": Vector((1, 0, 0)), "Y": Vector((0, 1, 0)), "Z": Vector((0, 0, 1))}
REST = {b.name: b.matrix_local.to_quaternion() for b in arm_data.bones}
FINGERS = [f"{f}{i}" for f in ("Index", "Middle", "Ring", "Little") for i in (1, 2, 3)]


def q(bone, rots):
    total = Quaternion()
    for axis, deg in rots:
        total = Quaternion(AX[axis], math.radians(deg)) @ total
    return REST[bone].inverted() @ total @ REST[bone]


def base_pose():
    """Стойка крипа: сутулится, голова вперёд, руки опущены, в правой оружие клинком вперёд, кулаки сжаты."""
    p = {
        "Spine": [("X", 6)], "Chest": [("X", 6)], "Neck": [("X", 8)], "Head": [("X", -6)],
        "UpperLeg.L": [("X", -6)], "LowerLeg.L": [("X", 12)], "Foot.L": [("X", -6)],
        "UpperLeg.R": [("X", -6)], "LowerLeg.R": [("X", 12)], "Foot.R": [("X", -6)],
        "UpperArm.L": [("Y", 74), ("X", -6)], "LowerArm.L": [("Z", -20)],
        "UpperArm.R": [("Y", -72), ("X", -14)], "LowerArm.R": [("Z", 40)], "Hand.R": [("Z", -20)],
    }
    for s, side in ((1, "L"), (-1, "R")):
        for f in FINGERS:
            p[f"{f}.{side}"] = [("Y", s * 55)]
        p[f"Thumb2.{side}"] = [("X", -30)]
    return p


def key_pose(frame, extra, hips=(0, 0, 0), replace=()):
    pose = base_pose()
    for b in replace:
        pose[b] = []
    for b, rots in extra.items():
        pose[b] = pose.get(b, []) + rots
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
        quat = q(pb.name, pose.get(pb.name, []))
        prev = LAST.get(pb.name)
        if prev is not None and prev.dot(quat) < 0:      # без «переворотов» между ключами
            quat.negate()
        LAST[pb.name] = quat
        pb.rotation_quaternion = quat
        pb.keyframe_insert("rotation_quaternion", frame=frame)
        if pb.name == "Hips":
            pb.location = REST["Hips"].inverted() @ Vector(hips)
            pb.keyframe_insert("location", frame=frame)


LAST = {}


def action(name, keys, loop):
    """keys: [(кадр, повороты, смещение таза, кости без базовой позы)]; у цикла последний ключ = первый."""
    rig.animation_data_create()
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    rig.animation_data.action = act
    LAST.clear()
    for frame, extra, hips, replace in keys:
        key_pose(frame, extra, hips, replace)
    end = keys[-1][0]
    if loop:
        end += keys[1][0] - keys[0][0]
        _, extra, hips, replace = keys[0]
        key_pose(end, extra, hips, replace)
    act.frame_range = (keys[0][0], end)
    return act


def wave(t):
    return math.sin(t * 2 * math.pi)


ACTIONS = []
# Idle: дыхание грудью, голова покачивается, оружие чуть ходит
ACTIONS.append(action("Idle", [
    (1 + i * 12, {"Chest": [("X", 3 * wave(i / 4))], "Head": [("Z", 5 * wave(i / 4 + 0.25)), ("X", 3 * wave(i / 4))],
                  "LowerArm.R": [("Z", 4 * wave(i / 4))], "LowerArm.L": [("Z", -4 * wave(i / 4 + 0.5))]},
     (0, 0, -0.012 * (1 + wave(i / 4)) / 2), ())
    for i in range(4)], loop=True))


def walk(t):
    """Шаркающий шаг: t = 0..1 -- полный цикл (два шага)."""
    s = wave(t)
    lift_l = max(0.0, -wave(t + 0.18))       # колено сгибается, когда нога идёт вперёд
    lift_r = max(0.0, wave(t + 0.18))
    return {
        "UpperLeg.L": [("X", -26 * s)], "LowerLeg.L": [("X", 40 * lift_l)], "Foot.L": [("X", 12 * s)],
        "UpperLeg.R": [("X", 26 * s)], "LowerLeg.R": [("X", 40 * lift_r)], "Foot.R": [("X", -12 * s)],
        "UpperArm.L": [("X", 20 * s)], "UpperArm.R": [("X", -8 * s)],
        "Hips": [("Z", 7 * s)], "Chest": [("Z", -9 * s)], "Head": [("Z", 5 * s)],
    }


ACTIONS.append(action("Walk", [(1 + i * 4, walk(i / 6), (0, 0, -0.035 * abs(wave(i / 6 + 0.25))), ())
                               for i in range(6)], loop=True))

# Attack: удар наотмашь -- замах вправо-назад с поворотом корпуса, рубящий взмах справа налево, возврат в стойку
ARM_R = ("UpperArm.R", "LowerArm.R", "Hand.R")
ACTIONS.append(action("Attack", [
    (1, {}, (0, 0, 0), ()),
    (10, {"UpperArm.R": [("Y", -10), ("Z", -55)], "LowerArm.R": [("Z", -30)], "Hand.R": [("Z", -45)],
          "Chest": [("Z", -22), ("X", -4)], "Spine": [("Z", -10)], "Head": [("Z", 12)],
          "UpperArm.L": [("X", -20)], "UpperLeg.L": [("X", -8)]}, (0, 0.03, 0), ARM_R),
    (15, {"UpperArm.R": [("Y", -25), ("Z", 95)], "LowerArm.R": [("Z", 15)], "Hand.R": [("Z", 15)],
          "Chest": [("Z", 22), ("X", 10)], "Spine": [("Z", 10), ("X", 6)], "Head": [("Z", -8)],
          "UpperArm.L": [("X", 25)], "UpperLeg.R": [("X", -12)], "LowerLeg.R": [("X", 12)]}, (0, -0.06, -0.05), ARM_R),
    (20, {"UpperArm.R": [("Y", -40), ("Z", 120)], "LowerArm.R": [("Z", 30)], "Hand.R": [("Z", 25)],
          "Chest": [("Z", 30), ("X", 14)], "Spine": [("Z", 12), ("X", 8)], "Head": [("Z", -10)],
          "UpperArm.L": [("X", 30)], "UpperLeg.R": [("X", -12)], "LowerLeg.R": [("X", 14)]}, (0, -0.07, -0.06), ARM_R),
    (32, {}, (0, 0, 0), ()),
], loop=False))

DEATH_HIPS = (0, -0.62, -0.735)   # таз в позе лёжа (подобрано так, чтобы скелет лежал на полу)
# Death: удар назад, колени подгибаются, падает лицом вниз и лежит; кисть повёрнута -- оружие ложится плашмя
ACTIONS.append(action("Death", [
    (1, {}, (0, 0, 0), ()),
    (6, {"Chest": [("X", -18)], "Neck": [("X", -10)], "Head": [("X", -25)],
         "UpperArm.L": [("Z", 25)], "UpperArm.R": [("Z", -25)]}, (0, 0.04, 0.0), ()),
    (16, {"Hips": [("X", 20)], "Spine": [("X", 15)], "Chest": [("X", 20)], "Head": [("X", 25)],
          "UpperLeg.L": [("X", -55)], "LowerLeg.L": [("X", 95)], "Foot.L": [("X", -30)],
          "UpperLeg.R": [("X", -45)], "LowerLeg.R": [("X", 85)], "Foot.R": [("X", -30)],
          "UpperArm.L": [("X", -20)], "UpperArm.R": [("X", -10)]}, (0, -0.1, -0.42), ()),
    (26, {"Hips": [("X", 80)], "Spine": [("X", 8)], "Chest": [("X", 4)], "Head": [("Z", 25)],
          "UpperLeg.L": [("X", -10)], "LowerLeg.L": [("X", 30)], "Foot.L": [("X", 35)],
          "UpperLeg.R": [("X", -5)], "LowerLeg.R": [("X", 20)], "Foot.R": [("X", 35)],
          "UpperArm.L": [("X", -10), ("Y", -20)], "UpperArm.R": [("X", -5), ("Y", 20)],
          "LowerArm.R": [("Z", -20)], "Hand.R": [("X", -90), ("Z", -20)]}, (0, -0.55, -0.70), ("Hand.R",)),
    (30, {"Hips": [("X", 86)], "Spine": [("X", 4)], "Head": [("Z", 30), ("X", -10)],
          "UpperLeg.L": [("X", -8)], "LowerLeg.L": [("X", 25)], "Foot.L": [("X", 40)],
          "UpperLeg.R": [("X", -4)], "LowerLeg.R": [("X", 15)], "Foot.R": [("X", 40)],
          "UpperArm.L": [("X", 5), ("Y", -25)], "UpperArm.R": [("X", 5), ("Y", 25)],
          "LowerArm.R": [("Z", -10)], "Hand.R": [("X", -90), ("Z", -20)]}, DEATH_HIPS, ("Hand.R",)),
    (40, {"Hips": [("X", 86)], "Spine": [("X", 4)], "Head": [("Z", 30), ("X", -10)],
          "UpperLeg.L": [("X", -8)], "LowerLeg.L": [("X", 25)], "Foot.L": [("X", 40)],
          "UpperLeg.R": [("X", -4)], "LowerLeg.R": [("X", 15)], "Foot.R": [("X", 40)],
          "UpperArm.L": [("X", 5), ("Y", -25)], "UpperArm.R": [("X", 5), ("Y", 25)],
          "LowerArm.R": [("Z", -10)], "Hand.R": [("X", -90), ("Z", -20)]}, DEATH_HIPS, ("Hand.R",)),
], loop=False))
scene.frame_set(int(ACTIONS[3].frame_range[1]))
low = min((body.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh().vertices), key=lambda v: v.co.z).co.z
print(f"[Skeleton] Death: нижняя точка тела лёжа z={low:+.3f}")
rig.animation_data.action = ACTIONS[0]

# ---------------------------------------------------------------- сохранение и экспорт
print(f"[Skeleton] треугольников: тело {sum(len(p.vertices) - 2 for p in me.polygons)}, костей: {len(arm_data.bones)}, "
      f"анимации: {[a.name for a in ACTIONS]}")
os.makedirs(os.path.dirname(BLEND), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=BLEND, compress=True)
if os.path.exists(BLEND + "1"):
    os.remove(BLEND + "1")   # резервная копия Blender не нужна: модель пересобирается скриптом

bpy.ops.object.select_all(action="DESELECT")
for o in [body, rig] + weapons:
    o.select_set(True)
bpy.context.view_layer.objects.active = rig
fbx = os.path.join(OUT_DIR, "Skeleton.fbx")
bpy.ops.export_scene.fbx(
    filepath=fbx, use_selection=True, object_types={"ARMATURE", "MESH"},
    apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
    add_leaf_bones=False, mesh_smooth_type="FACE", path_mode="STRIP",
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
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

    def shot(path, angle_deg, frame=1, dist=3.6, height=1.0, target_z=0.85, target_x=0.0):
        a = math.radians(angle_deg)
        cam.location = (target_x + math.sin(a) * dist, -math.cos(a) * dist, height)
        cam.rotation_euler = (Vector((target_x, 0, target_z)) - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.frame_set(frame)
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)

    def show_weapon(name):
        for w in weapons:
            w.hide_render = w.name != "Weapon_" + name

    show_weapon(WEAPONS[0])
    for ang in (0, 35, 90):
        shot(os.path.join(PREVIEW, f"view_{ang}.png"), ang)
    shot(os.path.join(PREVIEW, "face.png"), 0, dist=0.8, height=1.65, target_z=1.62)
    for act, frames, ang, h, tz in ((ACTIONS[1], (1, 5, 9, 13, 17, 21), 55, 1.0, 0.85),
                                    (ACTIONS[2], (1, 10, 15, 20, 26, 32), 20, 1.6, 0.9),
                                    (ACTIONS[3], (1, 6, 16, 26, 30, 40), 70, 0.7, 0.45)):
        rig.animation_data.action = act
        for f in frames:
            shot(os.path.join(PREVIEW, f"{act.name}_{f:02d}.png"), ang, f, height=h, target_z=tz)
    rig.animation_data.action = ACTIONS[0]
    for i, name in enumerate(WEAPONS):
        show_weapon(name)
        shot(os.path.join(PREVIEW, f"weapon_{i:02d}.png"), -35, 1, dist=2.2, height=1.2, target_z=1.0, target_x=-0.35)
    print(f"[Skeleton] превью: {PREVIEW}")
