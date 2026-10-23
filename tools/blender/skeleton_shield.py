"""Скелет со щитом: PSX-скелет с оружием (art/enemies/skeleton.blend от skeleton_psx.py) + викингский щит в левой руке.

  blender -b --factory-startup -P tools/blender/skeleton_shield.py -- [--preview build/enemies/shield]

Сначала нужен skeleton_psx.py. Щит -- art/weapons/VikingShields/shields.blend (объект «Shield 1») и раскраски
art/weapons/VikingShields/textures/paint1..8.png.
Результат:
  art/enemies/skeleton_shield.blend
  Assets/Game/Assets/enemies/SkeletonShield/SkeletonShield.fbx  -- риг Humanoid; анимации Idle, Walk, Attack,
      Block (щит поднят, цикл), BlockHit (удар по щиту), Parry (отбив щитом в сторону), Death
  Assets/Game/Assets/enemies/SkeletonShield/Textures/*.png       -- SKS_Paint1..8 -- раскраски (выбирает SkeletonCreep)

Щит держится за рукоять за умбоном: кость Shield (ребёнок Hand.L) в кулаке. Как щит сидит в руке, задаёт поза
защиты: в ней щит смотрит вперёд и чуть наружу; в остальных позах кисть поворачивается от этой.
"""
import math
import os
import shutil
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from skeleton_common import (Pose, export_fbx, fists, material, merge, preview_stage, save_blend, shrink, tex,  # noqa: E402
                             walk_legs, wave)

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC_BLEND = os.path.join(ROOT, "art", "enemies", "skeleton.blend")
SHIELDS = os.path.join(ROOT, "art", "weapons", "VikingShields")
SRC_TEX_DIR = os.path.join(ROOT, "Assets", "Game", "Assets", "enemies", "Skeleton", "Textures")
OUT_DIR = os.path.join(ROOT, "Assets", "Game", "Assets", "enemies", "SkeletonShield")
TEX_DIR = os.path.join(OUT_DIR, "Textures")
BLEND = os.path.join(ROOT, "art", "enemies", "skeleton_shield.blend")
FPS = 24
SHIELD_DIAMETER = 0.75   # м
SHIELD_TRIS = 360
PAINTS = 8

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PREVIEW = os.path.abspath(argv[argv.index("--preview") + 1]) if "--preview" in argv else None

# ---------------------------------------------------------------- скелет с оружием, без анимаций
bpy.ops.wm.open_mainfile(filepath=SRC_BLEND)
scene = bpy.context.scene
body = bpy.data.objects["Body"]
rig = bpy.data.objects["Rig"]
weapons = [o for o in bpy.data.objects if o.name.startswith("Weapon_")]
for a in list(bpy.data.actions):
    bpy.data.actions.remove(a)
rig.animation_data_clear()
for o in list(bpy.data.objects):
    if o.type in {"CAMERA", "LIGHT"}:
        bpy.data.objects.remove(o)

# материалы скелета и оружия -- свои копии SKS_* в папке щитовика (у каждой модели в Unity свои материалы)
os.makedirs(TEX_DIR, exist_ok=True)
for m in bpy.data.materials:
    if not m.name.startswith("SKL_"):
        continue
    m.name = "SKS_" + m.name[4:]
    img = next(n.image for n in m.node_tree.nodes if n.type == "TEX_IMAGE")
    shutil.copyfile(os.path.join(SRC_TEX_DIR, img.name + ".png"), os.path.join(TEX_DIR, m.name + ".png"))
    img.name = m.name
    img.filepath = os.path.join(TEX_DIR, m.name + ".png")
    img.reload()

# ---------------------------------------------------------------- щит
with bpy.data.libraries.load(os.path.join(SHIELDS, "shields.blend")) as (src, dst):
    dst.objects = ["Shield 1"]
shield_src = dst.objects[0]
scene.render.fps = FPS
sme = shield_src.data.copy()
sme.transform(shield_src.matrix_basis)   # объект не в сцене: matrix_world не вычислена, берём loc/rot/scale
slots = [s.material.name for s in shield_src.material_slots]
co = [v.co for v in sme.vertices]
lo = Vector([min(c[k] for c in co) for k in range(3)])
hi = Vector([max(c[k] for c in co) for k in range(3)])
# в исходнике щит смотрит в -Y (умбон и заклёпки спереди), тыл -- на hi.y; рукоять -- за умбоном
grip_m = Vector(((lo.x + hi.x) / 2, hi.y + 0.2, (lo.z + hi.z) / 2))
S = SHIELD_DIAMETER / (hi.x - lo.x)
print(f"[Shield] исходник: {tuple(round(x, 2) for x in lo)} .. {tuple(round(x, 2) for x in hi)}")


def shield_local(p):
    """Исходник -> система щита: начало в рукояти, X -- вбок, Y -- вверх, Z -- вперёд (лицевая сторона)."""
    d = (p - grip_m) * S
    return Vector((d.x, d.z, -d.y))


TEX = {
    "paint": [material(TEX_DIR, f"SKS_Paint{i}", shrink(os.path.join(SHIELDS, "textures", f"paint{i}.png"), 64))
              for i in range(1, PAINTS + 1)],
    "steel": material(TEX_DIR, "SKS_ShieldSteel", shrink(os.path.join(SHIELDS, "textures", "steel.png"), 32) * 0.8),
    "wood": material(TEX_DIR, "SKS_ShieldWood", shrink(os.path.join(SHIELDS, "textures", "wood.png"), 64)),
    "nuts": material(TEX_DIR, "SKS_ShieldNuts", tex((0.2, 0.19, 0.18), 0.5)),
}
# материалы исходника: .002 -- обод и умбон, .001 -- заклёпки, .004/.003 -- доски, .005 -- раскраска
KIND = {"Material.002": "steel", "Material.001": "nuts", "Material.004": "wood", "Material.003": "wood",
        "Material.005": "paint"}
shield_mats = [TEX["paint"][0], TEX["steel"], TEX["wood"], TEX["nuts"]]
SLOT = {"paint": 0, "steel": 1, "wood": 2, "nuts": 3}
kinds = [KIND[slots[p.material_index]] for p in sme.polygons]
front = Vector((0, -1, 0))
for p, k in zip(sme.polygons, kinds):
    if k == "paint" and p.normal.dot(front) < 0:
        p.flip()                     # раскраска в исходнике смотрит внутрь щита -- в Unity её не было бы видно
sme.materials.clear()                # сбрасывает индексы граней -- ставим после
for m in shield_mats:
    sme.materials.append(m)
for p, k in zip(sme.polygons, kinds):
    p.material_index = SLOT[k]
    p.use_smooth = k == "steel"

# ---------------------------------------------------------------- позы
pose = Pose(rig)
L_FIST = pose.H["Hand.L"] + Vector((0.055, 0, -0.02))     # середина кулака (в T-позе ладонь вниз)
TORSO = {"Spine": [("X", 6)], "Chest": [("X", 6), ("Z", 8)], "Neck": [("X", 4)], "Head": [("Z", -8)]}
LEGS = {"UpperLeg.L": [("X", -14), ("Y", -4)], "LowerLeg.L": [("X", 16)], "Foot.L": [("X", -4)],
        "UpperLeg.R": [("X", 10), ("Y", 4)], "LowerLeg.R": [("X", 14)], "Foot.R": [("X", -6)]}
ARM_R = {"UpperArm.R": [("Y", -72), ("X", -14)], "LowerArm.R": [("Z", 40)], "Hand.R": [("Z", -20)]}
POLE_L = Vector((1, 0.2, -0.7))      # левый локоть -- наружу и вниз
GUARD_WRIST = Vector((0.22, -0.30, 1.12))


def left(wrist, turn=Quaternion(), pole=POLE_L):
    """Левая рука со щитом: запястье в точку, щит повёрнут на turn (мир) от позы защиты."""
    return {"UpperArm.L": [("ROLL", -90), ("IK", wrist, pole)], "Hand.L": [("ORIENT", turn @ HAND_GUARD)]}


def rot(axis, deg):
    return Quaternion(Vector({"X": (1, 0, 0), "Y": (0, 1, 0), "Z": (0, 0, 1)}[axis]), math.radians(deg))


# поза защиты задаёт, как щит сидит в кулаке: рука по IK, кисть -- как получилась
guard0 = merge(TORSO, LEGS, ARM_R, fists(60, 60), {"UpperArm.L": [("ROLL", -90), ("IK", GUARD_WRIST, POLE_L)]})
W0, P0, _ = pose.solve(guard0, (0, 0, -0.03))
HAND_GUARD = W0["Hand.L"].copy()
fist_w = pose.at("Hand.L", L_FIST, W0, P0)
face = Vector((0.18, -1, 0)).normalized()          # щит смотрит вперёд и чуть наружу
up = Vector((0, 0, 1))
SHIELD_WORLD = Matrix((up.cross(face), up, face)).transposed()   # система щита -> мир в позе защиты


def shield_place(p):
    return pose.to_rest("Hand.L", fist_w + SHIELD_WORLD @ shield_local(p), W0, P0)


for v in sme.vertices:
    v.co = shield_place(v.co.copy())
shield = bpy.data.objects.new("Shield", sme)
sme.name = "Shield"
scene.collection.objects.link(shield)
dec = shield.modifiers.new("Decimate", "DECIMATE")
dec.ratio = SHIELD_TRIS / sum(len(p.vertices) - 2 for p in sme.polygons)
dec.use_collapse_triangulate = True
bpy.context.view_layer.objects.active = shield
bpy.ops.object.modifier_apply(modifier=dec.name)

bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="EDIT")
eb = rig.data.edit_bones.new("Shield")
eb.head = L_FIST
eb.tail = L_FIST + W0["Hand.L"].inverted() @ face * 0.12
eb.parent = rig.data.edit_bones["Hand.L"]
bpy.ops.object.mode_set(mode="OBJECT")
pose.refresh()
shield.vertex_groups.new(name="Shield").add(list(range(len(sme.vertices))), 1.0, "REPLACE")
shield.parent = rig
shield.modifiers.new("Armature", "ARMATURE").object = rig
print(f"[Shield] щит: {sum(len(p.vertices) - 2 for p in sme.polygons)} треугольников, масштаб {S:.3f}")


def guard(wrist=GUARD_WRIST, turn=Quaternion(), *more):
    return merge(TORSO, LEGS, ARM_R, fists(60, 60), left(wrist, turn), *more)


# ---------------------------------------------------------------- анимации
ACTIONS = []
ACTIONS.append(pose.action("Idle", [
    (1 + i * 12, merge(guard(GUARD_WRIST + Vector((0, 0, 0.012 * wave(i / 4)))),
                       {"Chest": [("X", 3 * wave(i / 4))], "Head": [("Z", 4 * wave(i / 4 + 0.25))]}),
     (0, 0, -0.03 - 0.01 * (1 + wave(i / 4)) / 2)) for i in range(4)], loop=True))


def walk_spec(t):
    return merge(TORSO, ARM_R, fists(60, 60), left(GUARD_WRIST + Vector((0, -0.02 * wave(t), 0.02 * abs(wave(t))))),
                 walk_legs(t), {"UpperArm.R": [("X", -10 * wave(t))]})


ACTIONS.append(pose.action("Walk", [(1 + i * 4, walk_spec(i / 6), (0, 0, -0.035 * abs(wave(i / 6 + 0.25))))
                                    for i in range(6)], loop=True))

# Attack: удар наотмашь правой (как у крипа), щит держится на месте -- запястье левой по IK к той же точке
ARM_KEYS = ("UpperArm.R", "LowerArm.R", "Hand.R")


def swing(extra):
    s = guard()
    for b in ARM_KEYS:
        s[b] = []
    return merge(s, extra)


ACTIONS.append(pose.action("Attack", [
    (1, guard(), (0, 0, -0.03)),
    (10, swing({"UpperArm.R": [("Y", -10), ("Z", -55)], "LowerArm.R": [("Z", -30)], "Hand.R": [("Z", -45)],
                "Chest": [("Z", -22), ("X", -4)], "Spine": [("Z", -10)], "Head": [("Z", 12)]}), (0, 0.03, -0.03)),
    (15, swing({"UpperArm.R": [("Y", -25), ("Z", 95)], "LowerArm.R": [("Z", 15)], "Hand.R": [("Z", 15)],
                "Chest": [("Z", 22), ("X", 10)], "Spine": [("Z", 10), ("X", 6)], "Head": [("Z", -8)],
                "UpperLeg.R": [("X", -12)], "LowerLeg.R": [("X", 12)]}), (0, -0.06, -0.07)),
    (20, swing({"UpperArm.R": [("Y", -40), ("Z", 120)], "LowerArm.R": [("Z", 30)], "Hand.R": [("Z", 25)],
                "Chest": [("Z", 30), ("X", 14)], "Spine": [("Z", 12), ("X", 8)], "Head": [("Z", -10)]}),
     (0, -0.07, -0.08)),
    (32, guard(), (0, 0, -0.03)),
]))

# Block: щит поднят к лицу, скелет пригнулся за ним; лёгкая дрожь
BLOCK_WRIST = Vector((0.12, -0.31, 1.33))
CROUCH = {"Spine": [("X", 8)], "Neck": [("X", 8)], "UpperLeg.L": [("X", -10)], "LowerLeg.L": [("X", 14)],
          "UpperLeg.R": [("X", -8)], "LowerLeg.R": [("X", 16)]}


def block(dz=0.0, turn=Quaternion(), *more):
    return merge(guard(BLOCK_WRIST + Vector((0, 0, dz)), rot("Z", -10) @ turn), CROUCH, *more)


ACTIONS.append(pose.action("Block", [
    (1 + i * 8, block(0.008 * wave(i / 3)), (0, 0, -0.08 - 0.005 * wave(i / 3))) for i in range(3)], loop=True))

# BlockHit: удар по щиту -- щит вдавлен к телу и запрокинут, скелет отъезжает назад и снова встаёт в блок
ACTIONS.append(pose.action("BlockHit", [
    (1, block(), (0, 0, -0.08)),
    (3, merge(guard(BLOCK_WRIST + Vector((0, 0.09, 0.03)), rot("X", -18) @ rot("Z", -10)), CROUCH,
              {"Chest": [("X", -10)], "Head": [("X", -12)]}), (0, 0.07, -0.1)),
    (7, merge(guard(BLOCK_WRIST + Vector((0, 0.05, 0.01)), rot("X", -8) @ rot("Z", -10)), CROUCH,
              {"Chest": [("X", -4)]}), (0, 0.05, -0.09)),
    (14, block(), (0, 0.02, -0.08)),
]))

# Parry: щит уводится вправо и резко отбивается наружу-влево (отводит удар), корпус доворачивает
ACTIONS.append(pose.action("Parry", [
    (1, guard(), (0, 0, -0.03)),
    (4, merge(guard(Vector((0.02, -0.33, 1.22)), rot("Z", -30)), {"Chest": [("Z", -14)], "Spine": [("Z", -6)]}),
     (0, 0.01, -0.05)),
    (8, merge(guard(Vector((0.44, -0.27, 1.24)), rot("Z", 55)), {"Chest": [("Z", 20)], "Spine": [("Z", 8)],
                                                                     "Head": [("Z", -10)]}), (0.03, -0.05, -0.06)),
    (11, merge(guard(Vector((0.46, -0.24, 1.22)), rot("Z", 60)), {"Chest": [("Z", 22)], "Spine": [("Z", 8)]}),
     (0.03, -0.05, -0.06)),
    (22, guard(), (0, 0, -0.03)),
]))

# Death: как у крипа -- удар назад, на колени, лицом вниз; щит падает рядом плашмя
lying = {"Hips": [("X", 86)], "Spine": [("X", 4)], "Head": [("Z", 30), ("X", -10)],
         "UpperLeg.L": [("X", -8)], "LowerLeg.L": [("X", 25)], "Foot.L": [("X", 40)],
         "UpperLeg.R": [("X", -4)], "LowerLeg.R": [("X", 15)], "Foot.R": [("X", 40)]}
DEATH_ARMS = {"UpperArm.L": [("Y", 70), ("X", 5), ("Y", -25)], "UpperArm.R": [("Y", -70), ("X", 5), ("Y", 25)],
              "Hand.L": [("X", 90), ("Y", 60), ("X", -90)],   # кисть повёрнута -- щит ложится плашмя
              "Hand.R": [("X", -90), ("Z", -20)]}
ACTIONS.append(pose.action("Death", [
    (1, guard(), (0, 0, -0.03)),
    (6, merge(guard(), {"Chest": [("X", -18)], "Neck": [("X", -10)], "Head": [("X", -25)]}), (0, 0.04, 0)),
    (16, merge({"Hips": [("X", 20)], "Spine": [("X", 15)], "Chest": [("X", 20)], "Head": [("X", 25)],
                "UpperLeg.L": [("X", -55)], "LowerLeg.L": [("X", 95)], "Foot.L": [("X", -30)],
                "UpperLeg.R": [("X", -45)], "LowerLeg.R": [("X", 85)], "Foot.R": [("X", -30)],
                "UpperArm.L": [("Y", 74), ("X", -20)], "UpperArm.R": [("Y", -74), ("X", -10)]}, fists(40, 40)),
     (0, -0.1, -0.42)),
    (26, merge(lying, DEATH_ARMS, fists(30, 30)), (0, -0.55, -0.70)),
    (30, merge(lying, DEATH_ARMS, fists(20, 20)), (0, -0.62, -0.735)),
    (40, merge(lying, DEATH_ARMS, fists(20, 20)), (0, -0.62, -0.735)),
]))
rig.animation_data.action = ACTIONS[0]

# ---------------------------------------------------------------- сохранение и экспорт
gear = [shield] + weapons
print(f"[Shield] анимации: {[a.name for a in ACTIONS]}")
save_blend(BLEND)
fbx = os.path.join(OUT_DIR, "SkeletonShield.fbx")
export_fbx(fbx, [body] + gear, rig)
print(f"[Shield] FBX: {fbx}")

# ---------------------------------------------------------------- превью
if PREVIEW:
    os.makedirs(PREVIEW, exist_ok=True)
    shot = preview_stage(scene)
    for w in weapons:
        w.hide_render = w.name != "Weapon_Arming_Sword"
    for ang in (0, -40, 60):
        shot(os.path.join(PREVIEW, f"view_{ang}.png"), ang)
    for act, frames, ang in ((ACTIONS[2], (10, 15, 20), 30), (ACTIONS[3], (1,), 30), (ACTIONS[4], (3,), 30),
                             (ACTIONS[5], (1, 4, 8, 11), 20), (ACTIONS[1], (5,), 60), (ACTIONS[6], (16, 40), 70)):
        rig.animation_data.action = act
        for f in frames:
            shot(os.path.join(PREVIEW, f"{act.name}_{f:02d}.png"), ang, f)
    rig.animation_data.action = ACTIONS[0]
    paint_node = next(n for n in TEX["paint"][0].node_tree.nodes if n.type == "TEX_IMAGE")
    for i, m in enumerate(TEX["paint"]):
        paint_node.image = next(n.image for n in m.node_tree.nodes if n.type == "TEX_IMAGE")
        shot(os.path.join(PREVIEW, f"paint_{i + 1}.png"), -15, 1, dist=1.8, height=1.3, target_z=1.1)
    print(f"[Shield] превью: {PREVIEW}")
