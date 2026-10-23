"""Общее для скелетов-врагов (skeleton_archer.py, skeleton_shield.py): позы, IK рук, ключи анимаций, PSX-текстуры,
превью, экспорт.

Поза -- словарь «кость -> список команд», команды применяются по порядку:
  ("X" | "Y" | "Z", град)  -- поворот вокруг оси мира в T-позе (как если бы кость поворачивали в позе покоя);
  ("ROLL", град)           -- вокруг своей оси;
  ("IK", цель, полюс)      -- у плеча: запястье в точку цель (мир), локоть в сторону полюс;
  ("AIM", направление)     -- направить кость (мир);
  ("ORIENT", кватернион)   -- поворот кости в мире задан целиком.
Ребёнок наследует поворот родителя. Координаты -- метры, Z вверх, лицо к -Y.
"""
import math
import os

import bpy
import numpy as np
from mathutils import Quaternion, Vector

AX = {"X": Vector((1, 0, 0)), "Y": Vector((0, 1, 0)), "Z": Vector((0, 0, 1))}
FINGERS = [f"{f}{i}" for f in ("Index", "Middle", "Ring", "Little") for i in (1, 2, 3)]


class Pose:
    def __init__(self, rig):
        self.rig = rig
        self.refresh()
        self.last = {}

    def refresh(self):
        """Перечитать кости (после добавления новых в режиме редактирования)."""
        bones = self.rig.data.bones
        self.order, todo = [], [b for b in bones if b.parent is None]
        while todo:
            b = todo.pop(0)
            self.order.append(b.name)
            todo += list(b.children)
        self.H = {b.name: b.head_local.copy() for b in bones}
        self.T = {b.name: b.tail_local.copy() for b in bones}
        self.REST = {b.name: b.matrix_local.to_quaternion() for b in bones}
        self.PARENT = {b.name: b.parent.name if b.parent else None for b in bones}

    def solve(self, spec, hips=(0, 0, 0)):
        """Ответ: W (поворот кости в мире относительно T-позы), P (положение головы кости), own (свой поворот)."""
        bones = self.rig.data.bones
        W, P, own, pending = {}, {}, {}, {}
        for b in self.order:
            p = self.PARENT[b]
            wp = W[p] if p else Quaternion()
            P[b] = P[p] + wp @ (self.H[b] - self.H[p]) if p else self.H[b] + Vector(hips)
            d = (self.T[b] - self.H[b]).normalized()
            o = Quaternion()
            for item in spec.get(b, []) + pending.pop(b, []):
                kind = item[0]
                if kind == "ROLL":
                    o = o @ Quaternion(d, math.radians(item[1]))
                elif kind == "ORIENT":
                    o = wp.inverted() @ item[1]
                elif kind == "AIM":
                    o = (o @ d).rotation_difference(wp.inverted() @ Vector(item[1]).normalized()) @ o
                elif kind == "IK":
                    target, pole = Vector(item[1]), Vector(item[2])
                    lower = next(c.name for c in bones[b].children)
                    hand = next(c.name for c in bones[lower].children if c.name.startswith("Hand"))
                    l1, l2 = (self.H[lower] - self.H[b]).length, (self.H[hand] - self.H[lower]).length
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

    def at(self, bone, rest_point, W, P):
        """Где окажется точка, жёстко привязанная к кости (в T-позе rest_point), в позе W, P."""
        return P[bone] + W[bone] @ (Vector(rest_point) - self.H[bone])

    def to_rest(self, bone, world_point, W, P):
        """Обратно: точка в позе W, P -> где она в T-позе, если привязана к кости."""
        return self.H[bone] + W[bone].inverted() @ (Vector(world_point) - P[bone])

    def key(self, frame, spec, hips=(0, 0, 0), extra=None):
        """Ключ позы на кадре; extra(frame) -- свои ключи (видимость стрелы, влияние ограничений...)."""
        _, _, own = self.solve(spec, hips)
        for pb in self.rig.pose.bones:
            pb.rotation_mode = "QUATERNION"
            quat = self.REST[pb.name].inverted() @ own[pb.name] @ self.REST[pb.name]
            prev = self.last.get(pb.name)
            if prev is not None and prev.dot(quat) < 0:      # без «переворотов» между ключами
                quat.negate()
            self.last[pb.name] = quat
            pb.rotation_quaternion = quat
            pb.keyframe_insert("rotation_quaternion", frame=frame)
        hb = self.rig.pose.bones["Hips"]
        hb.location = self.REST["Hips"].inverted() @ Vector(hips)
        hb.keyframe_insert("location", frame=frame)
        if extra:
            extra(frame)

    def action(self, name, keys, loop=False):
        """keys: [(кадр, поза, смещение таза, доп. аргументы для extra...)]; у цикла последний ключ = первый.
        Если в ключе после таза есть ещё значения, они передаются в self.extra(frame, *значения)."""
        self.rig.animation_data_create()
        act = bpy.data.actions.new(name)
        act.use_fake_user = True
        self.rig.animation_data.action = act
        self.last.clear()

        def put(frame, spec, hips=(0, 0, 0), *rest):
            self.key(frame, spec, hips, (lambda f: self.extra(f, *rest)) if hasattr(self, "extra") else None)
        for k in keys:
            put(*k)
        end = keys[-1][0]
        if loop:
            end += keys[1][0] - keys[0][0]
            put(end, *keys[0][1:])
        act.frame_range = (keys[0][0], end)
        return act


def merge(*specs):
    out = {}
    for s in specs:
        for b, rots in s.items():
            out[b] = out.get(b, []) + list(rots)
    return out


def fists(curl_l=60, curl_r=60):
    """Сжатые кулаки: пальцы загнуты к ладони (в T-позе ладони вниз)."""
    p = {}
    for s, side, curl in ((1, "L", curl_l), (-1, "R", curl_r)):
        for f in FINGERS:
            p[f"{f}.{side}"] = [("Y", s * curl)]
        p[f"Thumb2.{side}"] = [("X", -25)]
    return p


def wave(t):
    return math.sin(t * 2 * math.pi)


def walk_legs(t):
    """Шаркающий шаг: t = 0..1 -- полный цикл (два шага)."""
    s = wave(t)
    return {"UpperLeg.L": [("X", -24 * s)], "LowerLeg.L": [("X", 38 * max(0.0, -wave(t + 0.18)))],
            "UpperLeg.R": [("X", 24 * s)], "LowerLeg.R": [("X", 38 * max(0.0, wave(t + 0.18)))],
            "Foot.L": [("X", 10 * s)], "Foot.R": [("X", -10 * s)],
            "Hips": [("Z", 6 * s)], "Chest": [("Z", -8 * s)], "Head": [("Z", 5 * s)]}


# ---------------------------------------------------------------- PSX-текстуры
rng = np.random.default_rng(9)
BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) / 16.0 - 0.5


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


def ps1(rgb):
    """15-битный цвет с упорядоченным дизерингом и зерном, как в skeleton_psx.py."""
    n = rgb.shape[0]
    rgb = rgb * (0.9 + 0.2 * rng.random((n, n, 1)))
    dither = np.tile(BAYER, (n // 4, n // 4))[..., None] / 31
    return np.floor(np.clip(rgb + dither, 0, 1) * 31 + 0.5) / 31


def tex(color, spread=0.4, stripes=0, n=32):
    """Простая текстура материала: цвет + шум, по желанию поперечные полосы (обмотка, волокна)."""
    base = np.array(color) * (1 - spread / 2 + spread * noise(n, 1.0))[..., None]
    if stripes:
        base[::stripes] *= 0.6
    return ps1(base)


def shrink(path, size):
    """Картинка -> size x size усреднением (RGB, строки сверху вниз)."""
    im = bpy.data.images.load(path)
    w, h = im.size
    px = np.array(im.pixels[:]).reshape(h, w, 4)[::-1, :, :3]
    bpy.data.images.remove(im)
    ys = (np.arange(size + 1) * h / size).astype(int)
    xs = (np.arange(size + 1) * w / size).astype(int)
    return np.array([[px[ys[i]:ys[i + 1], xs[j]:xs[j + 1]].mean(axis=(0, 1)) for j in range(size)]
                     for i in range(size)])


def save_texture(tex_dir, name, rgb):
    h, w = rgb.shape[:2]
    im = bpy.data.images.new(name, w, h)
    px = np.ones((h, w, 4))
    px[:, :, :3] = np.clip(rgb, 0, 1)
    im.pixels = px[::-1].ravel()   # Blender хранит строки снизу вверх
    os.makedirs(tex_dir, exist_ok=True)
    im.filepath_raw = os.path.join(tex_dir, name + ".png")
    im.file_format = "PNG"
    im.save()
    return im


def material(tex_dir, name, rgb):
    """Матовый материал с PSX-текстурой (point-фильтр)."""
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.9
    t = nt.nodes.new("ShaderNodeTexImage")
    t.image, t.interpolation = save_texture(tex_dir, name, rgb), "Closest"
    nt.links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
    return m


def export_fbx(path, objects, rig):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects + [rig]:
        o.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"},
        apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        add_leaf_bones=False, mesh_smooth_type="FACE", path_mode="STRIP",
        bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)


def export_static_fbx(path, obj):
    """Один меш без рига и анимаций (снаряд): начало координат объекта -- начало координат модели."""
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"MESH"},
        apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        mesh_smooth_type="FACE", path_mode="STRIP", bake_anim=False)


def preview_stage(scene):
    """Ночной фон, три лампы, камера; возвращает shot(путь, угол, кадр, ...)."""
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
    return shot


def save_blend(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=path, compress=True)
    if os.path.exists(path + "1"):
        os.remove(path + "1")   # резервная копия Blender не нужна: модель пересобирается скриптом
