# Builds SM_ChampionBelt: a closed oval leather loop with a big gold front plate (raised rim, embossed star
# medallion, corner studs) and two smaller gold side plates. Re-imports the GLB, prints checks, renders a preview.
# Run:  "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" --background --python build_belt.py
# Blender side: Z up, front plate faces -Y (-> glTF +Z, Unity +Z via glTFast), origin at the centre of the loop.
import bpy, bmesh, math, os
from mathutils import Vector

ROOT = r"C:\Users\punko\Downloads\PoBox"
GLB = os.path.join(ROOT, "Assets", "Art", "Exports", "GLB", "SM_ChampionBelt.glb")
BLEND = os.path.join(ROOT, "Assets", "Art", "Raw", "Figures", "SM_ChampionBelt.blend")
PNG = os.path.join(ROOT, "SCREENSHOTS", "2026-11-03-figures", "SM_ChampionBelt.png")
for p in (GLB, BLEND, PNG): os.makedirs(os.path.dirname(p), exist_ok=True)

RX, RY = 0.146, 0.116        # strap centre line (outer size ~0.30 x 0.24 incl. thickness)
STRAP_T, STRAP_H = 0.008, 0.09
OUT = STRAP_T / 2            # strap outer surface offset

verts, faces, fmat = [], [], []
MATS = ["M_BeltStrap", "M_BeltGold"]


def V(p): verts.append(Vector(p)); return len(verts) - 1
def F(idx, m): faces.append(list(idx)); fmat.append(MATS.index(m))


def P(th): return Vector((RX * math.cos(th), RY * math.sin(th), 0))
def N(th): return Vector((math.cos(th) / RX, math.sin(th) / RY, 0)).normalized()


# arc-length table around the ellipse, starting at the front (theta = -90 deg, y = -RY)
TAB = []
_s, _prev = 0.0, P(-math.pi / 2)
for i in range(4001):
    th = -math.pi / 2 + 2 * math.pi * i / 4000
    p = P(th); _s += (p - _prev).length; _prev = p; TAB.append((_s, th))


def theta_at(s):
    """theta for signed arc length s from the front centre (+s toward +X)"""
    total = TAB[-1][0]; s = s % total
    lo, hi = 0, len(TAB) - 1
    while hi - lo > 1:
        mid = (lo + hi) // 2
        if TAB[mid][0] < s: lo = mid
        else: hi = mid
    return TAB[lo][1]


def surf(s, z, depth):
    th = theta_at(s)
    return P(th) + N(th) * (OUT + depth) + Vector((0, 0, z))


# ---- strap: closed loop with a rectangular section
SEG = 48
ring = []
for i in range(SEG):
    th = 2 * math.pi * i / SEG
    p, n = P(th), N(th)
    ring.append([V(p + n * r + Vector((0, 0, z))) for r, z in
                 ((OUT, -STRAP_H / 2), (OUT, STRAP_H / 2), (-OUT, STRAP_H / 2), (-OUT, -STRAP_H / 2))])
for i in range(SEG):
    a, b = ring[i], ring[(i + 1) % SEG]
    for k in range(4):
        k2 = (k + 1) % 4
        F([a[k], b[k], b[k2], a[k2]], "M_BeltStrap")


# ---- plates: curved slabs on the strap, raised rim ridge, cushioned centre
def plate(s0, width, height, cols, rows, taper, rim=0.010, inner=0.005, bulge=0.004, back=-0.002):
    grid = []
    for i in range(cols + 1):
        sn = -1 + 2 * i / cols
        h = height / 2 * (1 - taper * sn * sn)
        col = []
        for j in range(rows + 1):
            tn = -1 + 2 * j / rows
            edge = i in (0, cols) or j in (0, rows)
            ridge = i in (1, cols - 1) or j in (1, rows - 1)
            d = 0.0 if edge else rim if ridge else inner + bulge * (1 - sn * sn) * (1 - tn * tn)
            col.append(V(surf(s0 + sn * width / 2, tn * h, d)))
        grid.append(col)
    for i in range(cols):
        for j in range(rows):
            F([grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]], "M_BeltGold")
    # back skin (just inside the strap's outer face) and the side band joining it to the front edge
    bnd = [grid[i][0] for i in range(cols + 1)] + [grid[cols][j] for j in range(1, rows + 1)] + \
          [grid[i][rows] for i in range(cols - 1, -1, -1)] + [grid[0][j] for j in range(rows - 1, 0, -1)]
    bk = []
    for i in range(cols + 1):
        sn = -1 + 2 * i / cols
        h = height / 2 * (1 - taper * sn * sn)
        bk.append([V(surf(s0 + sn * width / 2, (-1 + 2 * j / rows) * h, back)) for j in range(rows + 1)])
    for i in range(cols):
        for j in range(rows):
            F([bk[i][j], bk[i][j + 1], bk[i + 1][j + 1], bk[i + 1][j]], "M_BeltGold")
    bbnd = [bk[i][0] for i in range(cols + 1)] + [bk[cols][j] for j in range(1, rows + 1)] + \
           [bk[i][rows] for i in range(cols - 1, -1, -1)] + [bk[0][j] for j in range(rows - 1, 0, -1)]
    n = len(bnd)
    for k in range(n):
        F([bnd[k], bbnd[k], bbnd[(k + 1) % n], bnd[(k + 1) % n]], "M_BeltGold")


def disk(center, normal, r_bottom, r_top, height, seg, m="M_BeltGold"):
    """beveled disk standing on 'center' along 'normal'; returns top centre"""
    nrm = normal.normalized(); up = Vector((0, 0, 1)); side = up.cross(nrm).normalized()
    up = nrm.cross(side).normalized()
    bot = [V(center + (side * math.cos(a) + up * math.sin(a)) * r_bottom) for a in
           (2 * math.pi * k / seg for k in range(seg))]
    top = [V(center + nrm * height + (side * math.cos(a) + up * math.sin(a)) * r_top) for a in
           (2 * math.pi * k / seg for k in range(seg))]
    for k in range(seg):
        k2 = (k + 1) % seg
        F([bot[k], bot[k2], top[k2], top[k]], m)
    F(list(reversed(bot)), m); F(top, m)
    return center + nrm * height, side, up, nrm


def star(center, side, up, nrm, r_out, r_in, height, m="M_BeltGold"):
    pts = []
    for k in range(10):
        a = math.pi / 2 + math.pi * k / 5
        r = r_out if k % 2 == 0 else r_in
        pts.append(side * math.cos(a) * r + up * math.sin(a) * r)
    base = [V(center + p - nrm * 0.001) for p in pts]
    top = [V(center + p * 0.85 + nrm * height) for p in pts]
    tip = V(center + nrm * (height + 0.002))
    for k in range(10):
        k2 = (k + 1) % 10
        F([base[k], base[k2], top[k2], top[k]], m)
        F([top[k], top[k2], tip], m)


def dome(center, nrm, r, seg=8, rings=3, m="M_BeltGold"):
    nrm = nrm.normalized(); side = Vector((0, 0, 1)).cross(nrm).normalized(); up = nrm.cross(side)
    rows = []
    for i in range(rings + 1):
        t = (math.pi / 2) * i / rings
        rows.append([V(center + (side * math.cos(a) + up * math.sin(a)) * r * math.cos(t) + nrm * r * 0.6 * math.sin(t) - nrm * 0.002)
                     for a in (2 * math.pi * k / seg for k in range(seg))])
    tip = V(center + nrm * r * 0.6)
    for i in range(rings):
        for k in range(seg):
            k2 = (k + 1) % seg
            F([rows[i][k], rows[i][k2], rows[i + 1][k2], rows[i + 1][k]], m)
    for k in range(seg): F([rows[-1][k], rows[-1][(k + 1) % seg], tip], m)
    F(list(reversed(rows[0])), m)


# front plate 0.24 x 0.18, centred on the front
FW, FH = 0.24, 0.18
plate(0.0, FW, FH, 18, 12, taper=0.30)
th0 = theta_at(0.0)
c = surf(0.0, 0.0, 0.004)
top, sd, up, nrm = disk(c, N(th0), 0.058, 0.052, 0.012, 24)
disk(top - nrm * 0.002, nrm, 0.044, 0.044, 0.004, 24)        # inner raised ring face
star(top + nrm * 0.002, sd, up, nrm, 0.036, 0.015, 0.004)
for sx in (-1, 1):                                            # corner studs
    for sz in (-1, 1):
        s = sx * 0.085
        h = FH / 2 * (1 - 0.30 * (0.085 / 0.12) ** 2)
        th = theta_at(s)
        dome(surf(s, sz * (h - 0.026), 0.007), N(th), 0.010)
# side plates 0.08 x 0.10, about a quarter of the way round
total = TAB[-1][0]
for sx in (-1, 1):
    s = sx * total * 0.21
    plate(s, 0.08, 0.10, 6, 6, taper=0.15, rim=0.007, inner=0.004, bulge=0.002)
    th = theta_at(s)
    dome(surf(s, 0.0, 0.005), N(th), 0.016, seg=10, rings=3)

# ---- object, materials
bpy.ops.wm.read_factory_settings(use_empty=True)
me = bpy.data.meshes.new("SM_ChampionBelt")
me.from_pydata([tuple(v) for v in verts], [], faces); me.update()
for name, col, metal, rough in (("M_BeltStrap", (0.02, 0.018, 0.016), 0.0, 0.45), ("M_BeltGold", (1.0, 0.72, 0.22), 1.0, 0.28)):
    m = bpy.data.materials.new(name)
    b = m.node_tree.nodes.get("Principled BSDF") if m.node_tree else None
    if b is None:
        m.use_nodes = True; b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*col, 1); b.inputs["Metallic"].default_value = metal
    b.inputs["Roughness"].default_value = rough
    m.diffuse_color = (*col, 1); m.metallic = metal; m.roughness = rough
    me.materials.append(m)
for p, mi in zip(me.polygons, fmat): p.material_index = mi
bm = bmesh.new(); bm.from_mesh(me)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.to_mesh(me); bm.free()
for p in me.polygons: p.use_smooth = False
ob = bpy.data.objects.new("SM_ChampionBelt", me)
bpy.context.scene.collection.objects.link(ob)
tris = sum(len(p.vertices) - 2 for p in me.polygons)
bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); bpy.context.view_layer.objects.active = ob
bpy.ops.export_scene.gltf(filepath=GLB, use_selection=True, export_format='GLB', export_yup=True, export_apply=True,
                          export_animations=False, export_materials='EXPORT', export_normals=True)
bpy.ops.wm.save_as_mainfile(filepath=BLEND)
print("BUILT SM_ChampionBelt tris=%d" % tris)

# ---- re-import check + preview
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)
obs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
o = obs[0]
ws = [o.matrix_world @ v.co for v in o.data.vertices]
lo = Vector((min(v.x for v in ws), min(v.y for v in ws), min(v.z for v in ws)))
hi = Vector((max(v.x for v in ws), max(v.y for v in ws), max(v.z for v in ws)))
gold = [o.matrix_world @ v.co for v in o.data.vertices
        if any(o.material_slots[p.material_index].material.name == "M_BeltGold" for p in [])]
print("REIMPORT objects=%s tris=%d mats=%s" % ([x.name for x in obs], sum(len(p.vertices) - 2 for p in o.data.polygons),
                                               [s.material.name for s in o.material_slots]))
print("BOUNDS (Blender, -Y = front) x[%.3f,%.3f] y[%.3f,%.3f] z[%.3f,%.3f]" % (lo.x, hi.x, lo.y, hi.y, lo.z, hi.z))
fy = min(v.y for v in ws)
front = [v for v in ws if v.y < fy + 0.03]
print("FRONT-most point y=%.3f (front plate is on -Y: %s)" % (fy, abs(fy) > abs(hi.y)))
sc = bpy.context.scene
sc.render.engine = 'BLENDER_EEVEE'
sc.render.resolution_x = 900; sc.render.resolution_y = 650
w = bpy.data.worlds.new("W"); sc.world = w
w.use_nodes = True if not w.node_tree else w.use_nodes
bg = w.node_tree.nodes.get("Background"); bg.inputs[0].default_value = (0.35, 0.37, 0.42, 1); bg.inputs[1].default_value = 0.8
for loc, e in (((-0.6, -0.9, 0.7), 60), ((0.8, -0.4, 0.5), 25), ((0.0, 0.8, 0.6), 20)):
    ld = bpy.data.lights.new("L", 'AREA'); ld.energy = e; ld.size = 0.6
    lo_ = bpy.data.objects.new("L", ld); sc.collection.objects.link(lo_); lo_.location = loc
    lo_.rotation_euler = (Vector((0, 0, 0)) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
cd = bpy.data.cameras.new("C"); cd.lens = 60
cam = bpy.data.objects.new("C", cd); sc.collection.objects.link(cam)
cam.location = (-0.38, -0.78, 0.30)
cam.rotation_euler = (Vector((0, -0.02, -0.01)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
sc.camera = cam
sc.render.filepath = PNG
bpy.ops.render.render(write_still=True)
print("PREVIEW", PNG)
