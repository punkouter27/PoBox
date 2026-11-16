import bpy, math
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
for name, axis, deg in [("LeftArm", "z", 40), ("RightForeArm", "z", 70), ("LeftLeg", "x", 70), ("RightUpLeg", "x", -40), ("Spine1", "y", 20), ("Head", "x", 20)]:
    pb = arm.pose.bones[name]; pb.rotation_mode = "XYZ"; setattr(pb.rotation_euler, axis, math.radians(deg))
bpy.context.view_layer.update()
