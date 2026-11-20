import bpy
import math

arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
for name, axis, deg in [
    ("CC_Base_L_Upperarm", "X", 50),
    ("CC_Base_R_Forearm", "Y", -70),
    ("CC_Base_L_Calf", "X", 60),
    ("CC_Base_Spine02", "Z", 20),
]:
    pb = arm.pose.bones[name]
    pb.rotation_mode = "XYZ"
    setattr(pb.rotation_euler, axis.lower(), math.radians(deg))
bpy.context.view_layer.update()
