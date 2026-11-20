import bpy

for o in bpy.data.objects:
    if o.type == "ARMATURE":
        o.data.pose_position = "REST"
