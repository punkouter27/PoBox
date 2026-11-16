"""Soldat: an unrigged mesh (one object, A-pose, no armature, no weights) given a skeleton and skin in Blender, then made
the metric glTF the trainer and Unity read (2026-11-16).

    blender -b -P BLENDER/2026-11-16_soldat/rig.py -- <fbx> <out.glb> <out.blend> <height_m>

The joints are found from the mesh's own shape, not drawn by hand:
  * legs: the crotch is the highest height at which the legs are still apart (no vertex near x = 0); the hip joints a
    little above it, over each leg; knees and ankles the centres of thin slices through each leg; the ball of the foot a
    third of the way back along the sole;
  * spine, neck, head: at the usual fractions of standing height (Drillis and Contini), each centred in the torso's own
    slice there (arms left out);
  * arms: the shoulder joint just inside the outer edge of the deltoid; the fingertip the furthest vertex out; wrist and
    elbow along that line by the usual segment ratios, each pulled to the centre of the arm's flesh around it.
Bone names are Mixamo's (Hips, Spine..Spine2, Neck, Head, LeftArm, LeftForeArm, LeftHand, LeftUpLeg, LeftLeg, LeftFoot,
LeftToeBase, LeftHandMiddle1..3), which glb_to_rig.py maps. The skin is Blender's automatic (heat) weights; a vertex left
with none (a separate part heat could not reach) takes the nearest bone. Then the figure is scaled to <height_m>.
"""
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector

fbx, glb, blend, height = sys.argv[sys.argv.index("--") + 1:]
height = float(height)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=fbx, use_anim=False)
mesh = next(o for o in bpy.data.objects if o.type == "MESH")
mesh.name = "Soldat"
# Bake the object's transform into the vertices, so object space is world space.
bpy.context.view_layer.objects.active = mesh
mesh.select_set(True)
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
V = np.array([v.co[:] for v in mesh.data.vertices])
z0, z1 = V[:, 2].min(), V[:, 2].max()
H = z1 - z0
V[:, 2] -= z0
X, Y, Z = V[:, 0], V[:, 1], V[:, 2]
print("RAW height", round(H, 4), "x", round(X.min(), 3), round(X.max(), 3), "y", round(Y.min(), 3), round(Y.max(), 3))


def sl(z, half=0.006, mask=None):
    m = np.abs(Z - z) < half * H
    return m if mask is None else m & mask


def centre(m):
    return V[m].mean(0) if m.any() else None


# ---- legs
crotch = None
for z in np.linspace(0.30 * H, 0.60 * H, 300):
    s = sl(z)
    if s.any() and not (s & (np.abs(X) < 0.012 * H)).any():
        crotch = z
print("CROTCH", round(crotch / H, 3), "of height")
# Trouser legs that touch on the inside close the gap low on the thigh (0.34 of height on Soldat), where a person's
# crotch is about 0.47: the hip and knee heights are then set by proportion (hip joint 0.48, as Timmy's mesh-fitted rig has it; 0.51
# slimmed the trunk to 58% of the body to clear the thighs; knee 0.285), and only their sideways and front-back place is taken from the mesh.
hip_z = max(crotch + 0.04 * H, 0.48 * H)
J = {}
for side, sgn in (("Left", 1), ("Right", -1)):
    leg = sgn * X > 0
    below = centre(sl(min(crotch, 0.42 * H) - 0.02 * H, mask=leg))
    J[f"{side}UpLeg"] = Vector((below[0], below[1], hip_z))
    ank_z = 0.05 * H
    ankle = centre(sl(ank_z, mask=leg))
    J[f"{side}Foot"] = Vector((ankle[0], ankle[1], ank_z))
    knee_z = 0.285 * H
    knee = centre(sl(knee_z, mask=leg))
    J[f"{side}Leg"] = Vector((knee[0], knee[1], knee_z))
    sole = leg & (Z < 0.03 * H)
    yf, yb = Y[sole].min(), Y[sole].max()                 # front (-y) and heel
    J[f"{side}ToeBase"] = Vector((ankle[0], yf + 0.30 * (yb - yf), 0.02 * H))
    J[f"{side}Toe_End"] = Vector((ankle[0], yf, 0.02 * H))

# ---- trunk and head (the torso's slice: within the shoulders' width, arms left out)
torso = np.abs(X) < 0.10 * H
def trunk(frac):
    c = centre(sl(frac * H, half=0.01, mask=torso))
    return Vector((0.0, c[1], frac * H))
# The pelvis joint a little above the hip joints and the spine's root above that, as every other boxer's rig has them
# (0.014-0.016 and 0.06-0.08 of height): level with the hips, the pelvis's shape sat inside both thighs.
pel_z = hip_z + 0.015 * H
J["Hips"] = Vector((0.0, centre(sl(pel_z, half=0.01, mask=torso))[1], pel_z))
J["Spine"] = trunk(pel_z / H + 0.075)
J["Spine1"] = trunk(0.66)
J["Spine2"] = trunk(0.74)
J["Neck"] = trunk(0.835)
J["Head"] = trunk(0.875)
J["HeadTop_End"] = Vector((0.0, J["Head"].y, H))

# ---- arms
for side, sgn in (("Left", 1), ("Right", -1)):
    s80 = sl(0.80 * H, half=0.01)
    outer = (sgn * X[s80]).max()
    sh = Vector((sgn * (outer - 0.035 * H), centre(sl(0.80 * H, half=0.01, mask=sgn * X > 0.05 * H))[1], 0.805 * H))
    tip_i = np.argmax(sgn * X)
    tip = Vector(V[tip_i])
    d = (tip - sh)
    L = d.length
    d.normalize()
    wrist = tip - d * (0.108 * H)
    upper, fore = 0.186, 0.146
    elbow = sh + d * ((wrist - sh).length * upper / (upper + fore))
    def flesh(p, r=0.035):
        m = np.linalg.norm(V - np.array(p[:]), axis=1) < r * H
        c = V[m].mean(0) if m.any() else p
        return Vector(c)
    elbow, wrist = flesh(elbow), flesh(wrist, 0.03)
    J[f"{side}Shoulder"] = Vector((sgn * 0.03 * H, J["Spine2"].y, 0.80 * H))
    J[f"{side}Arm"] = sh
    J[f"{side}ForeArm"] = elbow
    J[f"{side}Hand"] = wrist
    dh = (tip - wrist)
    J[f"{side}HandMiddle1"] = wrist + dh * 0.45
    J[f"{side}HandMiddle2"] = wrist + dh * 0.65
    J[f"{side}HandMiddle3"] = wrist + dh * 0.82
    J[f"{side}HandMiddle_End"] = tip
for k in sorted(J):
    print("JOINT", k, tuple(round(v / H, 3) for v in J[k]))

# ---- the armature: (bone, head joint, tail joint, parent)
CHAIN = [("Hips", "Hips", "Spine", None), ("Spine", "Spine", "Spine1", "Hips"), ("Spine1", "Spine1", "Spine2", "Spine"),
         ("Spine2", "Spine2", "Neck", "Spine1"), ("Neck", "Neck", "Head", "Spine2"), ("Head", "Head", "HeadTop_End", "Neck")]
for s in ("Left", "Right"):
    CHAIN += [(f"{s}Shoulder", f"{s}Shoulder", f"{s}Arm", "Spine2"), (f"{s}Arm", f"{s}Arm", f"{s}ForeArm", f"{s}Shoulder"),
              (f"{s}ForeArm", f"{s}ForeArm", f"{s}Hand", f"{s}Arm"), (f"{s}Hand", f"{s}Hand", f"{s}HandMiddle1", f"{s}ForeArm"),
              (f"{s}HandMiddle1", f"{s}HandMiddle1", f"{s}HandMiddle2", f"{s}Hand"),
              (f"{s}HandMiddle2", f"{s}HandMiddle2", f"{s}HandMiddle3", f"{s}HandMiddle1"),
              (f"{s}HandMiddle3", f"{s}HandMiddle3", f"{s}HandMiddle_End", f"{s}HandMiddle2"),
              (f"{s}UpLeg", f"{s}UpLeg", f"{s}Leg", "Hips"), (f"{s}Leg", f"{s}Leg", f"{s}Foot", f"{s}UpLeg"),
              (f"{s}Foot", f"{s}Foot", f"{s}ToeBase", f"{s}Leg"), (f"{s}ToeBase", f"{s}ToeBase", f"{s}Toe_End", f"{s}Foot")]
arm_data = bpy.data.armatures.new("SoldatRig")
arm = bpy.data.objects.new("Soldat_Armature", arm_data)
bpy.context.scene.collection.objects.link(arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
eb = {}
for name, h, t, parent in CHAIN:
    b = arm_data.edit_bones.new(name)
    b.head, b.tail = J[h], J[t]
    if parent:
        b.parent = eb[parent]
        b.use_connect = False
    eb[name] = b
bpy.ops.object.mode_set(mode="OBJECT")

# ---- skin: heat weights, then the nearest bone for whatever heat left bare
bpy.ops.object.select_all(action="DESELECT")
mesh.select_set(True)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.parent_set(type="ARMATURE_AUTO")
bare = [v.index for v in mesh.data.vertices if not any(g.weight > 1e-4 for g in v.groups)]
print("BARE after heat", len(bare), "of", len(mesh.data.vertices))
if bare:
    segs = [(b.name, Vector(b.head_local), Vector(b.tail_local)) for b in arm_data.bones if b.use_deform]
    def nearest(p):
        best, bd = None, 1e9
        for n, a, c in segs:
            ab = c - a
            t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-12)))
            d = (a + ab * t - p).length
            if d < bd:
                best, bd = n, d
        return best
    groups = {g.name: g for g in mesh.vertex_groups}
    for i in bare:
        n = nearest(mesh.data.vertices[i].co)
        g = groups.get(n) or mesh.vertex_groups.new(name=n)
        groups[n] = g
        g.add([i], 1.0, "REPLACE")

# ---- to height, feet on the floor, centred
k = height / H
arm.scale = (k, k, k)
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
me = mesh.evaluated_get(dg).to_mesh()
P = np.array([(mesh.matrix_world @ v.co)[:] for v in me.vertices])
arm.location = (-(P[:, 0].min() + P[:, 0].max()) / 2, -(P[:, 1].min() + P[:, 1].max()) / 2, -P[:, 2].min())
bpy.context.view_layer.update()
P = np.array([(mesh.matrix_world @ v.co)[:] for v in mesh.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh().vertices])
print("FINAL_BOUNDS", P.min(0).round(3), P.max(0).round(3))
bpy.ops.wm.save_as_mainfile(filepath=blend)
bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", export_skins=True, export_animations=False, export_yup=True)
print("WROTE", glb)
