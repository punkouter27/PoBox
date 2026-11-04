# Re-imports each SK_*.glb into an empty scene, checks rig/bones/triangles/Card parent, works out the axis
# convention as glTFast will see it in Unity, poses the figure (right arm overhead, seated legs) and renders
# rest + pose previews to SCREENSHOTS/2026-11-03-figures/.
# Run:  blender.exe --background --python validate_figures.py
import bpy, json, struct, math, os
from mathutils import Vector, Quaternion, Matrix

ROOT = r"C:\Users\punko\Downloads\PoBox"
GLB_DIR = os.path.join(ROOT, "Assets", "Art", "Exports", "GLB")
SHOT = os.path.join(ROOT, "SCREENSHOTS", "2026-11-03-figures")
os.makedirs(SHOT, exist_ok=True)
BONES = ["Hips", "Spine", "Chest", "Neck", "Head", "UpperArm.L", "ForeArm.L", "Hand.L", "UpperArm.R", "ForeArm.R",
         "Hand.R", "Thigh.L", "Shin.L", "Foot.L", "Thigh.R", "Shin.R", "Foot.R"]


def gltf_json(path):
    with open(path, "rb") as f:
        data = f.read()
    n = struct.unpack_from("<I", data, 12)[0]
    return json.loads(data[20:20 + n])


def unity_axes(js, report):
    """glTFast: position (x,y,z)->(-x,y,z), rotation (x,y,z,w)->(x,-y,-z,w). Chain to Unity world rest."""
    nodes = js["nodes"]
    parent = {}
    for i, nd in enumerate(nodes):
        for c in nd.get("children", []): parent[c] = i

    def local(i):
        nd = nodes[i]
        t = nd.get("translation", [0, 0, 0]); r = nd.get("rotation", [0, 0, 0, 1])
        return Vector((-t[0], t[1], t[2])), Quaternion((r[3], r[0], -r[1], -r[2]))

    def world(i):
        p, q = local(i)
        if i in parent:
            pp, pq = world(parent[i])
            return pp + pq @ p, pq @ q
        return p, q
    byname = {nd.get("name"): i for i, nd in enumerate(nodes)}
    out = {}
    for b in ("Hips", "UpperArm.R", "UpperArm.L", "Thigh.R", "Shin.R", "Head", "Hand.R"):
        p, q = world(byname[b])
        out[b] = (p, q)
        report.append("  Unity rest %-10s pos=(%.3f,%.3f,%.3f)  localX->%s localY->%s localZ->%s" % (
            b, p.x, p.y, p.z, fmt(q @ Vector((1, 0, 0))), fmt(q @ Vector((0, 1, 0))), fmt(q @ Vector((0, 0, 1)))))
    # which local rotation raises the right arm overhead / bends the thigh forward
    for b, goal in (("UpperArm.R", Vector((0, 1, 0))), ("UpperArm.L", Vector((0, 1, 0))), ("Thigh.R", Vector((0, 0, 1))),
                    ("Shin.R", Vector((0, 0, -1)))):
        p, q = out.get(b) or world(byname[b])
        best = []
        for ax, v in (("X", (1, 0, 0)), ("Y", (0, 1, 0)), ("Z", (0, 0, 1))):
            for ang in (90, -90, 135, -135):
                qq = q @ Quaternion(Vector(v), math.radians(ang))   # same as Unity rest * Quaternion.AngleAxis(ang, axis)
                d = (qq @ Vector((0, 1, 0))).normalized()
                best.append(((d - goal).length, ax, ang, d))
        best.sort(key=lambda x: x[0])
        report.append("  %s: best local rotation toward %s = AngleAxis(%+d, %s)  -> dir %s" % (
            b, fmt(goal), best[0][2], best[0][1], fmt(best[0][3])))


def fmt(v): return "(%+.2f,%+.2f,%+.2f)" % tuple(v)


def setup_render(scene):
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'MATERIAL'
    scene.display.shading.show_cavity = True
    scene.display.shading.show_object_outline = False
    scene.render.resolution_x = 600; scene.render.resolution_y = 800; scene.render.resolution_percentage = 100
    scene.render.film_transparent = False
    if scene.world is None: scene.world = bpy.data.worlds.new("W")
    scene.world.color = (0.32, 0.34, 0.38)
    cam_d = bpy.data.cameras.new("Cam"); cam_d.type = 'ORTHO'; cam_d.ortho_scale = 2.75
    cam = bpy.data.objects.new("Cam", cam_d); scene.collection.objects.link(cam)
    tgt = Vector((0, 0, 1.22))
    cam.location = tgt + Vector((-2.2, -3.8, 0.6))       # figure's right-front three-quarter
    cam.rotation_euler = (tgt - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam
    # ground plane
    bpy.ops.mesh.primitive_plane_add(size=3, location=(0, 0, 0))
    g = bpy.context.active_object
    gm = bpy.data.materials.new("Ground"); gm.diffuse_color = (0.55, 0.55, 0.58, 1); g.data.materials.append(gm)


def world_rot_to_local(pb, R):
    """pose rotation that applies armature-space rotation R to this bone (parents unposed in that axis)."""
    M = pb.bone.matrix_local.to_3x3()
    return (M.inverted() @ R @ M).to_quaternion()


def main():
    report = []
    for kind in ("Referee", "Cornerman", "RingCardWalker"):
        name = "SK_" + kind
        path = os.path.join(GLB_DIR, name + ".glb")
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.gltf(filepath=path)
        scene = bpy.context.scene
        arms = [o for o in scene.objects if o.type == 'ARMATURE']
        meshes = [o for o in scene.objects if o.type == 'MESH' and not o.name.startswith("Icosphere")]  # importer bone-shape
        report.append("== %s" % name)
        report.append("  armatures=%d (%s)" % (len(arms), [a.name for a in arms]))
        arm = arms[0]
        names = [b.name for b in arm.data.bones]
        missing = [b for b in BONES if b not in names]
        roots = [b.name for b in arm.data.bones if b.parent is None]
        report.append("  bones=%d missing=%s extra=%s roots=%s" % (len(names), missing, [b for b in names if b not in BONES], roots))
        tris = 0
        for m in meshes:
            t = sum(len(p.vertices) - 2 for p in m.data.polygons); tris += t
            unweighted = 0
            if m.vertex_groups:
                for v in m.data.vertices:
                    if sum(g.weight for g in v.groups) < 0.99: unweighted += 1
            report.append("  mesh %-6s tris=%d verts=%d mats=%s parent=%s parent_type=%s parent_bone=%s skinned=%s unweighted=%d" % (
                m.name, t, len(m.data.vertices), [s.material.name for s in m.material_slots], m.parent and m.parent.name,
                m.parent_type, m.parent_bone, any(md.type == 'ARMATURE' for md in m.modifiers), unweighted if m.vertex_groups else -1))
        report.append("  total tris=%d" % tris)
        allv = [m.matrix_world @ v.co for m in meshes if m.vertex_groups for v in m.data.vertices]
        report.append("  bounds x[%.3f,%.3f] y[%.3f,%.3f] z[%.3f,%.3f]" % (
            min(v.x for v in allv), max(v.x for v in allv), min(v.y for v in allv), max(v.y for v in allv),
            min(v.z for v in allv), max(v.z for v in allv)))
        js = gltf_json(path)
        nodes = js["nodes"]
        card_i = [i for i, n in enumerate(nodes) if n.get("name") == "Card"]
        if card_i:
            par = [n.get("name") for n in nodes if card_i[0] in n.get("children", [])]
            report.append("  glTF Card node parent=%s mesh=%s" % (par, js["meshes"][nodes[card_i[0]]["mesh"]]["name"]))
            mats = [m["name"] for m in js["materials"]]
            report.append("  glTF materials=%s" % mats)
        unity_axes(js, report)

        setup_render(scene)
        for o in meshes:   # flat look
            for p in o.data.polygons: p.use_smooth = False
        bpy.context.view_layer.objects.active = arm
        rest = os.path.join(SHOT, name + "_rest.png")
        scene.render.filepath = rest; bpy.ops.render.render(write_still=True)
        # test pose
        bpy.ops.object.mode_set(mode='POSE')
        for pb in arm.pose.bones: pb.rotation_mode = 'QUATERNION'
        pb = arm.pose.bones["UpperArm.R"]
        d0 = (pb.bone.tail_local - pb.bone.head_local).normalized()
        up = Vector((0, 0, 1))
        R = d0.rotation_difference(up).to_matrix()
        pb.rotation_quaternion = world_rot_to_local(pb, R)
        for sd in ("L", "R"):
            arm.pose.bones["Thigh." + sd].rotation_quaternion = world_rot_to_local(arm.pose.bones["Thigh." + sd], Matrix.Rotation(math.radians(-90), 3, 'X'))
            arm.pose.bones["Shin." + sd].rotation_quaternion = world_rot_to_local(arm.pose.bones["Shin." + sd], Matrix.Rotation(math.radians(90), 3, 'X'))
        # report the local-axis quaternions Blender used (bone-local frames)
        for b in ("UpperArm.R", "Thigh.R", "Shin.R"):
            q = arm.pose.bones[b].rotation_quaternion
            ax, an = q.to_axis_angle()
            report.append("  Blender pose %s = %.0f deg about local %s" % (b, math.degrees(an), fmt(ax)))
        bpy.ops.object.mode_set(mode='OBJECT')
        arm.location.z = 0.0
        bpy.context.view_layer.update()
        pose = os.path.join(SHOT, name + "_pose.png")
        scene.render.filepath = pose; bpy.ops.render.render(write_still=True)
        if card_i:
            c = [o for o in scene.objects if o.name.startswith("Card")][0]
            report.append("  posed Card world centre=%s (head top ~%.2f)" % (fmt(c.matrix_world @ Vector(c.bound_box[0]).lerp(Vector(c.bound_box[6]), 0.5)), max(v.z for v in allv)))
            # "card up" pose: standing, right upper arm out to horizontal, forearm bent straight up -> card beside the head
            for pb in arm.pose.bones: pb.rotation_quaternion = (1, 0, 0, 0)
            ua, fa = arm.pose.bones["UpperArm.R"], arm.pose.bones["ForeArm.R"]
            dua = (ua.bone.tail_local - ua.bone.head_local).normalized()
            Rua = dua.rotation_difference(Vector((-1, 0, 0.12)).normalized()).to_matrix()
            Rtot = (fa.bone.tail_local - fa.bone.head_local).normalized().rotation_difference(Vector((0, 0, 1))).to_matrix()
            ua.rotation_quaternion = world_rot_to_local(ua, Rua)
            fa.rotation_quaternion = world_rot_to_local(fa, Rua.inverted() @ Rtot)
            bpy.context.view_layer.update()
            ctr = c.matrix_world @ Vector(c.bound_box[0]).lerp(Vector(c.bound_box[6]), 0.5)
            nrm = (c.matrix_world.to_3x3() @ Vector((0, 0, 1))).normalized()
            report.append("  card-up pose: Card centre=%s, front normal=%s (Blender, -Y = figure front)" % (fmt(ctr), fmt(nrm)))
            for b in ("UpperArm.R", "ForeArm.R"):
                ax, an = arm.pose.bones[b].rotation_quaternion.to_axis_angle()
                report.append("  card-up %s = %.0f deg about Blender local %s" % (b, math.degrees(an), fmt(ax)))
            cp = os.path.join(SHOT, name + "_cardup.png")
            scene.render.filepath = cp; bpy.ops.render.render(write_still=True)
            report.append("  preview: %s" % cp)
        report.append("  previews: %s, %s" % (rest, pose))
    txt = "\n".join(report)
    print(txt)
    with open(os.path.join(SHOT, "validation.txt"), "w") as f:
        f.write(txt + "\n")


main()
