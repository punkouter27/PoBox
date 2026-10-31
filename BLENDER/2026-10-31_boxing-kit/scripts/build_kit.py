"""Builds Kit_<Name>.glb (satin trunks + high-top boots) for each PoBox boxer.
Run: blender -b --python build_kit.py -- [Name ...]
Reads Assets/Models/<Name>.glb (never writes it). Writes only under BLENDER/2026-10-31_boxing-kit/.

Method: each garment piece is a loft of rings. Every ring vertex is placed by casting a ray from outside
toward the ring centre onto the boxer's own lower-body surface (arm and head vertices removed by their
skin weights, one leg's vertices only for a leg piece), then pushed out by an offset. Weights come from
the same surface with a Data Transfer modifier (nearest face, interpolated)."""
import bpy, bmesh, sys, math, json
from mathutils import Vector
from mathutils.bvhtree import BVHTree

ROOT = "C:/Users/punko/Downloads/PoBox/"
OUT = ROOT + "BLENDER/2026-10-31_boxing-kit/"
MODELS = ROOT + "Assets/Models/"

def legs(t, s, f, toe): return dict(thigh=t, calf=s, foot=f, toe=toe)
MX = "mixamorig:"
STD = dict(hips="Hips", L=legs("LeftUpLeg", "LeftLeg", "LeftFoot", "LeftToeBase"), R=legs("RightUpLeg", "RightLeg", "RightFoot", "RightToeBase"))
BONES = {
    "Matt": STD, "Nick": STD, "Grandma": STD, "Grandpa": STD,
    "Trump": dict(hips=MX + "Hips", L=legs(MX + "LeftUpLeg", MX + "LeftLeg", MX + "LeftFoot", MX + "LeftToeBase"),
                  R=legs(MX + "RightUpLeg", MX + "RightLeg", MX + "RightFoot", MX + "RightToeBase")),
    "Zombie": dict(hips="CC_Base_Pelvis", L=legs("CC_Base_L_Thigh", "CC_Base_L_Calf", "CC_Base_L_Foot", "CC_Base_L_ToeBase"),
                   R=legs("CC_Base_R_Thigh", "CC_Base_R_Calf", "CC_Base_R_Foot", "CC_Base_R_ToeBase")),
    "Kim": dict(hips="Hips", L=legs("UpperLeg.L", "LowerLeg.L", "Foot.L", "Toe.L"), R=legs("UpperLeg.R", "LowerLeg.R", "Foot.R", "Toe.R")),
    "Sokol": dict(hips="hips", L=legs("thigh.L", "shin.L", "foot.L", None), R=legs("thigh.R", "shin.R", "foot.R", None)),
}
REPORT = {}
MAT_TRUNKS, MAT_BAND, MAT_BOOTS, MAT_LACES = 0, 1, 2, 3
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
F = Vector((0, -1, 0))      # every boxer faces -Y in Blender (+Z in glTF/Unity)

def log(*a): print("[kit]", *a, flush=True)

# ---------------------------------------------------------------- loading
def load(name):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=MODELS + name + ".glb")
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    arm.data.pose_position = 'REST'
    for o in list(bpy.data.objects):
        if o.type == 'MESH' and not any(m.type == 'ARMATURE' for m in o.modifiers):
            bpy.data.objects.remove(o)          # stray unskinned Icosphere in every source file
    bodies = [o for o in bpy.data.objects if o.type == 'MESH']
    return arm, bodies

def descendants(arm, names):
    out = set()
    for b in arm.data.bones:
        p = b
        while p:
            if p.name in names: out.add(b.name); break
            p = p.parent
    return out

def bone_sets(arm, cfg):
    low = lambda n: n.lower()
    arm_roots = {b.name for b in arm.data.bones if any(k in low(b.name) for k in ("shoulder", "clavicle"))}
    head_roots = {b.name for b in arm.data.bones if "neck" in low(b.name) or low(b.name) == "head"}
    return dict(arms=descendants(arm, arm_roots), head=descendants(arm, head_roots),
                L=descendants(arm, {cfg["L"]["thigh"]}), R=descendants(arm, {cfg["R"]["thigh"]}))

def bpos(arm, name): return arm.matrix_world @ arm.data.bones[name].head_local
def wsum(w, s): return sum(x for n, x in w.items() if n in s)
def perp(v, axis): return (v - axis * v.dot(axis)).normalized()

# ---------------------------------------------------------------- source (lower body) meshes
def make_source(bodies, keep, name):
    """World-space copy of the body meshes with their vertex groups, holding only vertices keep(weights, co) accepts."""
    objs = []
    for b in bodies:
        me = b.data.copy(); me.transform(b.matrix_world)
        o = bpy.data.objects.new("SRC_" + b.name, me); bpy.context.scene.collection.objects.link(o)
        for vg in b.vertex_groups: o.vertex_groups.new(name=vg.name)
        names = [vg.name for vg in b.vertex_groups]
        bm = bmesh.new(); bm.from_mesh(me)
        dl = bm.verts.layers.deform.verify()
        kill = [v for v in bm.verts if not keep({names[i]: x for i, x in v[dl].items()}, v.co)]
        bmesh.ops.delete(bm, geom=kill, context='VERTS')
        bm.to_mesh(me); bm.free()
        objs.append(o)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1: bpy.ops.object.join()
    src = bpy.context.view_layer.objects.active; src.name = name
    bvh = BVHTree.FromPolygons([v.co.copy() for v in src.data.vertices], [p.vertices[:] for p in src.data.polygons])
    return src, bvh

# ---------------------------------------------------------------- ring fitting
def cast(bvh, c, d, R0):
    hit = bvh.ray_cast(c + d * R0, -d, R0 * 1.2)
    if hit[0] is None: return None
    return (hit[0] - c).dot(d)

def fit_ring(bvh, c, u, v, N, R0, fallback, rmin=0.0, convex=6, smooth=1):
    """Radii of the outermost surface around c in the (u, v) plane. Vertex k sits at angle 2pi(k+0.5)/N, so
    face column N-1 is centred on +u and column N/2-1 on -u."""
    dirs, radii = [], []
    for k in range(N):
        th = 2 * math.pi * (k + 0.5) / N
        d = (u * math.cos(th) + v * math.sin(th)).normalized()
        r = cast(bvh, c, d, R0)
        if r is not None and r < rmin: r = None
        dirs.append(d); radii.append(r)
    fit_ring.hits = sum(r is not None for r in radii) / N
    if not any(r is not None for r in radii): radii = [fallback] * N
    else:
        for k in range(N):
            if radii[k] is None:
                for j in range(1, N):
                    a, b = radii[(k - j) % N], radii[(k + j) % N]
                    if a is not None or b is not None:
                        radii[k] = a if b is None else (b if a is None else 0.5 * (a + b)); break
    for _ in range(convex):     # bridge creases: satin and leather do not follow every dip
        radii = [max(radii[k], 0.5 * (radii[k - 1] + radii[(k + 1) % N])) for k in range(N)]
    for _ in range(smooth):
        radii = [0.25 * radii[k - 1] + 0.5 * radii[k] + 0.25 * radii[(k + 1) % N] for k in range(N)]
    return dirs, radii

class Tube:
    """Rings of N vertices lofted into quads, UVs unrolled into a rectangle."""
    def __init__(s, N): s.N = N; s.rings = []; s.vs = []
    def add(s, pts, v): s.rings.append(pts); s.vs.append(v)
    def build(s, name, uvrect, matfn, caps=()):
        me = bpy.data.meshes.new(name); bm = bmesh.new()
        uvl = bm.loops.layers.uv.new("UVMap")
        V = [[bm.verts.new(p) for p in r] for r in s.rings]
        u0, v0, u1, v1 = uvrect; vmin, vmax = min(s.vs), max(s.vs)
        def uv(k, j): return Vector((u0 + (u1 - u0) * k / s.N, v0 + (v1 - v0) * (s.vs[j] - vmin) / max(1e-6, vmax - vmin)))
        for j in range(len(V) - 1):
            for k in range(s.N):
                k2 = (k + 1) % s.N
                f = bm.faces.new((V[j][k], V[j][k2], V[j + 1][k2], V[j + 1][k]))
                f.material_index = matfn(j, k)
                for l, (kk, jj) in zip(f.loops, ((k, j), (k + 1, j), (k + 1, j + 1), (k, j + 1))): l[uvl].uv = uv(kk, jj)
        for j, pole in caps:    # close ring j with a fan
            pv = bm.verts.new(pole)
            for k in range(s.N):
                f = bm.faces.new((V[j][k], V[j][(k + 1) % s.N], pv))
                f.material_index = matfn(j, k)
                for l, (kk, jj) in zip(f.loops, ((k, j), (k + 1, j), (k + 0.5, j))): l[uvl].uv = uv(kk, jj)
        bm.to_mesh(me); bm.free()
        for p in me.polygons: p.use_smooth = True
        o = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(o)
        return o

def smooth_long(rings, it):
    """Smooth vertex k of each inner ring with vertex k of its neighbours; end rings stay."""
    rings = [[p.copy() for p in r] for r in rings]
    for _ in range(it):
        rings = [rings[0]] + [[a * 0.25 + b * 0.5 + c * 0.25 for a, b, c in zip(r0, r1, r2)]
                              for r0, r1, r2 in zip(rings[:-2], rings[1:-1], rings[2:])] + [rings[-1]]
    return rings

def foot_axis(pts, hint):
    """Principal horizontal axis of the foot's vertices, pointing the way hint (ankle -> toe) points."""
    n = len(pts); mx = sum(p.x for p in pts) / n; my = sum(p.y for p in pts) / n
    sxx = sum((p.x - mx) ** 2 for p in pts); syy = sum((p.y - my) ** 2 for p in pts); sxy = sum((p.x - mx) * (p.y - my) for p in pts)
    ang = 0.5 * math.atan2(2 * sxy, sxx - syy)
    a = Vector((math.cos(ang), math.sin(ang), 0))
    return a if a.dot(Vector((hint.x, hint.y, 0))) > 0 else -a

def orient_outward(o, axis_point):
    """Flip all faces if most of them point at the piece's own axis."""
    me = o.data
    score = sum(p.normal.dot(p.center - axis_point(p.center)) * p.area for p in me.polygons)
    if score < 0:
        bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.reverse_faces(bm, faces=bm.faces[:]); bm.to_mesh(me); bm.free()

def transfer_weights(dst, src):
    for vg in src.vertex_groups:
        if dst.vertex_groups.get(vg.name) is None: dst.vertex_groups.new(name=vg.name)
    m = dst.modifiers.new("DT", 'DATA_TRANSFER')
    m.object = src; m.use_vert_data = True; m.data_types_verts = {'VGROUP_WEIGHTS'}
    m.vert_mapping = 'POLYINTERP_NEAREST'
    m.layers_vgroup_select_src = 'ALL'; m.layers_vgroup_select_dst = 'NAME'
    bpy.ops.object.select_all(action='DESELECT'); dst.select_set(True); bpy.context.view_layer.objects.active = dst
    bpy.ops.object.modifier_apply(modifier=m.name)

# ---------------------------------------------------------------- build
def build(name):
    cfg = BONES[name]
    arm, bodies = load(name)
    sets = bone_sets(arm, cfg)
    H = bpos(arm, cfg["hips"])
    P = {s: {k: (bpos(arm, b) if b else None) for k, b in cfg[s].items()} for s in "LR"}
    Lleg = sum(P[s]["thigh"].z - P[s]["foot"].z for s in "LR") / 2
    K = Lleg / 0.85                                    # size factor vs. Matt
    log(name, "leg", round(Lleg, 3), "K", round(K, 2), "bodies", [b.name for b in bodies])

    lower = lambda w, co: wsum(w, sets["arms"]) + wsum(w, sets["head"]) < 0.3
    src, bvh = make_source(bodies, lower, "SRC")
    side, srcs, bvhs = {}, {}, {}
    for s, o in (("L", "R"), ("R", "L")):
        side[s] = 1.0 if P[s]["thigh"].x > H.x else -1.0
        def keep(w, co, s=s, o=o):
            if not lower(w, co): return False
            a, b = wsum(w, sets[s]), wsum(w, sets[o])
            if a > b: return True
            return a == b == 0 and (co.x - H.x) * side[s] > 0
        srcs[s], bvhs[s] = make_source(bodies, keep, "SRC_" + s)

    parts = []
    # ---- trunks: pelvis tube
    hj = 0.5 * (P["L"]["thigh"] + P["R"]["thigh"])
    tlen = 0.5 * sum((P[s]["thigh"] - P[s]["calf"]).length for s in "LR")
    knee_z = 0.5 * (P["L"]["calf"].z + P["R"]["calf"].z)
    hit = bvh.ray_cast(Vector((hj.x, hj.y, knee_z)), Z, 2.0)
    crotch = hit[0].z if hit[0] is not None and hit[0].z < hj.z else hj.z - 0.12 * tlen
    crotch = min(max(crotch, hj.z - 0.22 * tlen), hj.z - 0.05 * tlen)   # long jackets / loose trousers fool the ray
    waist = hj.z + 0.30 * tlen
    band = 0.20 * tlen
    log(" hipjoint", round(hj.z, 3), "crotch", round(crotch, 3), "waist", round(waist, 3), "thigh", round(tlen, 3))
    N = 40
    zs = [waist, waist - band * 0.5, waist - band]
    nlow = 7
    zs += [waist - band - (waist - band - (crotch - 0.012 * K)) * (i + 1) / nlow for i in range(nlow)]
    rings = []
    for z in zs:
        c = Vector((hj.x, hj.y, z))
        dirs, radii = fit_ring(bvh, c, X, F, N, 0.7 * K, 0.18 * K, rmin=0.04 * K)
        off = (0.024 if z > waist - band - 1e-6 else 0.018) * K
        rings.append([c + d * (r + off) for d, r in zip(dirs, radii)])
    tube = Tube(N)
    c0 = Vector((hj.x, hj.y, waist))
    tube.add([c0 + (p - c0) * (1 - 0.012 * K / max(0.05, (p - c0).length)) - Z * 0.010 * K for p in rings[0]], -0.03)  # waistband fold
    for z, r in zip(zs, rings): tube.add(r, waist - z)
    pel = tube.build("trunks_pelvis", (0, 0.62, 1, 1),
                     lambda j, k: MAT_BAND if j <= 2 or k in (N - 1, N // 2 - 1) else MAT_TRUNKS)
    orient_outward(pel, lambda p: Vector((hj.x, hj.y, p.z)))
    transfer_weights(pel, src); parts.append(pel)

    for s in "LR":
        th, ca, fo = P[s]["thigh"], P[s]["calf"], P[s]["foot"]
        # ---- trunks: leg tube from just above the crotch to the hem at mid-thigh
        ax = (ca - th).normalized()
        u = perp(X * side[s], ax); v = ax.cross(u)
        t_top = max(0.0, (th.z - (crotch + 0.04 * K)) / max(1e-4, -ax.z))
        t_hem = 0.56 * tlen
        M = 24; tube = Tube(M); nr = 8; last = None
        for i in range(nr + 1):
            t = t_top + (t_hem - t_top) * i / nr
            c = th + ax * t
            dirs, radii = fit_ring(bvhs[s], c, u, v, M, 0.4 * K, 0.08 * K, rmin=0.02 * K, convex=4)
            f = i / nr
            pts = [c + d * (r + (0.012 + 0.022 * f * f * (0.45 + 0.55 * (0.5 + 0.5 * d.dot(u)))) * K) for d, r in zip(dirs, radii)]
            tube.add(pts, t); last = (c, pts)
        c, pts = last
        tube.add([p + (c - p).normalized() * 0.008 * K for p in pts], t_hem + 0.012)   # hem fold
        leg = tube.build("trunks_leg_" + s, (0, 0.32, 0.5, 0.6) if s == "L" else (0.5, 0.32, 1, 0.6),
                         lambda j, k: MAT_BAND if k == M - 1 and j < nr else MAT_TRUNKS)
        orient_outward(leg, lambda p, th=th, ax=ax: th + ax * (p - th).dot(ax))
        transfer_weights(leg, srcs[s]); parts.append(leg)

        # ---- boot: foot vertices and the foot's forward
        sax = (fo - ca).normalized(); slen = (fo - ca).length       # knee -> ankle
        gi = {vg.index: vg.name for vg in srcs[s].vertex_groups}
        footset = descendants(arm, {cfg[s]["foot"]})
        foot_verts = [vv.co.copy() for vv in srcs[s].data.vertices
                      if sum(g.weight for g in vv.groups if gi[g.group] in footset) > 0.5 and vv.co.z < fo.z + 0.03 * K]
        if len(foot_verts) < 20: raise RuntimeError("too few foot vertices for %s %s" % (name, s))
        fwd = foot_axis(foot_verts, (P[s]["toe"] - fo) if P[s]["toe"] else F)   # some feet turn out 45 degrees
        sole = min(p.z for p in foot_verts)

        # ---- boot: shin tube, collar at 42 % of the shin above the ankle
        su = perp(fwd, sax); sv = sax.cross(su)
        top_t = 0.42 * slen; nr = 8; shin_rings = []
        for i in range(nr + 1):
            t = top_t - (top_t + 0.015 * K) * i / nr
            c = fo - sax * t
            dirs, radii = fit_ring(bvhs[s], c, su, sv, 20, 0.3 * K, 0.05 * K, rmin=0.015 * K, convex=3)
            shin_rings.append((c, radii, [c + d * (r + 0.014 * K) for d, r in zip(dirs, radii)]))
        sm = smooth_long([p for (_, _, p) in shin_rings], 2)
        shin_rings = [(c, r, p) for (c, r, _), p in zip(shin_rings, sm)]
        tube = Tube(20)
        c, _, pts = shin_rings[0]
        tube.add([p + (c - p).normalized() * 0.007 * K - sax * 0.008 * K for p in pts], -1)   # collar fold
        for i, (c, r_, pts) in enumerate(shin_rings): tube.add(pts, i)
        shin = tube.build("boot_shin_" + s, (0, 0.16, 0.5, 0.32) if s == "L" else (0.5, 0.16, 1, 0.32), lambda j, k: MAT_BOOTS)
        orient_outward(shin, lambda p, fo=fo, sax=sax: fo + sax * (p - fo).dot(sax))
        transfer_weights(shin, srcs[s]); parts.append(shin)

        # ---- boot: laces, X-crossing bars over the front (+su) of the shin tube
        bm = bmesh.new(); hw = 0.017 * K
        eyelets = [c + su * (0.5 * (radii[-1] + radii[0]) + 0.017 * K) for (c, radii, pts) in shin_rings[1:-1]]
        def bar(a, b, w=0.0035 * K, h=0.003 * K):
            d = (b - a).normalized(); sd = d.cross(su).normalized()
            vs = [bm.verts.new(p) for p in (a - sd * w, a + sd * w, b + sd * w, b - sd * w)]
            vt = [bm.verts.new(p.co + su * h) for p in vs]
            for q in ((vt[0], vt[1], vt[2], vt[3]), (vs[0], vs[3], vs[2], vs[1]), (vs[0], vs[1], vt[1], vt[0]),
                      (vs[1], vs[2], vt[2], vt[1]), (vs[2], vs[3], vt[3], vt[2]), (vs[3], vs[0], vt[0], vt[3])):
                bm.faces.new(q).material_index = MAT_LACES
        for a, b in zip(eyelets[:-1], eyelets[1:]):
            bar(a - sv * hw, b + sv * hw); bar(a + sv * hw, b - sv * hw)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        uvl = bm.loops.layers.uv.new("UVMap")
        for f in bm.faces:
            for l in f.loops: l[uvl].uv = (0.97, 0.03)
        lme = bpy.data.meshes.new("laces_" + s); bm.to_mesh(lme); bm.free()
        laces = bpy.data.objects.new("boot_laces_" + s, lme); bpy.context.scene.collection.objects.link(laces)
        transfer_weights(laces, srcs[s]); parts.append(laces)

        # ---- boot: foot tube along fwd, cross-sections in the (side, up) plane, flat sole, closed heel and toe
        side_v = fwd.cross(Z).normalized()
        proj = [(p - fo).dot(fwd) for p in foot_verts]
        s0, s1 = min(proj), max(proj)
        top_lim = fo.z + 0.02 * K
        nf = 12; frings = []
        for i in range(nf + 1):
            t = s0 + (s1 - s0) * (0.03 + 0.94 * i / nf)
            sl = [p for p, q in zip(foot_verts, proj) if abs(q - t) < 0.012 * K] or foot_verts
            lat = sum((p - fo).dot(side_v) for p in sl) / len(sl)
            zlo = min(p.z for p in sl); zhi = min(max(p.z for p in sl), top_lim)
            c = fo + fwd * t + side_v * lat; c.z = 0.5 * (zlo + zhi)
            dirs, radii = fit_ring(bvhs[s], c, side_v, Z, 20, 0.25 * K, 0.04 * K, rmin=0.005 * K, convex=5)
            if fit_ring.hits < 0.6: continue                          # slice off the end of the foot
            med = sorted(radii)[len(radii) // 2]
            radii = [min(r, 1.8 * med) for r in radii]
            pts = []
            for d, r in zip(dirs, radii):
                if d.z > 0.2: r = min(r, (top_lim - c.z) / d.z)
                p = c + d * (r + 0.015 * K)
                if d.z < -0.5: p.z = sole - 0.003 * K
                p.z = max(p.z, sole - 0.003 * K)
                pts.append(p)
            frings.append((c, pts))
        sm = smooth_long([p for _, p in frings], 2)
        for ring in sm:
            for p in ring: p.z = max(p.z, sole - 0.003 * K)
        frings = [(c, p) for (c, _), p in zip(frings, sm)]
        tube = Tube(20)
        ch, ph = frings[0]
        tube.add([ch + (p - ch) * 0.6 - fwd * 0.012 * K for p in ph], -1)
        for i, (c, pts) in enumerate(frings): tube.add(pts, i)
        ct, pt = frings[-1]
        tube.add([ct + (p - ct) * 0.6 + fwd * 0.014 * K for p in pt], len(frings))
        foot = tube.build("boot_foot_" + s, (0, 0.0, 0.5, 0.16) if s == "L" else (0.5, 0.0, 1, 0.16), lambda j, k: MAT_BOOTS,
                          caps=[(0, ch - fwd * 0.016 * K), (len(frings) + 1, ct + fwd * 0.018 * K)])
        base = Vector((fo.x, fo.y, 0.5 * (sole + top_lim)))
        orient_outward(foot, lambda p, base=base, fwd=fwd: base + fwd * (p - base).dot(fwd))
        transfer_weights(foot, srcs[s]); parts.append(foot)

    # ---- join, materials, weights clean-up, parent to the armature
    for o in [srcs["L"], srcs["R"], src]: bpy.data.objects.remove(o)
    bpy.ops.object.select_all(action='DESELECT')
    mats = []
    for mname, rough in (("Trunks", 0.3), ("Waistband", 0.4), ("Boots", 0.45), ("Laces", 0.7)):
        m = bpy.data.materials.new(mname)
        if not m.node_tree: m.use_nodes = True
        bsdf = m.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value = (0.8, 0.8, 0.8, 1) if mname in ("Trunks", "Boots") else (0.92, 0.92, 0.92, 1)
        bsdf.inputs["Roughness"].default_value = rough
        m.use_backface_culling = False
        mats.append(m)
    for o in parts:              # same four slots on every piece, so material_index survives the join
        for m in mats: o.data.materials.append(m)
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    kit = bpy.context.view_layer.objects.active; kit.name = "Kit_" + name; kit.data.name = "Kit_" + name
    for gname in [vg.name for vg in kit.vertex_groups if vg.name in sets["arms"] | sets["head"]]:
        kit.vertex_groups.remove(kit.vertex_groups[gname])        # trunks and boots never follow an arm
    bpy.ops.object.vertex_group_limit_total(limit=4)
    bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    used = {kit.vertex_groups[g.group].name for vv in kit.data.vertices for g in vv.groups if g.weight > 1e-4}
    for gname in [vg.name for vg in kit.vertex_groups if vg.name not in used]:
        kit.vertex_groups.remove(kit.vertex_groups[gname])
    unweighted = sum(1 for vv in kit.data.vertices if not any(g.weight > 1e-4 for g in vv.groups))
    kit.parent = arm; kit.matrix_parent_inverse = arm.matrix_world.inverted()
    kit.modifiers.new("Armature", 'ARMATURE').object = arm

    tris = sum(len(p.vertices) - 2 for p in kit.data.polygons)
    per_mat = {}
    for p in kit.data.polygons:
        k = mats[p.material_index].name; per_mat[k] = per_mat.get(k, 0) + len(p.vertices) - 2
    REPORT[name] = dict(tris=tris, per_material=per_mat, verts=len(kit.data.vertices),
                        bones=sorted(vg.name for vg in kit.vertex_groups), unweighted_verts=unweighted,
                        armature=arm.name, waist_z=round(waist, 3), crotch_z=round(crotch, 3), K=round(K, 3))
    log(name, REPORT[name])

    # ---- export the kit with its armature only (no body mesh)
    bpy.ops.object.select_all(action='DESELECT')
    kit.select_set(True); arm.select_set(True)
    bpy.ops.export_scene.gltf(filepath=OUT + "Kit_%s.glb" % name, use_selection=True, export_format='GLB',
                              export_yup=True, export_apply=False, export_animations=False, export_skins=True,
                              export_materials='EXPORT', export_texcoords=True, export_normals=True)
    render(name, bodies, kit)
    # ---- .blend with kit + armature only (re-import the boxer GLB next to it to edit the fit)
    for b in bodies: bpy.data.objects.remove(b)
    for o in [o for o in bpy.data.objects if o.type in ('CAMERA', 'LIGHT')]: bpy.data.objects.remove(o)
    if arm.animation_data: arm.animation_data_clear()
    for m in kit.data.materials:   # back to neutral after the render colours
        m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.8, 0.8, 0.8, 1) if m.name in ("Trunks", "Boots") else (0.92, 0.92, 0.92, 1)
    bpy.ops.outliner.orphans_purge(do_recursive=True)
    bpy.ops.wm.save_as_mainfile(filepath=OUT + "Kit_%s.blend" % name, compress=True)

def render(name, bodies, kit):
    sc = bpy.context.scene
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    sc.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in engines else 'BLENDER_EEVEE_NEXT'
    if sc.world is None: sc.world = bpy.data.worlds.new("W")
    if not sc.world.node_tree: sc.world.use_nodes = True
    bg = sc.world.node_tree.nodes.get("Background"); bg.inputs[0].default_value = (0.35, 0.37, 0.42, 1)
    sc.view_settings.view_transform = 'Standard'
    cols = {"Trunks": (0.7, 0.05, 0.05, 1), "Waistband": (0.9, 0.9, 0.9, 1), "Boots": (0.06, 0.06, 0.07, 1), "Laces": (0.9, 0.9, 0.9, 1)}
    for m in kit.data.materials: m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = cols[m.name]
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", 'SUN')); sc.collection.objects.link(sun)
    sun.data.energy = 3.0; sun.rotation_euler = (math.radians(50), 0, math.radians(-30))
    pts = [b.matrix_world @ Vector(c) for b in bodies for c in b.bound_box]
    zmin = min(p.z for p in pts); zmax = max(p.z for p in pts)
    cx = 0.5 * (min(p.x for p in pts) + max(p.x for p in pts)); cy = 0.5 * (min(p.y for p in pts) + max(p.y for p in pts))
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); sc.collection.objects.link(cam); sc.camera = cam
    cam.data.type = 'ORTHO'
    for tag, yaw, z0, z1, w in (("front", 0, zmin, zmax, 640), ("34", 40, zmin, zmax, 640),
                                ("legs34", 35, zmin - 0.02, zmin + 0.62 * (zmax - zmin), 800)):
        sc.render.resolution_x, sc.render.resolution_y = w, 800
        cam.data.ortho_scale = (z1 - z0) * 1.08 * max(1.0, w / 800)
        a = math.radians(yaw)
        cam.location = Vector((cx, cy, 0.5 * (z0 + z1))) + Vector((math.sin(a), -math.cos(a), 0)) * 6
        cam.rotation_euler = (math.radians(90), 0, a)
        sc.render.filepath = OUT + "renders/%s_%s.png" % (name, tag)
        bpy.ops.render.render(write_still=True)

if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    names = argv or list(BONES)
    for n in names:
        try: build(n)
        except Exception as e:
            import traceback; traceback.print_exc(); REPORT[n] = dict(error=str(e))
    path = OUT + "scripts/report_%s.json" % ("_".join(names) if argv else "all")
    json.dump(REPORT, open(path, "w"), indent=1)
    log("done", path)
