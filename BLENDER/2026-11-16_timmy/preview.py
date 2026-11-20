"""Front and side previews of Timmy.blend, and the world-space bounds of the evaluated mesh.

blender -b BLENDER/2026-11-16_timmy/Timmy.blend -P BLENDER/2026-11-16_timmy/preview.py -- <out_dir>
"""

import os
import sys
import bpy
from mathutils import Vector

out = sys.argv[sys.argv.index("--") + 1]
scene = bpy.context.scene
dg = bpy.context.evaluated_depsgraph_get()
lo = Vector((1e9,) * 3)
hi = Vector((-1e9,) * 3)
for o in scene.objects:
    if o.type != "MESH":
        continue
    me = o.evaluated_get(dg).to_mesh()
    for v in me.vertices:
        w = o.matrix_world @ v.co
        lo = Vector(map(min, lo, w))
        hi = Vector(map(max, hi, w))
print(
    "EVAL_BOUNDS",
    tuple(round(x, 3) for x in lo),
    tuple(round(x, 3) for x in hi),
    "SIZE",
    tuple(round(x, 3) for x in hi - lo),
)
for o in scene.objects:
    print(
        "OBJ",
        o.name,
        o.type,
        tuple(round(x, 3) for x in o.rotation_euler),
        tuple(round(x, 4) for x in o.scale),
        o.parent.name if o.parent else "-",
    )

scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "TEXTURE"
scene.render.resolution_x, scene.render.resolution_y = 600, 900
centre = (lo + hi) / 2
size = max(hi - lo) * 1.1
cam_data = bpy.data.cameras.new("Cam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = size
cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
for name, loc, rot in [
    ("front", (0, -10, 0), (1.5708, 0, 0)),
    ("side", (10, 0, 0), (1.5708, 0, 1.5708)),
    ("top", (0, 0, 10), (0, 0, 0)),
]:
    cam.location = centre + Vector(loc)
    cam.rotation_euler = rot
    scene.render.filepath = os.path.join(out, f"preview_{name}.png")
    bpy.ops.render.render(write_still=True)
    print("PREVIEW", scene.render.filepath)
