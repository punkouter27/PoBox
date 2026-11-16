import sys, bpy
from mathutils import Vector
fbx = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx, use_anim=False)
for o in bpy.data.objects:
    extra = ""
    if o.type == "MESH":
        extra = f"verts={len(o.data.vertices)} groups={len(o.vertex_groups)} shapekeys={bool(o.data.shape_keys)} mods={[m.type for m in o.modifiers]} mats={len(o.data.materials)}"
    if o.type == "ARMATURE":
        extra = f"bones={len(o.data.bones)} {[b.name for b in o.data.bones][:40]}"
    print("OBJ", o.name, o.type, "parent", o.parent.name if o.parent else "-", tuple(round(v, 3) for v in o.rotation_euler), tuple(round(v, 4) for v in o.scale), extra)
dg = bpy.context.evaluated_depsgraph_get()
lo, hi = Vector((1e9,) * 3), Vector((-1e9,) * 3)
for o in bpy.data.objects:
    if o.type == "MESH":
        me = o.evaluated_get(dg).to_mesh()
        for v in me.vertices:
            p = o.matrix_world @ v.co
            lo, hi = Vector(map(min, lo, p)), Vector(map(max, hi, p))
print("BOUNDS", tuple(round(x, 3) for x in lo), tuple(round(x, 3) for x in hi), "SIZE", tuple(round(x, 3) for x in hi - lo))
print("ACTIONS", len(bpy.data.actions))
bpy.ops.wm.save_as_mainfile(filepath=sys.argv[sys.argv.index("--") + 2])
