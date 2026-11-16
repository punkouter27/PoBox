"""Timmy's AccuRig FBX -> a clean, standing, metric glTF for the trainer and Unity (2026-11-16).

    blender -b -P BLENDER/2026-11-16_timmy/stage.py -- <fbx> <out.glb> <out.blend> <height_m>

What the FBX needed:
  * four animation clips that posed the figure into spikes: imported without them (use_anim=False); with every
    pose bone at identity the mesh is exactly its bind shape (probe_import.py: worst vertex move 0.0);
  * the legs stood about 27 degrees apart: each thigh is turned, about the body's own forward axis, so the ankle
    hangs under the hip; that is baked into the mesh and made the rest pose, so the body's zero stance is a stance.
    The facial shape keys go first (Blender cannot bake a pose into a mesh with shape keys; the fight pulls no faces);
  * imported lying down (up along -Y) and not in metres: the armature object is turned so up is +Z and the face looks
    down -Y (the rig pipeline's frame) and scaled to <height_m>, feet on z = 0, centred. Object transforms are left
    for the glTF exporter to write (applying them on a skinned armature moved the mesh twice).
"""
import math
import sys

import bpy
from mathutils import Matrix, Vector

fbx, glb, blend, height = sys.argv[sys.argv.index("--") + 1:]
height = float(height)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx, use_anim=False)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
meshes = [o for o in bpy.data.objects if o.type == "MESH"]


def pose_head(name):
    return arm.matrix_world @ arm.pose.bones[name].head


def bounds():
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    lo, hi = Vector((1e9,) * 3), Vector((-1e9,) * 3)
    for m in meshes:
        me = m.evaluated_get(dg).to_mesh()
        for v in me.vertices:
            p = m.matrix_world @ v.co
            lo, hi = Vector(map(min, lo, p)), Vector(map(max, hi, p))
    return lo, hi


# The FBX's mesh and skeleton do not coincide in Blender: the mesh stands up (+Z) while the bones lie along -Y and
# sit lower (a transform on the mesh that the importer applies to it and not to the bones), so any pose tore the mesh
# into spikes. The mesh is moved onto the bones by the skin weights themselves: each bone's centre should be the
# weighted centre of the vertices it drives. A similarity transform (turn, scale, shift; Umeyama) fitted between the
# two sets of points, and the residual reported.
import numpy as np
BONES = ["CC_Base_Head", "CC_Base_Spine02", "CC_Base_Pelvis", "CC_Base_L_Thigh", "CC_Base_R_Thigh", "CC_Base_L_Calf",
         "CC_Base_R_Calf", "CC_Base_L_Foot", "CC_Base_R_Foot", "CC_Base_L_Upperarm", "CC_Base_R_Upperarm",
         "CC_Base_L_Forearm", "CC_Base_R_Forearm", "CC_Base_L_Hand", "CC_Base_R_Hand"]
src, dst = [], []
for name in BONES:
    b = arm.data.bones.get(name)
    if b is None:
        continue
    acc, wsum = Vector((0, 0, 0)), 0.0
    for m in meshes:
        g = m.vertex_groups.get(name)
        if g is None:
            continue
        for v in m.data.vertices:
            for ge in v.groups:
                if ge.group == g.index and ge.weight > 0.5:
                    acc += (m.matrix_world @ v.co) * ge.weight
                    wsum += ge.weight
    if wsum <= 0:
        continue
    src.append(acc / wsum)
    dst.append(arm.matrix_world @ ((b.head_local + b.tail_local) / 2))
A, Bm = np.array([list(p) for p in src]), np.array([list(p) for p in dst])
ma, mb = A.mean(0), Bm.mean(0)
U, S, Vt = np.linalg.svd((Bm - mb).T @ (A - ma) / len(A))
D = np.eye(3); D[2, 2] = np.sign(np.linalg.det(U @ Vt))
Rn = U @ D @ Vt
scale = float(np.trace(np.diag(S) @ D) / ((A - ma) ** 2).sum(1).mean())
tn = mb - scale * Rn @ ma
fitted = (scale * (Rn @ A.T)).T + tn
print("MESH_ON_BONES bones", len(A), "residual before", round(float(np.linalg.norm(A - Bm, axis=1).mean()), 4),
      "after", round(float(np.linalg.norm(fitted - Bm, axis=1).mean()), 4), "scale", round(scale, 4))
T = Matrix.Translation(Vector(tn)) @ Matrix(Rn.tolist()).to_4x4() @ Matrix.Scale(scale, 4)
for m in meshes:
    m.matrix_world = T @ m.matrix_world
bpy.context.view_layer.update()

for m in meshes:
    if m.data.shape_keys:
        print("removing", len(m.data.shape_keys.key_blocks), "shape keys from", m.name)
        bpy.context.view_layer.objects.active = m
        m.shape_key_clear()

bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")
for pb in arm.pose.bones:
    pb.matrix_basis = Matrix.Identity(4)
bpy.context.view_layer.update()

# The body's own axes, in world space: up (feet to head) and its left (right hip to left hip).
up = (pose_head("CC_Base_Head") - (pose_head("CC_Base_L_Foot") + pose_head("CC_Base_R_Foot")) / 2).normalized()
left = pose_head("CC_Base_L_Thigh") - pose_head("CC_Base_R_Thigh")
left = (left - left.dot(up) * up).normalized()
for side in ("L", "R"):
    thigh = arm.pose.bones[f"CC_Base_{side}_Thigh"]
    hip, ankle = pose_head(thigh.name), pose_head(f"CC_Base_{side}_Foot")
    d = ankle - hip
    target = d - d.dot(left) * left                    # the same leg with its sideways lean taken out
    print(f"{side} leg splay {math.degrees(d.angle(target)):.1f} deg")
    q = d.rotation_difference(target).to_matrix().to_4x4()
    rot = Matrix.Translation(hip) @ q @ Matrix.Translation(-hip)
    w = arm.matrix_world
    thigh.matrix = w.inverted() @ rot @ w @ thigh.matrix
    bpy.context.view_layer.update()
    d2 = pose_head(f"CC_Base_{side}_Foot") - pose_head(thigh.name)
    print(f"{side} sideways lean left {d2.dot(left):.4f} m")
bpy.ops.object.mode_set(mode="OBJECT")

lo0, hi0 = bounds()
print("POSED_BOUNDS", tuple(round(x, 3) for x in lo0), tuple(round(x, 3) for x in hi0))

# Bake the pose into the meshes and make it the rest pose.
for m in meshes:
    mod = next((md for md in m.modifiers if md.type == "ARMATURE"), None)
    if mod is None:
        continue
    bpy.context.view_layer.objects.active = m
    bpy.ops.object.modifier_copy(modifier=mod.name)
    bpy.ops.object.modifier_apply(modifier=mod.name)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="POSE")
bpy.ops.pose.select_all(action="SELECT")
bpy.ops.pose.armature_apply(selected=False)
bpy.ops.object.mode_set(mode="OBJECT")
lo1, hi1 = bounds()
print("BAKED_BOUNDS", tuple(round(x, 3) for x in lo1), tuple(round(x, 3) for x in hi1))

# Upright, facing -Y, to height, feet on the floor, centred: all on the armature object.
q = up.rotation_difference(Vector((0, 0, 1))).to_matrix().to_4x4()
arm.matrix_world = q @ arm.matrix_world
bpy.context.view_layer.update()
eyes = (pose_head("CC_Base_L_Eye") + pose_head("CC_Base_R_Eye")) / 2 - pose_head("CC_Base_Head")
if eyes.y > 0:
    arm.matrix_world = Matrix.Rotation(math.pi, 4, "Z") @ arm.matrix_world
lo, hi = bounds()
k = height / (hi.z - lo.z)
arm.matrix_world = Matrix.Scale(k, 4) @ arm.matrix_world
lo, hi = bounds()
arm.matrix_world = Matrix.Translation((-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z)) @ arm.matrix_world
lo, hi = bounds()
print("FINAL_BOUNDS", tuple(round(x, 3) for x in lo), tuple(round(x, 3) for x in hi))
for n in ("CC_Base_L_Thigh", "CC_Base_L_Foot", "CC_Base_R_Thigh", "CC_Base_R_Foot", "CC_Base_Head", "CC_Base_L_Eye", "CC_Base_L_Hand", "CC_Base_R_Hand"):
    print("BONE", n, tuple(round(v, 3) for v in pose_head(n)))
for m in meshes:
    m.name = "Timmy"
arm.name = "Timmy_Armature"
bpy.ops.wm.save_as_mainfile(filepath=blend)
bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", export_skins=True, export_animations=False, export_yup=True)
print("WROTE", glb)
