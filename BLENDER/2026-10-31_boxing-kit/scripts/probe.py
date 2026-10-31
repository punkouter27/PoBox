import bpy
from mathutils import Vector
M="C:/Users/punko/Downloads/PoBox/Assets/Models/"
for n in ["Matt","Zombie","Nick","Trump","Grandma","Grandpa","Kim","Sokol"]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=M+n+".glb")
    print("=====",n, [o.name for o in bpy.data.objects])
    arm=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]
    print(" pose_position",arm.data.pose_position, "anim", arm.animation_data.action if arm.animation_data else None)
    for b in arm.data.bones:
        nm=b.name.lower()
        if any(k in nm for k in ["hip","pelvis","upleg","leg","thigh","calf","shin","foot","toe"]) and "twist" not in nm and "share" not in nm and "1" not in nm[-1:]:
            h=arm.matrix_world@b.head_local; t=arm.matrix_world@b.tail_local
            pb=arm.pose.bones[b.name]; ph=arm.matrix_world@pb.head
            print("  %-26s head %s tail %s posehead %s"%(b.name,tuple(round(x,3) for x in h),tuple(round(x,3) for x in t),tuple(round(x,3) for x in ph)))
