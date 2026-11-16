import sys, bpy
from mathutils import Matrix, Vector
fbx = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx, use_anim=False)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE"); m = next(o for o in bpy.data.objects if o.type == "MESH")
print("ARM_MW", [tuple(round(x, 3) for x in r) for r in arm.matrix_world])
for n in ["CC_Base_Hip", "CC_Base_L_Thigh", "CC_Base_L_Calf", "CC_Base_L_Foot", "CC_Base_R_Thigh", "CC_Base_R_Foot", "CC_Base_Head", "CC_Base_L_Upperarm", "CC_Base_L_Hand"]:
    b = arm.data.bones[n]
    print("BONE", n, tuple(round(v, 3) for v in arm.matrix_world @ b.head_local))
dg = bpy.context.evaluated_depsgraph_get(); me = m.evaluated_get(dg).to_mesh()
ws = [m.matrix_world @ v.co for v in me.vertices]
print("MESH_Z", round(min(p.z for p in ws), 3), round(max(p.z for p in ws), 3), "X", round(min(p.x for p in ws), 3), round(max(p.x for p in ws), 3))
