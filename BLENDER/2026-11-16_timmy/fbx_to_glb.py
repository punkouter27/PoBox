"""Timmy's AccuRig FBX -> the glTF the trainer and Unity read, and a preview render. Run headless:

    blender -b -P BLENDER/2026-11-16_timmy/fbx_to_glb.py -- <fbx> <out.glb> <preview.png> <out.blend>

Prints the figure's height (feet to crown), the armature's bones and the meshes, so the rig can be checked before
glb_to_rig.py reads it.
"""
import sys
import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
fbx, glb, png, blend = argv

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx, automatic_bone_orientation=False)

arms = [o for o in bpy.data.objects if o.type == "ARMATURE"]
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
print("ARMATURES", [a.name for a in arms])
print("MESHES", [(m.name, len(m.data.vertices)) for m in meshes])
for a in arms:
    print("BONES", len(a.data.bones), [b.name for b in a.data.bones][:80])

lo = Vector((1e9, 1e9, 1e9)); hi = Vector((-1e9, -1e9, -1e9))
for m in meshes:
    for c in m.bound_box:
        w = m.matrix_world @ Vector(c)
        lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
print("BOUNDS_MIN", tuple(round(v, 3) for v in lo), "BOUNDS_MAX", tuple(round(v, 3) for v in hi))
print("HEIGHT_Z", round(hi.z - lo.z, 3), "WIDTH_X", round(hi.x - lo.x, 3), "DEPTH_Y", round(hi.y - lo.y, 3))
print("SCALE", [tuple(round(s, 4) for s in o.scale) for o in arms + meshes][:4])

bpy.ops.wm.save_as_mainfile(filepath=blend)
bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", export_skins=True, export_animations=False,
                          export_apply=False, export_yup=True)
print("WROTE", glb)

# A preview: front, three-quarter, lit, orthographic, framed on the bounds.
scene = bpy.context.scene
scene.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_WORKBENCH"
scene.render.resolution_x, scene.render.resolution_y = 720, 1080
centre = (lo + hi) / 2
size = max(hi.z - lo.z, hi.x - lo.x) * 1.15
cam_data = bpy.data.cameras.new("Cam"); cam_data.type = "ORTHO"; cam_data.ortho_scale = size
cam = bpy.data.objects.new("Cam", cam_data); scene.collection.objects.link(cam)
cam.location = centre + Vector((size * 0.6, -size * 1.6, 0.0))
cam.rotation_euler = (1.5708, 0, 0.36)
scene.camera = cam
light = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN")); light.rotation_euler = (0.8, 0.2, 0.6)
light.data.energy = 3.0
scene.collection.objects.link(light)
if scene.world is None:
    scene.world = bpy.data.worlds.new("W")
scene.render.filepath = png
bpy.ops.render.render(write_still=True)
print("PREVIEW", png)
