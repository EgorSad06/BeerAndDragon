"""Скелет-лучник: PSX-скелет (art/enemies/skeleton.blend от skeleton_psx.py) + лук, колчан за спиной, стрела.

  blender -b --factory-startup -P tools/blender/skeleton_archer.py -- [--preview build/enemies/archer]

Сначала нужен skeleton_psx.py (он делает art/enemies/skeleton.blend). Модель лука, стрел и колчана --
art/weapons/BowQuiver/bow_quiver.fbx (один меш: лук, обмотка, тетива, колчан со стрелами, отдельная стрела).
Результат:
  art/enemies/skeleton_archer.blend
  Assets/Game/Assets/enemies/SkeletonArcher/SkeletonArcher.fbx   -- риг Generic (кости лука и стрелы анимированы),
                                                                  анимации Idle, Walk, Draw, Shoot, Death
  Assets/Game/Assets/enemies/SkeletonArcher/Textures/*.png

Как устроено натяжение (в Unity работает без кода -- всё запечено в кости при экспорте FBX):
  - лук на кости Bow (ребёнок Hand.L), середина тетивы -- на кости BowString (ребёнок Bow);
  - стрела на кости ArrowNock (ребёнок Hand.R, у пальцев); ограничение Damped Track держит её направленной
    на рукоять лука (кость ArrowRest), стрела видна, когда масштаб ArrowNock = 1;
  - BowString с ограничением Copy Location идёт за пальцами, пока влияние = 1 (стрела на тетиве);
    в Shoot влияние падает в 0 -- тетива распрямляется, стрела исчезает (в игре её заменит летящая стрела);
  - руки ставятся простым IK (плечо -> локоть -> запястье) к нужной точке: колчан, тетива, щека, вперёд на цель.
"""
import math
import os
import shutil
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC_BLEND = os.path.join(ROOT, "art", "enemies", "skeleton.blend")
BOW_FBX = os.path.join(ROOT, "art", "weapons", "BowQuiver", "bow_quiver.fbx")
SRC_TEX = os.path.join(ROOT, "Assets", "Game", "Assets", "enemies", "Skeleton", "Textures", "SKL_Bone.png")
OUT_DIR = os.path.join(ROOT, "Assets", "Game", "Assets", "enemies", "SkeletonArcher")
TEX_DIR = os.path.join(OUT_DIR, "Textures")
BLEND = os.path.join(ROOT, "art", "enemies", "skeleton_archer.blend")
FPS = 24
BOW_LENGTH = 1.2      # м, от рога до рога
ARROW_LENGTH = 0.78   # м
QUIVER_SCALE = 1.8    # колчан и стрелы в нём -- во столько раз больше исходника

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PREVIEW = os.path.abspath(argv[argv.index("--preview") + 1]) if "--preview" in argv else None

# ---------------------------------------------------------------- скелет из skeleton.blend, без оружия и анимаций
bpy.ops.wm.open_mainfile(filepath=SRC_BLEND)
scene = bpy.context.scene
scene.render.fps = FPS
body = bpy.data.objects["Body"]
rig = bpy.data.objects["Rig"]
arm_data = rig.data
for o in list(bpy.data.objects):
    if o.name.startswith("Weapon_"):
        bpy.data.objects.remove(o)
for a in list(bpy.data.actions):
    bpy.data.actions.remove(a)
rig.animation_data_clear()
for o in list(bpy.data.objects):
    if o.type in {"CAMERA", "LIGHT"}:
        bpy.data.objects.remove(o)

os.makedirs(TEX_DIR, exist_ok=True)
shutil.copyfile(SRC_TEX, os.path.join(TEX_DIR, "SKA_Bone.png"))
bone_mat = body.data.materials[0]
bone_mat.name = "SKA_Bone"
img = next(n.image for n in bone_mat.node_tree.nodes if n.type == "TEX_IMAGE")
img.name = "SKA_Bone"
img.filepath = os.path.join(TEX_DIR, "SKA_Bone.png")
img.reload()

# ---------------------------------------------------------------- текстуры PS1 для лука и колчана
rng = np.random.default_rng(9)
BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) / 16.0 - 0.5


def noise(n, scale):
    out = np.zeros((n, n))
    for octave, amp in ((4, 0.5), (8, 0.3), (16, 0.2)):
        g = rng.random((octave + 1, octave + 1))
        xs = np.linspace(0, octave, n, endpoint=False)
        i, f = xs.astype(int), xs - xs.astype(int)
        y0 = g[i][:, i] * (1 - f)[None, :] + g[i][:, i + 1] * f[None, :]
        y1 = g[i + 1][:, i] * (1 - f)[None, :] + g[i + 1][:, i + 1] * f[None, :]
        out += amp * (y0 * (1 - f)[:, None] + y1 * f[:, None])
    return (out - out.min()) / (np.ptp(out) + 1e-6) * scale


def ps1(rgb):
    """15-битный цвет с упорядоченным дизерингом и зерном, как в skeleton_psx.py."""
    n = rgb.shape[0]
    rgb = rgb * (0.9 + 0.2 * rng.random((n, n, 1)))
    dither = np.tile(BAYER, (n // 4, n // 4))[..., None] / 31
    return np.floor(np.clip(rgb + dither, 0, 1) * 31 + 0.5) / 31


def tex(color, spread=0.4, stripes=0):
    n = 32
    base = np.array(color) * (1 - spread / 2 + spread * noise(n, 1.0))[..., None]
    if stripes:
        base[::stripes] *= 0.6
    return ps1(base)


def save_texture(name, rgb):
    h, w = rgb.shape[:2]
    im = bpy.data.images.new(name, w, h)
    px = np.ones((h, w, 4))
    px[:, :, :3] = np.clip(rgb, 0, 1)
    im.pixels = px[::-1].ravel()
    im.filepath_raw = os.path.join(TEX_DIR, name + ".png")
    im.file_format = "PNG"
    im.save()
    return im


def material(name, rgb):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.9
    t = nt.nodes.new("ShaderNodeTexImage")
    t.image, t.interpolation = save_texture(name, rgb), "Closest"
    nt.links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
    return m


MATS = {
    "wood": material("SKA_BowWood", tex((0.30, 0.17, 0.09), 0.5, stripes=4)),
    "grip": material("SKA_Grip", tex((0.20, 0.12, 0.07), 0.5, stripes=2)),
    "string": material("SKA_String", tex((0.70, 0.66, 0.55), 0.2)),
    "quiver": material("SKA_Quiver", tex((0.33, 0.21, 0.12), 0.6, stripes=8)),
    "shaft": material("SKA_Shaft", tex((0.55, 0.42, 0.26), 0.3)),
    "head": material("SKA_ArrowHead", tex((0.45, 0.45, 0.47), 0.4)),
    "feather": material("SKA_Feather", tex((0.62, 0.58, 0.52), 0.5, stripes=3)),
}
MAT_ORDER = list(MATS)

# ---------------------------------------------------------------- модель лука: куски по связности
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=BOW_FBX)
src = next(o for o in bpy.data.objects if o not in before and o.type == "MESH")
scene.render.fps = FPS   # импорт FBX ставит сцене частоту кадров из файла
src_me = src.data
src_me.transform(src.matrix_world)
islands = []
bm = bmesh.new()
bm.from_mesh(src_me)
bm.verts.ensure_lookup_table()
seen = set()
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
bm.free()
co = [v.co.copy() for v in src_me.vertices]


def box(isl):
    pts = [co[i] for i in isl]
    return (Vector([min(p[k] for p in pts) for k in range(3)]), Vector([max(p[k] for p in pts) for k in range(3)]))


islands.sort(key=len, reverse=True)
parts = {"bow": [islands[0]], "quiver": [islands[1]], "string": [], "grip": [], "quiver_arrows": [],
         "quiver_fletch": [], "arrow": [], "arrow_fletch": []}
for isl in islands[2:]:
    lo, hi = box(isl)
    size = hi - lo
    if len(isl) <= 20 and size.length > 0.4:
        parts["string"].append(isl)                 # исходную тетиву заменим своей (с серединой на кости)
    elif len(isl) >= 100 and size.length > 0.4:
        parts["arrow" if size.x > size.y else "quiver_arrows"].append(isl)
    elif len(isl) <= 20:
        parts["arrow_fletch" if lo.x < -0.2 else "quiver_fletch"].append(isl)
    else:
        parts["grip"].append(isl)
print("[Archer] куски:", {k: len(v) for k, v in parts.items()})

# система координат лука в исходнике: концы тетивы -- рога, рукоять -- середина обмотки
string_pts = [co[i] for i in parts["string"][0]]
tip_a = max(string_pts, key=lambda p: p.y)
tip_b = min(string_pts, key=lambda p: p.y)
grip_c = sum((co[i] for i in parts["grip"][0]), Vector()) / len(parts["grip"][0])
bow_up = (tip_a - tip_b).normalized()
nock_m = tip_b + bow_up * (grip_c - tip_b).dot(bow_up)             # точка тетивы напротив рукояти
bow_back = (nock_m - grip_c).normalized()                          # от рукояти к тетиве (к лучнику)
BOW_SCALE = BOW_LENGTH / (tip_a - tip_b).length
BRACE = (nock_m - grip_c).length * BOW_SCALE
print(f"[Archer] лук: масштаб {BOW_SCALE:.2f}, от рукояти до тетивы {BRACE:.3f} м")


def bow_local(p):
    """Исходник -> система лука: начало в рукояти, X -- вбок, Y -- вверх (к рогу), Z -- к тетиве."""
    d = (p - grip_c) * BOW_SCALE
    side = bow_up.cross(bow_back)
    return Vector((d.dot(side), d.dot(bow_up), d.dot(bow_back)))


def build_mesh(name, chunks, place, ratio=None):
    """chunks: [(остров, функция материала(центр грани в исходнике) -> ключ MATS)] -> объект, вершины через place()."""
    bm = bmesh.new()
    uv = bm.loops.layers.uv.verify()
    for isl, mat_of in chunks:
        idx = set(isl)
        vmap = {}
        for i in isl:
            vmap[i] = bm.verts.new(place(co[i]))
        for p in src_me.polygons:
            if p.vertices[0] in idx:
                f = bm.faces.new([vmap[i] for i in p.vertices])
                c = sum((co[i] for i in p.vertices), Vector()) / len(p.vertices)
                f.material_index = MAT_ORDER.index(mat_of(c))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    scene.collection.objects.link(obj)
    for k in MAT_ORDER:
        me.materials.append(MATS[k])
    if ratio:
        dec = obj.modifiers.new("Decimate", "DECIMATE")
        dec.ratio = ratio
        dec.use_collapse_triangulate = True
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=dec.name)
    uvl = me.uv_layers.active or me.uv_layers.new()
    for p in me.polygons:
        p.use_smooth = MAT_ORDER[p.material_index] in ("wood", "quiver", "grip")
        nx, ny, nz = (abs(c) for c in p.normal)
        for li in p.loop_indices:
            c = me.vertices[me.loops[li].vertex_index].co
            a, b = (c.y, c.z) if nx >= ny and nx >= nz else (c.x, c.z) if ny >= nz else (c.x, c.y)
            uvl.data[li].uv = (a * 8, b * 8)
    return obj


def arrow_mat(nock, tip):
    """Стрела: последние 9 % длины к наконечнику -- железо, остальное древко."""
    axis = tip - nock

    def f(c):
        return "head" if (c - nock).dot(axis) / axis.length_squared > 0.91 else "shaft"
    return f


# ---------------------------------------------------------------- поза: повороты осями мира в T-позе + IK рук
AX = {"X": Vector((1, 0, 0)), "Y": Vector((0, 1, 0)), "Z": Vector((0, 0, 1))}


def hierarchy():
    order, todo = [], [b for b in arm_data.bones if b.parent is None]
    while todo:
        b = todo.pop(0)
        order.append(b.name)
        todo += list(b.children)
    return order


def rest_data():
    global ORDER, H, T, REST, PARENT
    ORDER = hierarchy()
    H = {b.name: b.head_local.copy() for b in arm_data.bones}
    T = {b.name: b.tail_local.copy() for b in arm_data.bones}
    REST = {b.name: b.matrix_local.to_quaternion() for b in arm_data.bones}
    PARENT = {b.name: b.parent.name if b.parent else None for b in arm_data.bones}


rest_data()


def solve(spec, hips=(0, 0, 0)):
    """spec: кость -> список: ("X", град) -- поворот осью мира T-позы (по порядку), ("ROLL", град) -- вокруг своей
    оси, ("IK", цель, полюс) -- у плеча: запястье в точку цель, локоть в сторону полюс; ("AIM", направление);
    ("ORIENT", кватернион) -- поворот кости в мире.
    Ответ: W (поворот кости в мире относительно T-позы), P (положение головы кости), own (свой поворот)."""
    W, P, own, pending = {}, {}, {}, {}
    for b in ORDER:
        p = PARENT[b]
        wp = W[p] if p else Quaternion()
        P[b] = P[p] + wp @ (H[b] - H[p]) if p else H[b] + Vector(hips)
        d = (T[b] - H[b]).normalized()
        o = Quaternion()
        for item in spec.get(b, []) + pending.pop(b, []):
            kind = item[0]
            if kind == "ROLL":
                o = o @ Quaternion(d, math.radians(item[1]))
            elif kind == "ORIENT":                     # поворот кости в мире задан целиком
                o = wp.inverted() @ item[1]
            elif kind == "AIM":
                o = (o @ d).rotation_difference(wp.inverted() @ Vector(item[1]).normalized()) @ o
            elif kind == "IK":
                target, pole = Vector(item[1]), Vector(item[2])
                lower = next(c.name for c in arm_data.bones[b].children)
                hand = next(c.name for c in arm_data.bones[lower].children if c.name.startswith("Hand"))
                l1, l2 = (H[lower] - H[b]).length, (H[hand] - H[lower]).length
                to = target - P[b]
                dist = min(to.length, (l1 + l2) * 0.999)
                u = to.normalized()
                v = (pole - u * pole.dot(u)).normalized()
                a = (l1 * l1 - l2 * l2 + dist * dist) / (2 * dist)
                elbow = P[b] + u * a + v * math.sqrt(max(l1 * l1 - a * a, 0.0))
                o = (o @ d).rotation_difference(wp.inverted() @ (elbow - P[b]).normalized()) @ o
                pending[lower] = [("AIM", P[b] + u * dist - elbow)]
            else:
                o = Quaternion(AX[kind], math.radians(item[1])) @ o
        own[b] = o
        W[b] = wp @ o
    return W, P, own


def at(bone, rest_point, W, P):
    """Где окажется точка, жёстко привязанная к кости (в T-позе rest_point), в позе W, P."""
    return P[bone] + W[bone] @ (Vector(rest_point) - H[bone])


L_FIST = H["Hand.L"] + Vector((0.055, 0, -0.02))     # середина кулака (в T-позе ладонь вниз)
R_FINGERS = H["Index2.R"] + Vector((0, 0, -0.01))  # где пальцы держат стрелу
TORSO_DRAW = {"Spine": [("Z", -22)], "Chest": [("Z", -22), ("X", -4)], "Neck": [("Z", 15)], "Head": [("Z", 25)]}
FINGERS = [f"{f}{i}" for f in ("Index", "Middle", "Ring", "Little") for i in (1, 2, 3)]


def merge(*specs):
    out = {}
    for s in specs:
        for b, rots in s.items():
            out[b] = out.get(b, []) + list(rots)
    return out


def fists(curl_l=60, curl_r=60):
    p = {}
    for s, side, curl in ((1, "L", curl_l), (-1, "R", curl_r)):
        for f in FINGERS:
            p[f"{f}.{side}"] = [("Y", s * curl)]
        p[f"Thumb2.{side}"] = [("X", -25)]
    return p


LEGS_STAND = {"UpperLeg.L": [("X", -4), ("Y", -4)], "LowerLeg.L": [("X", 8)],
              "UpperLeg.R": [("X", -4), ("Y", 4)], "LowerLeg.R": [("X", 8)]}
LEGS_AIM = {"UpperLeg.L": [("X", -14), ("Y", -8)], "LowerLeg.L": [("X", 12)],
            "UpperLeg.R": [("X", 10), ("Y", 6)], "LowerLeg.R": [("X", 10)], "Foot.R": [("X", -8)]}


def spec_idle():
    """Стоит наготове: лук вертикально в левой руке (кисть повёрнута как при прицеле), правая висит."""
    return merge({"Hand.L": [("ORIENT", HAND_L_AIM)], "Spine": [("X", 5)], "Chest": [("X", 5)], "Neck": [("X", 6)], "Head": [("X", -4)],
                  "UpperArm.L": [("ROLL", -90), ("IK", (0.26, -0.2, 1.0), (0.5, 1, -0.6))],
                  "UpperArm.R": [("IK", (-0.27, 0.12, 0.92), (-0.6, 1, 0))]},
                 LEGS_STAND, fists(60, 45))


def aim_points(W, P):
    """В позе натяжения: щека (куда тянется тетива) и точка, куда смотрит левая рука."""
    cheek = at("Head", H["Head"] + Vector((-0.055, -0.1, 0.0)), W, P)
    return cheek


def spec_aim(draw):
    """Целится: корпус развёрнут левым плечом к цели, левая рука вперёд, правое запястье -- между тетивой
    (draw = 0) и щекой (draw = 1)."""
    s = merge(TORSO_DRAW, LEGS_AIM, fists(60, 35))
    W, P, _ = solve(s)
    shoulder_l = P["UpperArm.L"]
    wrist_l = shoulder_l + Vector((0.0, -0.53, 0.03))
    s = merge(s, {"UpperArm.L": [("ROLL", -90), ("IK", wrist_l, (1, 0.3, -0.4))]})
    W, P, _ = solve(s)
    fist = at("Hand.L", L_FIST, W, P)
    cheek = aim_points(W, P)
    line = (cheek - fist).normalized()
    string_rest = fist + line * BRACE
    anchor = string_rest.lerp(cheek, draw)
    # запястье правой чуть позади пальцев (пальцы на тетиве)
    wrist_r = anchor + line * 0.07 + Vector((0, 0, 0.02))
    return merge(s, {"UpperArm.R": [("IK", wrist_r, (-0.7, 0.6, 0.25))]})


def spec_quiver():
    """Правая рука за плечом у колчана."""
    s = merge({"Spine": [("X", 3)], "Chest": [("Z", 10)], "Head": [("Z", -15)]}, LEGS_STAND, fists(60, 70))
    W, P, _ = solve(s)
    top = at("Chest", QUIVER_TOP, W, P)
    return merge(s, {"UpperArm.L": [("ROLL", -90), ("IK", (0.26, -0.2, 1.0), (0.5, 1, -0.6))],
                     "Hand.L": [("ORIENT", HAND_L_AIM)],
                     "UpperArm.R": [("IK", top + Vector((0.0, -0.03, 0.06)), (-0.4, -0.6, 0.9))]})


# ---------------------------------------------------------------- колчан за спиной (на кости Chest)
q_isl = parts["quiver"][0]
q_lo, q_hi = box(q_isl)
q_center_m = (q_lo + q_hi) / 2
Q_CENTER = Vector((-0.06, 0.31, 1.22))
Q_UP = Vector((-0.38, 0.06, 0.92)).normalized()
q_out = (Vector((0, 1, 0)) - Q_UP * Q_UP.y).normalized()
q_side = Q_UP.cross(q_out)
Q_ROT = Matrix((q_side, Q_UP, q_out)).transposed()   # исходник: X -> вбок, Y (вверх колчана) -> Q_UP, Z -> наружу


def quiver_place(p):
    return Q_CENTER + Q_ROT @ ((p - q_center_m) * QUIVER_SCALE)


fletch_top = max(co[i].y for isl in parts["quiver_fletch"] for i in isl)
QUIVER_TOP = quiver_place(Vector((q_center_m.x, fletch_top, q_center_m.z)))
quiver_chunks = [(q_isl, lambda c: "quiver")]
for isl in parts["quiver_arrows"]:
    lo, hi = box(isl)
    quiver_chunks.append((isl, arrow_mat(Vector((0, hi.y, 0)), Vector((0, lo.y, 0)))))
quiver_chunks += [(isl, lambda c: "feather") for isl in parts["quiver_fletch"]]
quiver = build_mesh("Quiver", quiver_chunks, quiver_place, ratio=0.15)

# ---------------------------------------------------------------- лук: место в кулаке задаётся позой прицеливания
W_aim, P_aim, _ = solve(spec_aim(1.0))
HAND_L_AIM = W_aim["Hand.L"].copy()
fist_w = at("Hand.L", L_FIST, W_aim, P_aim)
cheek_w = aim_points(W_aim, P_aim)
back_w = cheek_w - fist_w
back_w.z = 0
back_w.normalize()
up_w = Vector((0, 0, 1))
BOW_WORLD = Matrix((up_w.cross(back_w), up_w, back_w)).transposed()   # система лука -> мир в позе прицела
to_rest = W_aim["Hand.L"].inverted()                                  # мир позы -> T-поза (для кисти)


def bow_place(p):
    world = fist_w + BOW_WORLD @ bow_local(p)
    return H["Hand.L"] + to_rest @ (world - P_aim["Hand.L"])


bow_chunks = [(isl, lambda c: "wood") for isl in parts["bow"]] + [(isl, lambda c: "grip") for isl in parts["grip"]]
bow = build_mesh("Bow", bow_chunks, bow_place, ratio=0.22)
FIST_REST = L_FIST
STRING_REST = bow_place(nock_m)
TIP_A, TIP_B = bow_place(tip_a), bow_place(tip_b)
ARROW_REST_PT = H["Hand.L"] + to_rest @ (fist_w + Vector((0, 0, 0.035)) - P_aim["Hand.L"])

# тетива: две тонкие трёхгранные палки рог -> середина -> рог; середина -- на кости BowString
bm = bmesh.new()
deform = bm.verts.layers.deform.verify()
rings = []
for p in (TIP_A, STRING_REST, TIP_B):
    axis = (TIP_A - TIP_B).normalized()
    side = axis.orthogonal().normalized()
    ring = [bm.verts.new(p + Quaternion(axis, k * 2 * math.pi / 3) @ side * 0.003) for k in range(3)]
    rings.append(ring)
for r0, r1 in zip(rings, rings[1:]):
    for k in range(3):
        f = bm.faces.new((r0[k], r0[(k + 1) % 3], r1[(k + 1) % 3], r1[k]))
        f.material_index = MAT_ORDER.index("string")
string_me = bpy.data.meshes.new("BowString")
bm.to_mesh(string_me)
bm.free()

# стрела на тетиве: хвост в начале координат, наконечник по -Y (как кость ArrowNock в T-позе)
a_isl = parts["arrow"][0]
a_lo, a_hi = box(a_isl)
a_nock_m = Vector((a_lo.x, (a_lo.y + a_hi.y) / 2, (a_lo.z + a_hi.z) / 2))
a_tip_m = max((co[i] for i in a_isl), key=lambda p: p.x)
a_dir = (a_tip_m - a_nock_m).normalized()
a_scale = ARROW_LENGTH / (a_tip_m - a_nock_m).length
a_rot = a_dir.rotation_difference(Vector((0, -1, 0)))


def arrow_place(p):
    return R_FINGERS + a_rot @ ((p - a_nock_m) * a_scale)


arrow = build_mesh("Arrow", [(a_isl, arrow_mat(a_nock_m, a_tip_m))] +
                   [(isl, lambda c: "feather") for isl in parts["arrow_fletch"]], arrow_place, ratio=0.4)
bpy.data.objects.remove(src)

# ---------------------------------------------------------------- кости лука и стрелы, привязка мешей
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="EDIT")
eb = arm_data.edit_bones
for name in ("WeaponSocket.L", "WeaponSocket.R"):
    eb.remove(eb[name])                              # у лучника свои кости оружия
b = eb.new("Bow")
b.head, b.tail = FIST_REST, FIST_REST + to_rest @ Vector((0, 0, 0.15))
b.parent = eb["Hand.L"]
b = eb.new("BowString")
b.head, b.tail = STRING_REST, STRING_REST + (TIP_A - TIP_B).normalized() * 0.05
b.parent = eb["Bow"]
b = eb.new("ArrowRest")
b.head, b.tail = ARROW_REST_PT, ARROW_REST_PT + Vector((0, 0, 0.03))
b.parent = eb["Bow"]
b.use_deform = False
b = eb.new("ArrowNock")
b.head, b.tail = R_FINGERS, R_FINGERS + Vector((0, -0.1, 0))
b.parent = eb["Hand.R"]
bpy.ops.object.mode_set(mode="OBJECT")
rest_data()

pb = rig.pose.bones["ArrowNock"]
c = pb.constraints.new("DAMPED_TRACK")
c.target, c.subtarget, c.track_axis = rig, "ArrowRest", "TRACK_Y"
pb = rig.pose.bones["BowString"]
c = pb.constraints.new("COPY_LOCATION")
c.name = "FollowNock"
c.target, c.subtarget = rig, "ArrowNock"


def skin(obj, bone_of_vertex):
    obj.vertex_groups.clear()
    for v in obj.data.vertices:
        name = bone_of_vertex(v)
        g = obj.vertex_groups.get(name) or obj.vertex_groups.new(name=name)
        g.add([v.index], 1.0, "REPLACE")
    obj.parent = rig
    obj.modifiers.new("Armature", "ARMATURE").object = rig


string_obj = bpy.data.objects.new("BowString", string_me)
scene.collection.objects.link(string_obj)
for m in MAT_ORDER:
    string_me.materials.append(MATS[m])
skin(bow, lambda v: "Bow")
skin(string_obj, lambda v: "BowString" if 3 <= v.index < 6 else "Bow")
skin(quiver, lambda v: "Chest")
skin(arrow, lambda v: "ArrowNock")
gear = [bow, string_obj, quiver, arrow]
print("[Archer] треугольников: " + ", ".join(f"{o.name} {sum(len(p.vertices) - 2 for p in o.data.polygons)}"
                                            for o in [body] + gear))

# ---------------------------------------------------------------- анимации
LAST = {}


def key_pose(frame, spec, hips=(0, 0, 0), nocked=0.0, arrow_shown=0.0):
    _, _, own = solve(spec, hips)
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
        quat = REST[pb.name].inverted() @ own[pb.name] @ REST[pb.name]
        prev = LAST.get(pb.name)
        if prev is not None and prev.dot(quat) < 0:      # без «переворотов» между ключами
            quat.negate()
        LAST[pb.name] = quat
        pb.rotation_quaternion = quat
        pb.keyframe_insert("rotation_quaternion", frame=frame)
    hb = rig.pose.bones["Hips"]
    hb.location = REST["Hips"].inverted() @ Vector(hips)
    hb.keyframe_insert("location", frame=frame)
    nb = rig.pose.bones["ArrowNock"]
    nb.scale = Vector((1, 1, 1)) * max(arrow_shown, 0.001)
    nb.keyframe_insert("scale", frame=frame)
    con = rig.pose.bones["BowString"].constraints["FollowNock"]
    con.influence = nocked
    con.keyframe_insert("influence", frame=frame)


def action(name, keys, loop=False):
    """keys: [(кадр, spec, таз, тетива на стреле 0..1, стрела видна 0..1)]."""
    rig.animation_data_create()
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    rig.animation_data.action = act
    LAST.clear()
    for k in keys:
        key_pose(*k)
    end = keys[-1][0]
    if loop:
        end += keys[1][0] - keys[0][0]
        key_pose(end, *keys[0][1:])
    act.frame_range = (keys[0][0], end)
    return act


def wave(t):
    return math.sin(t * 2 * math.pi)


ACTIONS = []
ACTIONS.append(action("Idle", [
    (1 + i * 12, merge(spec_idle(), {"Chest": [("X", 3 * wave(i / 4))], "Head": [("Z", 5 * wave(i / 4 + 0.25))]}),
     (0, 0, -0.012 * (1 + wave(i / 4)) / 2), 0, 0) for i in range(4)], loop=True))


def walk(t):
    s = wave(t)
    return {"UpperLeg.L": [("X", -24 * s)], "LowerLeg.L": [("X", 38 * max(0.0, -wave(t + 0.18)))],
            "UpperLeg.R": [("X", 24 * s)], "LowerLeg.R": [("X", 38 * max(0.0, wave(t + 0.18)))],
            "Foot.L": [("X", 10 * s)], "Foot.R": [("X", -10 * s)],
            "Hips": [("Z", 6 * s)], "Chest": [("Z", -8 * s)], "Head": [("Z", 5 * s)]}


def walk_spec(t):
    s = merge(spec_idle(), walk(t))
    s["UpperArm.R"] = [("IK", (-0.27, 0.12 - 0.14 * wave(t), 0.93 + 0.03 * abs(wave(t))), (-0.6, 1, 0))]
    return s


ACTIONS.append(action("Walk", [(1 + i * 4, walk_spec(i / 6), (0, 0, -0.035 * abs(wave(i / 6 + 0.25))), 0, 0)
                               for i in range(6)], loop=True))

# Draw: рука за плечо к колчану, достаёт стрелу, поднимает лук, накладывает стрелу и натягивает до щеки
ACTIONS.append(action("Draw", [
    (1, spec_idle(), (0, 0, 0), 0, 0),
    (8, spec_quiver(), (0, 0, 0), 0, 0),
    (9, spec_quiver(), (0, 0, 0), 0, 1),
    (14, merge(spec_quiver(), {"UpperArm.R": [("Z", 25), ("X", -15)]}), (0, 0, 0), 0, 1),
    (20, spec_aim(0.0), (0, 0, -0.02), 0, 1),
    (22, spec_aim(0.05), (0, 0, -0.02), 1, 1),
    (32, spec_aim(1.0), (0, 0, -0.03), 1, 1),
    (36, spec_aim(1.0), (0, 0, -0.03), 1, 1),
]))

# Shoot: отпускает тетиву (кадр 3 -- выстрел), рука отлетает назад, лук опускается
release = spec_aim(1.0)
ACTIONS.append(action("Shoot", [
    (1, spec_aim(1.0), (0, 0, -0.03), 1, 1),
    (2, spec_aim(1.0), (0, 0, -0.03), 1, 1),
    (3, merge(release, {"Hand.R": [("Z", -25)]}), (0, 0, -0.03), 0, 0),
    (6, merge(spec_aim(1.25), {"Hand.R": [("Z", -35)], "Chest": [("X", -3)]}), (0, 0.01, -0.03), 0, 0),
    (12, spec_aim(1.2), (0, 0, -0.02), 0, 0),
    (24, spec_idle(), (0, 0, 0), 0, 0),
]))

# Death: удар назад, колени подгибаются, падает лицом вниз, лук плашмя рядом
DEATH_ARMS = {"UpperArm.L": [("Y", 70), ("X", 5), ("Y", -25)], "UpperArm.R": [("Y", -70), ("X", 5), ("Y", 25)],
              "Hand.L": [("X", -90), ("Z", -15)]}   # кисть повёрнута -- лук ложится плашмя
lying = {"Hips": [("X", 86)], "Spine": [("X", 4)], "Head": [("Z", 30), ("X", -10)],
         "UpperLeg.L": [("X", -8)], "LowerLeg.L": [("X", 25)], "Foot.L": [("X", 40)],
         "UpperLeg.R": [("X", -4)], "LowerLeg.R": [("X", 15)], "Foot.R": [("X", 40)]}
ACTIONS.append(action("Death", [
    (1, spec_idle(), (0, 0, 0), 0, 0),
    (6, merge(spec_idle(), {"Chest": [("X", -18)], "Neck": [("X", -10)], "Head": [("X", -25)]}), (0, 0.04, 0), 0, 0),
    (16, merge({"Hips": [("X", 20)], "Spine": [("X", 15)], "Chest": [("X", 20)], "Head": [("X", 25)],
                "UpperLeg.L": [("X", -55)], "LowerLeg.L": [("X", 95)], "Foot.L": [("X", -30)],
                "UpperLeg.R": [("X", -45)], "LowerLeg.R": [("X", 85)], "Foot.R": [("X", -30)],
                "UpperArm.L": [("Y", 74), ("X", -20)], "UpperArm.R": [("Y", -74), ("X", -10)]}, fists(30, 30)),
     (0, -0.1, -0.42), 0, 0),
    (26, merge(lying, DEATH_ARMS, fists(20, 20)), (0, -0.55, -0.70), 0, 0),
    (30, merge(lying, DEATH_ARMS, fists(10, 10)), (0, -0.62, -0.735), 0, 0),
    (40, merge(lying, DEATH_ARMS, fists(10, 10)), (0, -0.62, -0.735), 0, 0),
]))
rig.animation_data.action = ACTIONS[0]

# ---------------------------------------------------------------- сохранение и экспорт
os.makedirs(os.path.dirname(BLEND), exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=BLEND, compress=True)
if os.path.exists(BLEND + "1"):
    os.remove(BLEND + "1")

bpy.ops.object.select_all(action="DESELECT")
for o in [body, rig] + gear:
    o.select_set(True)
bpy.context.view_layer.objects.active = rig
fbx = os.path.join(OUT_DIR, "SkeletonArcher.fbx")
bpy.ops.export_scene.fbx(
    filepath=fbx, use_selection=True, object_types={"ARMATURE", "MESH"},
    apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
    add_leaf_bones=False, mesh_smooth_type="FACE", path_mode="STRIP",
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
print(f"[Archer] анимации: {[a.name for a in ACTIONS]}")
print(f"[Archer] FBX: {fbx}")

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

    def shot(path, angle_deg, frame=1, dist=3.6, height=1.1, target_z=0.9):
        a = math.radians(angle_deg)
        cam.location = (math.sin(a) * dist, -math.cos(a) * dist, height)
        cam.rotation_euler = (Vector((0, 0, target_z)) - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.frame_set(frame)
        scene.render.filepath = path
        bpy.ops.render.render(write_still=True)

    for ang in (0, 40, 140):
        shot(os.path.join(PREVIEW, f"view_{ang}.png"), ang)
    for act, frames, ang in ((ACTIONS[2], (1, 8, 14, 20, 26, 36), 50), (ACTIONS[3], (1, 3, 6, 12, 24), 50),
                             (ACTIONS[1], (1, 9), 50), (ACTIONS[4], (16, 40), 70)):
        rig.animation_data.action = act
        for f in frames:
            shot(os.path.join(PREVIEW, f"{act.name}_{f:02d}.png"), ang, f)
    rig.animation_data.action = ACTIONS[2]
    shot(os.path.join(PREVIEW, "draw_front.png"), -10, 36, dist=2.4, height=1.5, target_z=1.35)
    shot(os.path.join(PREVIEW, "draw_top.png"), 90, 36, dist=2.4, height=1.5, target_z=1.35)
    print(f"[Archer] превью: {PREVIEW}")
