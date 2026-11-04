# Builds three low-poly rigged ring-side figures (referee, cornerman, ring-card walker) for PoBox.
# Run:  "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" --background --python build_figures.py
# Writes  Assets/Art/Exports/GLB/SK_*.glb  and  Assets/Art/Raw/Figures/SK_*.blend
#
# Conventions (Blender side): Z up, figure faces -Y, figure's left (.L) at +X, feet at the origin, 1 unit = 1 m.
# Rest pose: A-pose (arms 45 degrees down). Every bone's local Y runs along the bone, local Z points to the
# figure's front (-Y) for every bone (Head/Neck/Spine too), so in Blender: rotating a leg bone about local X bends
# it forward/back, rotating an arm bone about local Z swings it out to the side and up (abduction).
# The glTF exporter's Y-up conversion turns Blender -Y forward into glTF +Z forward (Unity +Z after glTFast).
import bpy, bmesh, math, os
from mathutils import Vector, Matrix

ROOT = r"C:\Users\punko\Downloads\PoBox"
GLB_DIR = os.path.join(ROOT, "Assets", "Art", "Exports", "GLB")
RAW_DIR = os.path.join(ROOT, "Assets", "Art", "Raw", "Figures")
os.makedirs(GLB_DIR, exist_ok=True)
os.makedirs(RAW_DIR, exist_ok=True)

A45 = math.sqrt(0.5)
FWD = Vector((0, -1, 0))


# ------------------------------------------------------------------ mesh accumulator
class Builder:
    def __init__(self):
        self.verts, self.faces, self.fmat, self.weights = [], [], [], []
        self.mats = []          # material names, index = slot

    def mat(self, name):
        if name not in self.mats: self.mats.append(name)
        return self.mats.index(name)

    def add_vert(self, co, w):
        self.verts.append(Vector(co)); self.weights.append(dict(w)); return len(self.verts) - 1

    def add_face(self, idx, mat):
        self.faces.append(list(idx)); self.fmat.append(self.mat(mat))

    def loft(self, rings, n, cap_start=True, cap_end=True, zmin=None):
        """rings: list of dict(c=center, u=axis, v=axis, rx, ry, w=weights, m=material of the band after this ring,
        cap material = first/last ring's m). Ring k vertex j = c + u*cos*rx + v*sin*ry."""
        idx = []
        for r in rings:
            row = []
            for j in range(n):
                a = 2 * math.pi * j / n + math.pi / n
                p = Vector(r['c']) + Vector(r['u']) * math.cos(a) * r['rx'] + Vector(r['v']) * math.sin(a) * r['ry']
                if zmin is not None and p.z < zmin: p.z = zmin
                row.append(self.add_vert(p, r['w']))
            idx.append(row)
        for k in range(len(rings) - 1):
            for j in range(n):
                j2 = (j + 1) % n
                self.add_face([idx[k][j], idx[k][j2], idx[k + 1][j2], idx[k + 1][j]], rings[k]['m'])
        if cap_start: self.add_face(list(reversed(idx[0])), rings[0]['m'])
        if cap_end: self.add_face(idx[-1], rings[-1].get('mc', rings[-2]['m']))
        return idx

    def sphere(self, c, r, w, mat, seg=8, rings=5, scale=(1, 1, 1)):
        c = Vector(c)
        top = self.add_vert(c + Vector((0, 0, r * scale[2])), w)
        bot = self.add_vert(c - Vector((0, 0, r * scale[2])), w)
        rows = []
        for i in range(1, rings):
            t = math.pi * i / rings
            row = []
            for j in range(seg):
                a = 2 * math.pi * j / seg
                row.append(self.add_vert(c + Vector((math.sin(t) * math.cos(a) * r * scale[0],
                                                     math.sin(t) * math.sin(a) * r * scale[1],
                                                     math.cos(t) * r * scale[2])), w))
            rows.append(row)
        for j in range(seg):
            j2 = (j + 1) % seg
            self.add_face([top, rows[0][j], rows[0][j2]], mat)
            self.add_face([bot, rows[-1][j2], rows[-1][j]], mat)
            for i in range(len(rows) - 1):
                self.add_face([rows[i][j], rows[i + 1][j], rows[i + 1][j2], rows[i][j2]], mat)

    def box(self, c, half, w, mat, rot=None):
        c = Vector(c); rot = rot or Matrix.Identity(3)
        ids = []
        for z in (-1, 1):
            for y in (-1, 1):
                for x in (-1, 1):
                    ids.append(self.add_vert(c + rot @ Vector((x * half[0], y * half[1], z * half[2])), w))
        for f in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
            self.add_face([ids[i] for i in f], mat)


def zr(z, rx, ry, w, m, cy=0.0, cx=0.0, **kw):
    """ring around the vertical axis"""
    d = dict(c=(cx, cy, z), u=(1, 0, 0), v=(0, 1, 0), rx=rx, ry=ry, w=w, m=m); d.update(kw); return d


# ------------------------------------------------------------------ figure specs
def spec(kind):
    s = dict(kind=kind, height=1.75, female=False)
    if kind == "Referee":
        s.update(skin=(0.80, 0.60, 0.47), hair=(0.12, 0.09, 0.07),
                 cols=dict(M_Shirt=((0.93, 0.93, 0.92), 0.7), M_Trousers=((0.03, 0.03, 0.035), 0.85),
                           M_Shoes=((0.02, 0.02, 0.02), 0.3), M_BowTie=((0.01, 0.01, 0.01), 0.4),
                           M_Belt=((0.02, 0.02, 0.02), 0.35)),
                 top="M_Shirt", bottom="M_Trousers", shoes="M_Shoes", sole="M_Shoes", sleeve=0.14,
                 bow_tie=True, legs_covered=True)
    elif kind == "Cornerman":
        s.update(skin=(0.55, 0.38, 0.27), hair=(0.05, 0.04, 0.035),
                 cols=dict(M_Team=((1.0, 1.0, 1.0), 0.75), M_Trousers=((0.35, 0.35, 0.36), 0.8),
                           M_Shoes=((0.85, 0.85, 0.85), 0.6), M_Sole=((0.15, 0.15, 0.15), 0.8),
                           M_Towel=((0.97, 0.97, 0.95), 0.95), M_Belt=((0.1, 0.07, 0.05), 0.5)),
                 top="M_Team", bottom="M_Trousers", shoes="M_Shoes", sole="M_Sole", sleeve=0.13,
                 towel=True, collar=True, legs_covered=True)
    elif kind == "RingCardWalker":
        s.update(height=1.72, female=True, skin=(0.86, 0.66, 0.52), hair=(0.30, 0.17, 0.08),
                 cols=dict(M_Top=((0.95, 0.20, 0.45), 0.6), M_Shorts=((0.04, 0.04, 0.05), 0.55),
                           M_Shoes=((0.95, 0.95, 0.95), 0.6), M_Sole=((0.95, 0.20, 0.45), 0.6),
                           M_Belt=((0.04, 0.04, 0.05), 0.55)),
                 top="M_Top", bottom="M_Shorts", shoes="M_Shoes", sole="M_Sole", sleeve=None,
                 legs_covered=False, card=True, ponytail=True)
    return s


# ------------------------------------------------------------------ skeleton (Blender coords, 1.75 m figure)
def joints(s):
    k = s['height'] / 1.75
    f = s['female']
    sh_x = 0.175 if f else 0.19
    hip_x = 0.095
    J = dict(
        hips=Vector((0, 0, 0.95)), spine=Vector((0, 0, 1.05)), chest=Vector((0, 0, 1.20)),
        neck=Vector((0, 0, 1.43)), head=Vector((0, 0, 1.53)), head_top=Vector((0, 0, 1.75)))
    for side, sx in (("L", 1), ("R", -1)):
        sh = Vector((sx * sh_x, 0, 1.39))
        d = Vector((sx * A45, 0, -A45))
        ua, fa, ha = (0.28, 0.25, 0.17) if f else (0.29, 0.26, 0.18)
        J['sh' + side] = sh; J['el' + side] = sh + d * ua; J['wr' + side] = sh + d * (ua + fa)
        J['hd' + side] = sh + d * (ua + fa + ha); J['ad' + side] = d
        J['hip' + side] = Vector((sx * hip_x, 0, 0.93)); J['kn' + side] = Vector((sx * hip_x, -0.01, 0.50))
        J['an' + side] = Vector((sx * hip_x, 0.0, 0.085)); J['toe' + side] = Vector((sx * hip_x, -0.15, 0.03))
        J['arm_len' + side] = (ua, fa, ha)
    for key, v in J.items():
        if isinstance(v, Vector) and not key.startswith('ad'): J[key] = v * k
    J['k'] = k
    return J


def make_armature(name, J):
    arm_data = bpy.data.armatures.new(name + "_Rig")
    arm = bpy.data.objects.new("Rig", arm_data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm_data.edit_bones

    def bone(nm, h, t, parent=None, conn=False):
        b = eb.new(nm); b.head = h; b.tail = t
        b.align_roll(FWD)                     # local Z -> figure's front
        if parent: b.parent = eb[parent]; b.use_connect = conn
        return b
    bone("Hips", J['hips'], J['spine'])
    bone("Spine", J['spine'], J['chest'], "Hips", True)
    bone("Chest", J['chest'], J['neck'], "Spine", True)
    bone("Neck", J['neck'], J['head'], "Chest", True)
    bone("Head", J['head'], J['head_top'], "Neck", True)
    for sd in ("L", "R"):
        bone("UpperArm." + sd, J['sh' + sd], J['el' + sd], "Chest")
        bone("ForeArm." + sd, J['el' + sd], J['wr' + sd], "UpperArm." + sd, True)
        bone("Hand." + sd, J['wr' + sd], J['hd' + sd], "ForeArm." + sd, True)
        bone("Thigh." + sd, J['hip' + sd], J['kn' + sd], "Hips")
        bone("Shin." + sd, J['kn' + sd], J['an' + sd], "Thigh." + sd, True)
        bone("Foot." + sd, J['an' + sd], J['toe' + sd], "Shin." + sd, True)
    # foot: roll so local Z points up-ish/front consistently -> align Z to world up for a forward-pointing bone
    for sd in ("L", "R"):
        eb["Foot." + sd].align_roll(Vector((0, 0, 1)))
    bpy.ops.object.mode_set(mode='OBJECT')
    return arm


# ------------------------------------------------------------------ body
def build_body(s, J):
    B = Builder()
    k = J['k']; f = s['female']
    SK, HR, TOP, BOT = "M_Skin", "M_Hair", s['top'], s['bottom']
    H, SP, CH, NK, HD = {"Hips": 1}, {"Spine": 1}, {"Chest": 1}, {"Neck": 1}, {"Head": 1}

    def mix(a, b, t=0.5):
        out = {}
        for kk, v in a.items(): out[kk] = out.get(kk, 0) + v * (1 - t)
        for kk, v in b.items(): out[kk] = out.get(kk, 0) + v * t
        return out

    # torso (crotch -> shoulders), 12 sides; scaled by k
    hipw = 0.185 if f else 0.172
    chw = 0.165 if f else 0.19
    waist = 0.13 if f else 0.152
    T = [  # z, rx, ry, cy, weights, band material
        (0.80, 0.10, 0.075, 0.0, H, BOT),
        (0.87, hipw - 0.01, 0.110, 0.0, H, BOT),
        (0.94, hipw, 0.118, 0.0, H, BOT),
        (1.01, hipw - 0.015, 0.110, 0.0, mix(H, SP, 0.3), "M_Belt"),
        (1.035, hipw - 0.02, 0.108, 0.0, mix(H, SP, 0.4), TOP),
        (1.09, waist, 0.100, 0.0, SP, TOP),
        (1.17, waist + 0.012, 0.108, -0.005, mix(SP, CH), TOP),
        (1.26, chw - 0.005, 0.120 if not f else 0.128, -0.01 if not f else -0.016, CH, TOP),
        (1.34, chw, 0.118, -0.005, CH, TOP),
        (1.40, chw - 0.02, 0.105, 0.0, CH, TOP),
        (1.445, 0.085, 0.075, 0.0, CH, TOP),
    ]
    B.loft([zr(z * k, rx * k, ry * k, w, m, cy=cy * k) for z, rx, ry, cy, w, m in T], 12)

    # neck
    nk = [(1.40, 0.058, CH), (1.47, 0.055, mix(CH, NK)), (1.52, 0.053, NK), (1.575, 0.05, HD)]
    B.loft([zr(z * k, r * k, r * k, w, SK, cy=0.005 * k) for z, r, w in nk], 8, cap_start=False, cap_end=False)

    # head, 12 sides
    hs = 0.95 if f else 1.0
    HDR = [(1.535, 0.035, 0.030, -0.035), (1.56, 0.060, 0.072, -0.018), (1.60, 0.075, 0.095, 0.0),
           (1.65, 0.081, 0.100, 0.002), (1.70, 0.077, 0.095, 0.006), (1.735, 0.055, 0.070, 0.01), (1.752, 0.02, 0.025, 0.01)]
    B.loft([zr(z * k, rx * k * hs, ry * k * hs, HD, SK, cy=cy * k) for z, rx, ry, cy in HDR], 12)
    # hair cap (open bottom): follows the skull, a little bigger, hairline set back at the front
    hz0 = 1.655 if not f else 1.62
    HC = [(hz0, 0.088, 0.106, 0.012), (1.70, 0.085, 0.104, 0.010), (1.738, 0.062, 0.080, 0.012), (1.762, 0.022, 0.03, 0.012)]
    B.loft([zr(z * k, rx * k * hs, ry * k * hs, HD, HR, cy=cy * k) for z, rx, ry, cy in HC], 12, cap_start=False)
    # back of the head hair (covers the nape)
    B.loft([zr(z * k, rx * k * hs, ry * k * hs, HD, HR, cy=cy * k) for z, rx, ry, cy in
            [(1.585, 0.07, 0.05, 0.05), (1.62, 0.082, 0.07, 0.04), (hz0 + 0.01, 0.089, 0.085, 0.03)]], 12)
    if s.get('ponytail'):
        c0 = Vector((0, 0.10, 1.70)) * k
        pts = [(c0, 0.035), (c0 + Vector((0, 0.04, -0.06)) * k, 0.04), (c0 + Vector((0, 0.05, -0.16)) * k, 0.03),
               (c0 + Vector((0, 0.04, -0.26)) * k, 0.012)]
        B.loft([dict(c=p, u=(1, 0, 0), v=(0, 1, 0), rx=r, ry=r * 0.8, w=HD, m=HR) for p, r in pts], 8)
    # face: nose, eyes, ears
    nz = 1.625 * k
    fy = -0.100 * k * hs
    B.add_face([B.add_vert((0, fy - 0.025 * k, nz - 0.005 * k), HD), B.add_vert((0.014 * k, fy + 0.002, nz - 0.02 * k), HD),
                B.add_vert((0, fy + 0.002, nz + 0.025 * k), HD)], SK)
    B.add_face([B.add_vert((0, fy - 0.025 * k, nz - 0.005 * k), HD), B.add_vert((0, fy + 0.002, nz + 0.025 * k), HD),
                B.add_vert((-0.014 * k, fy + 0.002, nz - 0.02 * k), HD)], SK)
    B.add_face([B.add_vert((0, fy - 0.025 * k, nz - 0.005 * k), HD), B.add_vert((-0.014 * k, fy + 0.002, nz - 0.02 * k), HD),
                B.add_vert((0.014 * k, fy + 0.002, nz - 0.02 * k), HD)], SK)
    for sx in (1, -1):
        B.box((sx * 0.032 * k * hs, fy + 0.008 * k, 1.655 * k), (0.008 * k, 0.004 * k, 0.0045 * k), HD, "M_Eyes")
        B.box((sx * 0.032 * k * hs, fy + 0.006 * k, 1.671 * k), (0.014 * k, 0.004 * k, 0.002 * k), HD, HR)  # brow
        B.sphere((sx * 0.080 * k * hs, 0.005 * k, 1.635 * k), 0.022 * k, HD, SK, seg=6, rings=4, scale=(0.4, 0.8, 1.2))
    B.box((0, fy + 0.012 * k, 1.585 * k), (0.022 * k, 0.006 * k, 0.003 * k), HD, "M_Lips")

    # arms
    for sd, sx in (("L", 1), ("R", -1)):
        ua, fa, ha = J['arm_len' + sd]; ua *= k; fa *= k; ha *= k
        S = J['sh' + sd]; d = J['ad' + sd]
        u = Vector((A45, 0, sx * A45)); v = Vector((0, 1, 0))
        UA, FA, HN = {"UpperArm." + sd: 1}, {"ForeArm." + sd: 1}, {"Hand." + sd: 1}
        sl = s['sleeve']
        upr = 0.046 if f else 0.052
        R = []

        def ar(t, r, w, m, ry=None, **kw):
            dd = dict(c=S + d * (t * k), u=u, v=v, rx=r * k, ry=(ry or r) * k, w=w, m=m); dd.update(kw); return dd
        if sl:
            R += [ar(-0.03, upr, UA, TOP), ar(0.06, upr, UA, TOP), ar(sl, upr - 0.004, UA, TOP),
                  ar(sl, upr - 0.012, UA, SK)]
        else:
            R += [ar(-0.03, upr - 0.008, UA, SK), ar(0.06, upr - 0.008, UA, SK)]
        R += [ar(ua / k - 0.06, 0.042 if not f else 0.035, UA, SK),
              ar(ua / k, 0.037 if not f else 0.031, mix(UA, FA), SK),
              ar(ua / k + 0.05, 0.037 if not f else 0.031, FA, SK),
              ar(ua / k + 0.14, 0.032 if not f else 0.027, FA, SK),
              ar(ua / k + fa / k - 0.01, 0.025 if not f else 0.021, mix(FA, HN, 0.3), SK, ry=0.030 if not f else 0.026)]
        B.loft(R, 8)
        # hand (flat, wide front-to-back)
        hw = 0.9 if f else 1.0
        t0 = ua / k + fa / k
        H_ = [ar(t0 - 0.015, 0.020 * hw, HN, SK, ry=0.032 * hw), ar(t0 + 0.06, 0.022 * hw, HN, SK, ry=0.046 * hw),
              ar(t0 + 0.13, 0.018 * hw, HN, SK, ry=0.042 * hw), ar(t0 + ha / k, 0.010 * hw, HN, SK, ry=0.026 * hw)]
        B.loft(H_, 8)
        # thumb
        tb = S + d * ((t0 + 0.04) * k) + v * (-0.035 * k * hw) + u * (0.006 * k)
        B.loft([dict(c=tb, u=u, v=d, rx=0.012 * k, ry=0.012 * k, w=HN, m=SK),
                dict(c=tb + (d * 0.05 + v * -0.02) * k, u=u, v=d, rx=0.009 * k, ry=0.009 * k, w=HN, m=SK)], 6)
        # shoulder ball (covers the joint when the arm is raised)
        B.sphere(S + Vector((-sx * 0.006, 0, 0.0)) * 1.0, ((upr + 0.002) if sl else (upr - 0.006)) * k, UA, TOP if sl else SK, seg=10, rings=6)

    # legs
    for sd, sx in (("L", 1), ("R", -1)):
        TH, SN, FT = {"Thigh." + sd: 1}, {"Shin." + sd: 1}, {"Foot." + sd: 1}
        x = J['hip' + sd].x
        cov = s['legs_covered']
        LEG = BOT if cov else SK
        th = 0.090 if f else 0.088
        L = [(0.95, th, TH, BOT), (0.86, th, TH, BOT), (0.76, th - 0.012, TH, BOT if cov else BOT)]
        if not cov:   # shorts end above mid-thigh: hem
            L = [(0.95, th + 0.004, TH, BOT), (0.86, th + 0.004, TH, BOT), (0.80, th - 0.002, TH, BOT),
                 (0.80, th - 0.010, TH, SK)]
        L += [(0.62, 0.064, TH, LEG), (0.535, 0.055, TH, LEG), (0.50, 0.054, mix(TH, SN), LEG),
              (0.46, 0.053, SN, LEG), (0.34, 0.058 if not f else 0.052, SN, LEG), (0.20, 0.042, SN, LEG),
              (0.085, 0.037, SN, LEG)]
        if not cov:
            L = [l if l[3] == BOT or l[3] == SK else (l[0], l[1], l[2], SK) for l in L]
            L = [(z, r * (0.93 if z < 0.75 else 1.0), w, m) for z, r, w, m in L]
        B.loft([zr(z * k, r * k, r * k, w, m, cx=x, cy=(-0.008 if 0.4 < z < 0.6 else 0.0) * k) for z, r, w, m in L], 8)
        # hip ball (thigh pivots inside it when seated)
        B.sphere(J['hip' + sd], (th + 0.002) * k, TH, BOT, seg=10, rings=6)
        # knee ball
        B.sphere(J['kn' + sd], (0.051 if cov else 0.047) * k, SN, LEG, seg=8, rings=5)
        # shoe: loft heel->toe along -Y, flat sole at z=0
        fw = 0.92 if f else 1.0
        SH = s['shoes']
        S_ = [(0.070, 0.040, 0.040, 0.050), (0.020, 0.048, 0.058, 0.060), (-0.060, 0.050, 0.045, 0.045),
              (-0.150, 0.046, 0.032, 0.032), (-0.195, 0.030, 0.022, 0.024)]
        rings = [dict(c=(x, y * k * fw, cz * k), u=(1, 0, 0), v=(0, 0, 1), rx=rx * k * fw, ry=rz * k, w=FT, m=SH)
                 for y, rx, rz, cz in S_]
        B.loft(rings, 10, zmin=0.0)
        # sole slab
        if s['sole'] != SH:   # sole: same outline as the shoe, slightly wider, 1.6 cm tall
            B.loft([dict(c=(x, y * k * fw, 0.0), u=(1, 0, 0), v=(0, 0, 1), rx=rx * 1.06 * k * fw, ry=0.016 * k, w=FT, m=s['sole']) for y, rx, rz, cz in S_], 10, zmin=0.0)

    # extras
    if s.get('bow_tie'):
        c = Vector((0, -0.070, 1.432)) * k
        for sx in (1, -1):
            a = B.add_vert(c, NK); b1 = B.add_vert(c + Vector((sx * 0.052, -0.006, 0.024)) * k, NK)
            b2 = B.add_vert(c + Vector((sx * 0.052, -0.006, -0.024)) * k, NK)
            b3 = B.add_vert(c + Vector((sx * 0.046, 0.012, 0.0)) * k, NK)
            B.add_face([a, b1, b2], "M_BowTie"); B.add_face([a, b3, b1], "M_BowTie")
            B.add_face([a, b2, b3], "M_BowTie"); B.add_face([b1, b3, b2], "M_BowTie")
        B.box(c + Vector((0, -0.006, 0)) * k, (0.012 * k, 0.008 * k, 0.013 * k), NK, "M_BowTie")
    if s.get('bow_tie') or s.get('collar'):
        # shirt / polo collar band
        B.loft([zr(1.415 * k, 0.064 * k, 0.064 * k, CH, TOP, cy=0.004 * k), zr(1.455 * k, 0.060 * k, 0.060 * k, mix(CH, NK), TOP, cy=0.004 * k)],
               8, cap_start=False, cap_end=False)
    if s.get('towel'):
        # strip over the left shoulder: front -> top -> back, along a path in the YZ plane at x = 0.135
        P = [(-0.13, 1.08), (-0.135, 1.22), (-0.128, 1.33), (-0.10, 1.415), (-0.045, 1.465), (0.02, 1.475),
             (0.08, 1.445), (0.118, 1.38), (0.130, 1.25), (0.126, 1.12)]
        rings = []
        for i, (y, z) in enumerate(P):
            a = Vector((0, *P[max(i - 1, 0)])); b = Vector((0, *P[min(i + 1, len(P) - 1)]))
            t = (b - a).normalized(); nrm = Vector((0, -t.z, t.y))   # in-plane normal
            rings.append(dict(c=Vector((0.14, y, z)) * k, u=(1, 0, 0), v=nrm, rx=0.075 * k, ry=0.010 * k, w=CH, m="M_Towel"))
        B.loft(rings, 4)
    return B


def materials(s):
    out = {"M_Skin": (s['skin'], 0.6), "M_Hair": (s['hair'], 0.8), "M_Eyes": ((0.03, 0.03, 0.035), 0.3),
           "M_Lips": (tuple(c * 0.75 for c in s['skin']), 0.6)}
    out.update(s['cols'])
    return out


def make_material(name, col, rough):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    lin = tuple(c ** 2.2 for c in col)  # authored in sRGB-ish; base colour is linear
    bsdf.inputs["Base Color"].default_value = (*lin, 1)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = 0.0
    m.diffuse_color = (*lin, 1)
    return m


def to_mesh(B, name, matdefs):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in B.verts], [], B.faces)
    me.update()
    for mn in B.mats:
        col, r = matdefs[mn]; me.materials.append(make_material(mn, col, r))
    for p, mi in zip(me.polygons, B.fmat): p.material_index = mi
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    for i, w in enumerate(B.weights):
        for bn, val in w.items():
            if val <= 0: continue
            vg = ob.vertex_groups.get(bn) or ob.vertex_groups.new(name=bn)
            vg.add([i], val, 'ADD')
    # weld coincident verts inside each island, make normals consistent, flat shade
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    for p in me.polygons: p.use_smooth = False
    return ob


def make_card(arm, J):
    """Card: 0.50 x 0.35 board held by its bottom edge in the right hand, in the plane of the hand bone's X/Y,
    front face toward the bone's +Z (figure's front at rest and after a sideways raise)."""
    W, Hh, T = 0.50, 0.35, 0.012
    y0 = 0.115      # bottom edge this far along Hand.R from the wrist (inside the fingers)
    verts = []
    for z in (-T / 2, T / 2):
        for y in (y0, y0 + Hh):
            for x in (-W / 2, W / 2):
                verts.append((x, y, z))
    # index: z-,y0,x- 0 | z-,y0,x+ 1 | z-,y1,x- 2 | z-,y1,x+ 3 | z+,... 4..7
    faces = [(4, 5, 7, 6),   # front (+Z)
             (0, 2, 3, 1),   # back
             (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
    me = bpy.data.meshes.new("Card")
    me.from_pydata(verts, [], faces); me.update()
    me.materials.append(make_material("M_Card", (1, 1, 1), 0.5))
    me.materials.append(make_material("M_CardBack", (0.92, 0.92, 0.90), 0.6))
    for i, p in enumerate(me.polygons): p.material_index = 0 if i == 0 else 1; p.use_smooth = False
    uv = me.uv_layers.new(name="UVMap")
    for p in me.polygons:
        for li in p.loop_indices:
            vi = me.loops[li].vertex_index
            x, y, z = verts[vi]
            if p.index == 0:
                uv.data[li].uv = ((x + W / 2) / W, (y - y0) / Hh)
            else:
                uv.data[li].uv = (0.0, 0.0)
    ob = bpy.data.objects.new("Card", me)
    bpy.context.scene.collection.objects.link(ob)
    # place in the Hand.R bone's rest frame, then parent to the bone keeping that placement
    hb = arm.data.bones["Hand.R"]
    world = arm.matrix_world @ hb.matrix_local      # bone head frame, Y along the bone
    ob.parent = arm; ob.parent_type = 'BONE'; ob.parent_bone = "Hand.R"
    bpy.context.view_layer.update()
    ob.matrix_world = world
    bpy.context.view_layer.update()
    return ob


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def build(kind):
    clear_scene()
    s = spec(kind)
    J = joints(s)
    name = "SK_" + kind
    arm = make_armature(name, J)
    B = build_body(s, J)
    body = to_mesh(B, "Body", materials(s))
    body.parent = arm
    mod = body.modifiers.new("Armature", 'ARMATURE'); mod.object = arm
    card = make_card(arm, J) if s.get('card') else None
    tris = sum(len(p.vertices) - 2 for p in body.data.polygons)
    if card: tris += sum(len(p.vertices) - 2 for p in card.data.polygons)
    # export
    bpy.ops.object.select_all(action='DESELECT')
    for o in (arm, body, card):
        if o: o.select_set(True)
    bpy.context.view_layer.objects.active = arm
    glb = os.path.join(GLB_DIR, name + ".glb")
    bpy.ops.export_scene.gltf(filepath=glb, use_selection=True, export_format='GLB', export_yup=True,
                              export_apply=False, export_animations=False, export_skins=True,
                              export_materials='EXPORT', export_texcoords=True, export_normals=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(RAW_DIR, name + ".blend"))
    print("BUILT %s tris=%d verts=%d -> %s" % (name, tris, len(body.data.vertices), glb))


for kind in ("Referee", "Cornerman", "RingCardWalker"):
    build(kind)
