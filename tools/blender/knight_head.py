"""Треугольники головы рыцаря (кость Head): мировые координаты Blender и UV -> head_tris.json для helmet_damage.py."""
import bpy, json, math, os, sys
from mathutils import Vector
argv = sys.argv[sys.argv.index("--")+1:]
out = argv[0]
bpy.ops.wm.read_factory_settings(use_empty=True)
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
bpy.ops.import_scene.fbx(filepath=os.path.join(ROOT, "Assets", "Game", "Assets", "Knight", "knight.fbx"))
o = bpy.data.objects["knight"]; me = o.data
gi = o.vertex_groups["Head"].index
def dom(v): return max(v.groups, key=lambda g: g.weight).group if v.groups else -1
head = {v.index for v in me.vertices if dom(v) == gi}
me.calc_loop_triangles()
uv = me.uv_layers.active.data
W = o.matrix_world
tris = []
for t in me.loop_triangles:
    if all(i in head for i in t.vertices):
        tris.append({"p": [list(W @ me.vertices[i].co) for i in t.vertices], "uv": [list(uv[l].uv) for l in t.loops]})
hv = [W @ me.vertices[i].co for i in head]
print("HEADV", len(head), "tris", len(tris), [round(min(c[k] for c in hv),3) for k in range(3)], [round(max(c[k] for c in hv),3) for k in range(3)])
os.makedirs(out, exist_ok=True)
json.dump(tris, open(out + "/head_tris.json", "w"))
