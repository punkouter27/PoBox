"""Which FBX import settings give an armature whose rest pose is the mesh's bind pose? For each try: the largest
distance any vertex moves between the rest display (undeformed) and the pose with every bone at identity.

    blender -b -P probe_import.py -- <fbx>
"""

import sys
import bpy
from mathutils import Matrix

fbx = sys.argv[sys.argv.index("--") + 1]
tries = [
    dict(automatic_bone_orientation=False),
    dict(automatic_bone_orientation=True),
    dict(automatic_bone_orientation=False, use_prepost_rot=False),
    dict(automatic_bone_orientation=True, force_connect_children=False, ignore_leaf_bones=True),
    dict(automatic_bone_orientation=False, use_anim=False),
]


def coords(m):
    dg = bpy.context.evaluated_depsgraph_get()
    me = m.evaluated_get(dg).to_mesh()
    return [m.matrix_world @ v.co for v in me.vertices]


for t in tries:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    try:
        bpy.ops.import_scene.fbx(filepath=fbx, **t)
    except Exception as e:
        print("TRY", t, "import failed", e)
        continue
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    m = next(o for o in bpy.data.objects if o.type == "MESH")
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    arm.data.pose_position = "REST"
    bpy.context.view_layer.update()
    rest = coords(m)
    arm.data.pose_position = "POSE"
    bpy.context.view_layer.update()
    pose = coords(m)
    worst = max((a - b).length for a, b in zip(rest, pose))
    print("TRY", t, "worst vertex move", round(worst, 4), "actions", len(bpy.data.actions))
