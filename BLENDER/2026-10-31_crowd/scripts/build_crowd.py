# Builds 16 low-poly spectators in three poses (seated, standing arms up, standing phone up), saves
# crowd_people.blend and renders the impostor rows (albedo, normal, mask, AO) as linear EXRs.
#
#   blender -b --factory-startup --python build_crowd.py -- <render_dir> [--no-render]
#
# Units: metres. Blender axes: Z up, each figure faces -Y (towards the render camera), figure's left = +X.
# Origin of every figure = its pivot: the seat surface centre (z = 0). The floor the feet stand on is z = -0.46.
import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
RENDER_DIR = argv[0] if argv else os.path.join(ROOT, "renders")
DO_RENDER = "--no-render" not in argv

FLOOR = -0.46          # floor below the seat surface
CELL_W, CELL_H = 1.15, 2.30
CELL_BOTTOM = -0.50    # cell covers z in [-0.50, 1.80] relative to the pivot
NPEOPLE = 16
POSES = ["seated", "cheer", "phone"]
VIEWS = {"front": 0.0, "noseL": -40.0, "noseR": 40.0}   # yaw (deg) about Z; noseL = nose points to image left

def lin(c):
    def f(x):
        return x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4
    return (f(c[0]), f(c[1]), f(c[2]))

# ---------------------------------------------------------------- palette (sRGB)
SKIN = {
    "pale": (0.96, 0.80, 0.70), "light": (0.92, 0.72, 0.58), "rosy": (0.93, 0.74, 0.64),
    "eastasian": (0.92, 0.77, 0.60), "medium": (0.80, 0.58, 0.42), "olive": (0.74, 0.56, 0.40),
    "tan": (0.68, 0.48, 0.33), "brown": (0.55, 0.36, 0.24), "dark": (0.40, 0.26, 0.17), "deep": (0.29, 0.19, 0.13),
}
HAIR = {
    "black": (0.06, 0.05, 0.045), "darkbrown": (0.17, 0.10, 0.06), "brown": (0.32, 0.20, 0.11),
    "blonde": (0.78, 0.63, 0.38), "red": (0.58, 0.22, 0.09), "grey": (0.62, 0.62, 0.62), "white": (0.86, 0.86, 0.84),
}
BOTTOM = {"jeans": (0.22, 0.30, 0.46), "black": (0.09, 0.09, 0.10), "khaki": (0.62, 0.55, 0.40),
          "grey": (0.40, 0.40, 0.42), "navy": (0.12, 0.15, 0.26)}
SHOE = {"white": (0.88, 0.88, 0.86), "black": (0.08, 0.08, 0.08), "brown": (0.30, 0.19, 0.11)}
TOP = (1.0, 1.0, 1.0)          # recolourable garment: neutral white, tinted in the shader
TOP_ACC = (0.86, 0.86, 0.86)   # seams, pockets, hood lining: still recolourable, darker

# id, sex, height, build, skin, hair style, hair colour, top, bottom, shoes, extras
PEOPLE = [
    dict(name="P00_Dan",    sex="m", h=1.80, b=1.00, skin="light",     hair="short",   hc="brown",     top="tee",      bot="jeans", shoe="white", x=[]),
    dict(name="P01_Maya",   sex="f", h=1.65, b=0.90, skin="tan",       hair="long",    hc="black",     top="hoodie",   bot="black", shoe="white", x=[]),
    dict(name="P02_Marcus", sex="m", h=1.86, b=1.28, skin="dark",      hair="buzz",    hc="black",     top="jacket",   bot="khaki", shoe="black", x=["beard"], inner=(0.92, 0.92, 0.90)),
    dict(name="P03_Emma",   sex="f", h=1.62, b=0.95, skin="pale",      hair="ponytail",hc="blonde",    top="long",     bot="jeans", shoe="white", x=[]),
    dict(name="P04_Ken",    sex="m", h=1.74, b=1.00, skin="eastasian", hair="short",   hc="black",     top="tee",      bot="grey",  shoe="black", x=["cap"], cap=(0.10, 0.10, 0.12)),
    dict(name="P05_Aisha",  sex="f", h=1.70, b=1.00, skin="brown",     hair="afro",    hc="black",     top="jacket",   bot="jeans", shoe="white", x=[], inner=(0.10, 0.10, 0.10)),
    dict(name="P06_Leo",    sex="m", h=1.78, b=0.88, skin="medium",    hair="short",   hc="darkbrown", top="hoodie",   bot="black", shoe="white", x=["beanie"], cap=(0.55, 0.12, 0.10)),
    dict(name="P07_Rose",   sex="f", h=1.58, b=1.22, skin="rosy",      hair="bun",     hc="grey",      top="cardigan", bot="skirt", shoe="brown", x=["glasses"], inner=(0.85, 0.80, 0.70), skirt=(0.25, 0.22, 0.30)),
    dict(name="P08_Omar",   sex="m", h=1.90, b=1.05, skin="olive",     hair="short",   hc="black",     top="long",     bot="jeans", shoe="black", x=["beard"]),
    dict(name="P09_Zuri",   sex="f", h=1.68, b=0.88, skin="deep",      hair="crop",    hc="black",     top="tee",      bot="jeans", shoe="white", x=[]),
    dict(name="P10_Frank",  sex="m", h=1.72, b=1.30, skin="light",     hair="bald",    hc="grey",      top="polo",     bot="khaki", shoe="brown", x=["belly"]),
    dict(name="P11_Lucia",  sex="f", h=1.66, b=1.00, skin="tan",       hair="bob",     hc="darkbrown", top="hoodie",   bot="jeans", shoe="white", x=[]),
    dict(name="P12_Andre",  sex="m", h=1.80, b=1.08, skin="deep",      hair="buzz",    hc="black",     top="jacket",   bot="black", shoe="white", x=["cap", "capback", "sunglasses"], cap=(0.85, 0.85, 0.82), inner=(0.12, 0.12, 0.12)),
    dict(name="P13_Ciara",  sex="f", h=1.74, b=0.95, skin="pale",      hair="long",    hc="red",       top="tee",      bot="black", shoe="black", x=[]),
    dict(name="P14_Raj",    sex="m", h=1.68, b=0.90, skin="brown",     hair="curly",   hc="black",     top="polo",     bot="jeans", shoe="brown", x=["glasses"]),
    dict(name="P15_Mei",    sex="f", h=1.60, b=0.95, skin="eastasian", hair="bob",     hc="black",     top="long",     bot="khaki", shoe="white", x=["bangs", "cap"], cap=(0.12, 0.20, 0.45)),
]

# Mask (R recolour, G skin, B light spot) per part kind, before AO.
KIND_MASK = {"top": (1, 0, 0), "skin": (0, 1, 0), "hand": (0, 1, 1), "fixed": (0, 0, 0), "phone": (0, 0, 1)}

# ---------------------------------------------------------------- geometry
class Builder:
    def __init__(self):
        self.bm = bmesh.new()
        self.alb = self.bm.loops.layers.float_color.new("alb")
        self.msk = self.bm.loops.layers.float_color.new("msk")

    def _finish(self, verts, colour, kind):
        faces = set()
        for v in verts:
            faces.update(v.link_faces)
        c = lin(colour) + (1.0,)
        m = KIND_MASK[kind] + (1.0,)
        for f in faces:
            f.smooth = True
            for l in f.loops:
                l[self.alb] = c
                l[self.msk] = m

    def capsule(self, a, b, r1, r2, colour, kind, fx=1.0, fy=1.0, cap=(1.0, 1.0), side=(1, 0, 0), segs=16, rings=10):
        a, b = Vector(a), Vector(b)
        d = b - a
        L = d.length
        d = d.normalized() if L > 1e-6 else Vector((0, 0, 1))
        s = Vector(side) - d * d.dot(Vector(side))
        if s.length < 1e-4:
            s = Vector((0, 1, 0)) - d * d.y
        s.normalize()
        t = d.cross(s)
        R = Matrix((s, t, d)).transposed()
        ret = bmesh.ops.create_uvsphere(self.bm, u_segments=segs, v_segments=rings, radius=1.0)
        for v in ret["verts"]:
            x, y, z = v.co
            if z >= -1e-6:
                r = r2; z2 = z * r2 * cap[1] + L
            else:
                r = r1; z2 = z * r1 * cap[0]
            v.co = a + R @ Vector((x * r * fx, y * r * fy, z2))
        self._finish(ret["verts"], colour, kind)

    def ellipsoid(self, c, radii, colour, kind, rot=None, half=False, segs=16, rings=10):
        c = Vector(c)
        ret = bmesh.ops.create_uvsphere(self.bm, u_segments=segs, v_segments=rings, radius=1.0)
        R = rot if rot is not None else Matrix.Identity(3)
        for v in ret["verts"]:
            x, y, z = v.co
            if half and z < 0:
                z = 0.0
            v.co = c + R @ Vector((x * radii[0], y * radii[1], z * radii[2]))
        self._finish(ret["verts"], colour, kind)

    def mesh(self, name):
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        return me


def rotx(deg):
    return Matrix.Rotation(math.radians(deg), 3, "X")


def build(p, pose, idx):
    B = Builder()
    k = p["h"] / 1.75
    b = p["b"]
    fem = p["sex"] == "f"
    skin, hairc = SKIN[p["skin"]], HAIR[p["hc"]]
    trous = BOTTOM.get(p["bot"], (0.2, 0.2, 0.2))
    shoe = SHOE[p["shoe"]]
    top = p["top"]
    H = p["h"]
    gb = b ** 0.5

    # --- standing joint heights (above floor); seated shifts the upper body so hips sit on the seat
    hipz_stand = FLOOR + 0.50 * H
    if pose == "seated":
        dz = 0.10 - hipz_stand
        dy = 0.03
    else:
        dz = 0.0
        dy = 0.0
    def up(z):          # upper-body height in this pose
        return z + dz

    sw = (0.165 if fem else 0.19) * k * (0.85 + 0.15 * b)
    hw = (0.092 if fem else 0.082) * k * gb
    sz = up(FLOOR + 0.815 * H)
    head_c = Vector((0, dy - 0.005, up(FLOOR + H - 0.118 * k)))
    hrx, hry, hrz = 0.079 * k, 0.096 * k, 0.118 * k

    # --- torso (the top)
    tr1 = (0.135 if fem else 0.140) * k * b
    tr2 = (0.150 if fem else 0.170) * k * (0.6 + 0.4 * b)
    if top in ("jacket", "cardigan", "hoodie"):
        tr1 *= 1.05; tr2 *= 1.05
    ta = Vector((0, dy, up(FLOOR + 0.54 * H)))
    tb = Vector((0, dy, up(FLOOR + 0.775 * H)))
    B.capsule(ta, tb, tr1, tr2, TOP, "top", fy=0.62, cap=(0.5, 0.42))
    # shoulders
    B.capsule((-sw, dy + 0.005, sz - 0.01), (sw, dy + 0.005, sz - 0.01), 0.055 * k * gb, 0.055 * k * gb, TOP, "top", side=(0, 0, 1))
    if fem:
        for s in (-1, 1):
            B.ellipsoid((s * 0.048 * k, dy - tr2 * 0.62 * 0.72, up(FLOOR + 0.715 * H)), (0.048 * k * gb, 0.036 * k * gb, 0.046 * k * gb), TOP, "top")
    if "belly" in p["x"]:
        B.ellipsoid((0, dy - 0.05, up(FLOOR + 0.585 * H)), (0.15 * k * b, 0.11 * b, 0.15 * k), TOP, "top")
    chest_front = dy - tr2 * 0.62
    # top details
    if top in ("jacket", "cardigan"):
        B.ellipsoid((0, chest_front + 0.012, up(FLOOR + 0.66 * H)), (0.045 * k, 0.03, 0.17 * k), p.get("inner", (0.9, 0.9, 0.9)), "fixed")
        for s in (-1, 1):      # lapels / collar
            B.capsule((s * 0.07 * k, dy + 0.0, sz + 0.02), (s * 0.03 * k, chest_front + 0.02, up(FLOOR + 0.72 * H)), 0.018, 0.012, TOP_ACC, "top")
    elif top == "hoodie":
        B.ellipsoid((0, dy + 0.075 * k, sz + 0.025), (0.12 * k, 0.065 * k, 0.07 * k), TOP_ACC, "top")       # hood
        B.ellipsoid((0, chest_front - 0.005 + 0.02, up(FLOOR + 0.58 * H)), (0.11 * k * b, 0.03, 0.065 * k), TOP_ACC, "top")   # pocket
        for s in (-1, 1):
            B.capsule((s * 0.03, chest_front + 0.01, sz - 0.03), (s * 0.032, chest_front - 0.005, sz - 0.14 * k), 0.006, 0.006, (0.92, 0.92, 0.9), "fixed", segs=6, rings=4)
    elif top == "polo":
        for s in (-1, 1):
            B.capsule((s * 0.055 * k, dy + 0.01, sz + 0.035), (s * 0.018, chest_front + 0.035, sz - 0.03), 0.017, 0.012, TOP_ACC, "top")
        B.ellipsoid((0, chest_front + 0.015, sz - 0.07 * k), (0.016, 0.02, 0.06 * k), TOP_ACC, "top")
    else:   # tee / long: a neckline in skin
        B.ellipsoid((0, chest_front + 0.05, sz + 0.03), (0.042 * k, 0.03, 0.028 * k), skin, "skin")

    # --- neck, head
    B.capsule((0, dy + 0.012, sz - 0.02), (0, head_c.y + 0.015, head_c.z - 0.06 * k), 0.048 * k * gb, 0.045 * k, skin, "skin")
    B.ellipsoid(head_c, (hrx, hry, hrz), skin, "skin", segs=14, rings=10)
    B.ellipsoid(head_c + Vector((0, -0.028 * k, -0.06 * k)), (0.062 * k, 0.07 * k, 0.062 * k), skin, "skin")   # jaw
    B.ellipsoid(head_c + Vector((0, -0.094 * k, -0.025 * k)), (0.016 * k, 0.022 * k, 0.028 * k), skin, "skin", segs=8, rings=6)   # nose
    for s in (-1, 1):
        B.ellipsoid(head_c + Vector((s * 0.077 * k, 0.008, -0.01 * k)), (0.016, 0.024, 0.03 * k), skin, "skin", segs=8, rings=6)  # ears
        if "sunglasses" in p["x"]:
            B.ellipsoid(head_c + Vector((s * 0.032 * k, -0.084 * k, 0.012 * k)), (0.026 * k, 0.012, 0.018 * k), (0.03, 0.03, 0.035), "fixed", segs=8, rings=6)
        else:
            B.ellipsoid(head_c + Vector((s * 0.031 * k, -0.083 * k, 0.012 * k)), (0.012 * k, 0.012, 0.010 * k), (0.06, 0.045, 0.04), "fixed", segs=8, rings=6)
            if "glasses" in p["x"]:
                B.ellipsoid(head_c + Vector((s * 0.032 * k, -0.091 * k, 0.012 * k)), (0.024 * k, 0.004, 0.017 * k), (0.12, 0.10, 0.09), "fixed", segs=10, rings=6)
        # brows
        B.capsule(head_c + Vector((s * 0.015 * k, -0.088 * k, 0.034 * k)), head_c + Vector((s * 0.05 * k, -0.08 * k, 0.034 * k)), 0.006, 0.006,
                  hairc if p["hair"] != "bald" else (0.5, 0.5, 0.5), "fixed", segs=6, rings=4)
    if "beard" in p["x"]:
        B.ellipsoid(head_c + Vector((0, -0.045 * k, -0.075 * k)), (0.066 * k, 0.06 * k, 0.05 * k), hairc, "fixed")
        B.ellipsoid(head_c + Vector((0, -0.085 * k, -0.05 * k)), (0.03 * k, 0.012, 0.008), hairc, "fixed", segs=8, rings=4)   # moustache
    # hair
    hs = p["hair"]
    if hs in ("short", "long", "ponytail", "bun", "crop", "bob"):
        B.ellipsoid(head_c + Vector((0, 0.012 * k, 0.022 * k)), (hrx * 1.08, hry * 1.06, hrz * 0.98), hairc, "fixed", segs=14, rings=10)
    if hs == "buzz":
        B.ellipsoid(head_c + Vector((0, 0.008 * k, 0.012 * k)), (hrx * 1.03, hry * 1.03, hrz * 0.97), hairc, "fixed", segs=14, rings=10)
    if hs == "curly":
        B.ellipsoid(head_c + Vector((0, 0.02 * k, 0.035 * k)), (hrx * 1.22, hry * 1.12, hrz * 0.98), hairc, "fixed", segs=14, rings=10)
    if hs == "bald":
        B.ellipsoid(head_c + Vector((0, 0.02 * k, -0.025 * k)), (hrx * 1.07, hry * 1.03, hrz * 0.78), hairc, "fixed", segs=14, rings=10)
    if hs == "long":
        B.ellipsoid(head_c + Vector((0, 0.04 * k, -0.10 * k)), (0.098 * k, 0.062 * k, 0.175 * k), hairc, "fixed")
    if hs == "bob":
        B.ellipsoid(head_c + Vector((0, 0.03 * k, -0.035 * k)), (0.096 * k, 0.07 * k, 0.11 * k), hairc, "fixed")
    if hs == "ponytail":
        B.capsule(head_c + Vector((0, 0.09 * k, 0.03 * k)), head_c + Vector((0, 0.15 * k, -0.17 * k)), 0.035, 0.018, hairc, "fixed")
    if hs == "bun":
        B.ellipsoid(head_c + Vector((0, 0.075 * k, 0.085 * k)), (0.045, 0.045, 0.04), hairc, "fixed")
    if hs == "afro":
        B.ellipsoid(head_c + Vector((0, 0.04 * k, 0.04 * k)), (0.135 * k, 0.122 * k, 0.13 * k), hairc, "fixed", segs=14, rings=10)
    if "bangs" in p["x"]:
        B.ellipsoid(head_c + Vector((0, -0.074 * k, 0.05 * k)), (0.072 * k, 0.03 * k, 0.034 * k), hairc, "fixed")
    if "cap" in p["x"]:
        cc = p.get("cap", (0.1, 0.1, 0.1))
        B.ellipsoid(head_c + Vector((0, 0.005, 0.03 * k)), (hrx * 1.12, hry * 1.1, hrz * 0.85), cc, "fixed", half=True, segs=14, rings=10)
        by = 0.105 * k if "capback" in p["x"] else -0.105 * k
        tilt = -8 if "capback" in p["x"] else 8
        B.ellipsoid(head_c + Vector((0, by, 0.035 * k)), (0.075 * k, 0.065 * k, 0.010), cc, "fixed", rot=rotx(tilt))
    if "beanie" in p["x"]:
        cc = p.get("cap", (0.5, 0.1, 0.1))
        B.ellipsoid(head_c + Vector((0, 0.008, 0.03 * k)), (hrx * 1.12, hry * 1.1, hrz * 1.0), cc, "fixed", half=True, segs=14, rings=10)
        B.capsule(head_c + Vector((-hrx * 1.0, 0.008, 0.04 * k)), head_c + Vector((hrx * 1.0, 0.008, 0.04 * k)), 0.016, 0.016, cc, "fixed", side=(0, 0, 1))

    # --- hips and legs
    hipz = up(hipz_stand)
    B.ellipsoid((0, dy + 0.005, hipz + 0.035), (hw + 0.075 * k * gb, 0.105 * k * b, 0.105 * k), trous if p["bot"] != "skirt" else p["skirt"], "fixed")
    legc = trous if p["bot"] != "skirt" else (0.22, 0.17, 0.15)     # skirt: dark tights
    for s in (-1, 1):
        hip = Vector((s * hw, dy, hipz))
        if pose == "seated":
            knee = Vector((s * hw * 1.08, -0.255 * H + dy, 0.13))
            ankle = Vector((s * hw * 1.0, knee.y + 0.02, FLOOR + 0.06))
        else:
            knee = Vector((s * hw * 0.98, -0.015, FLOOR + 0.27 * H))
            ankle = Vector((s * hw * 0.92, 0.0, FLOOR + 0.06))
        thighc = p["skirt"] if p["bot"] == "skirt" else trous
        B.capsule(hip, knee, 0.088 * k * gb, 0.058 * k * gb, thighc, "fixed")
        B.capsule(knee, ankle, 0.056 * k * gb, 0.04 * k, legc, "fixed")
        B.ellipsoid((ankle.x, ankle.y - 0.05 * k, FLOOR + 0.04), (0.048 * k, 0.125 * k, 0.042), shoe, "fixed")
    if p["bot"] == "skirt" and pose != "seated":
        B.capsule((0, 0, hipz + 0.04), (0, -0.01, FLOOR + 0.30 * H), hw + 0.07, hw + 0.13, p["skirt"], "fixed", fy=0.75, cap=(0.15, 0.4))

    # --- arms
    L1, L2 = 0.172 * H, 0.150 * H
    long_sleeve = top in ("long", "hoodie", "jacket", "cardigan")
    rnd = (idx * 0.618034) % 1.0       # per-person variation
    for s in (-1, 1):                  # s = +1 figure's left (+X), -1 figure's right
        S = Vector((s * (sw - 0.01), dy + 0.005, sz - 0.025))
        if pose == "seated":
            E = S + Vector((s * 0.03, -0.07, -L1 * 0.97))
            T = Vector((s * hw * 1.1, -0.15 * H, 0.20))
            W = E + (T - E).normalized() * L2
            hd = (T - E).normalized()
        elif pose == "cheer":
            style = idx % 4
            a = math.radians(22 + 16 * rnd + (6 if s > 0 else 0))
            fwd = 0.18 + 0.15 * rnd
            d1 = Vector((s * math.sin(a), -fwd, math.cos(a))).normalized()
            E = S + d1 * L1
            if style == 3:    # hands clapping together overhead
                d2 = Vector((-s * 0.55, -0.1, 0.83)).normalized()
            elif style == 1:  # wide V, straight arms
                d2 = Vector((s * math.sin(a * 1.15), -fwd * 0.6, math.cos(a * 1.15))).normalized()
            else:             # bent elbows, fists up
                d2 = Vector((s * 0.08, -0.25, 0.97)).normalized()
            W = E + d2 * L2
            hd = d2
        else:   # phone: figure's right hand holds a phone up and forward, left arm relaxed
            if s < 0:
                d1 = Vector((-0.20, -0.75, 0.62)).normalized()
                E = S + d1 * L1
                d2 = Vector((0.12, -0.35, 0.93)).normalized()
                W = E + d2 * L2
                hd = d2
            else:
                E = S + Vector((s * 0.03, -0.03, -L1 * 0.98))
                W = E + Vector((s * 0.01, -0.06, -L2 * 0.98))
                hd = (W - E).normalized()
        upc = TOP
        B.capsule(S, E, 0.05 * k * gb, 0.042 * k * gb, upc, "top")
        if long_sleeve:
            B.capsule(E, W, 0.043 * k * gb, 0.034 * k, TOP, "top")
        else:
            B.capsule(E, W, 0.038 * k * gb, 0.028 * k, skin, "skin")
            if top in ("tee", "polo"):     # sleeve end
                mid = S + (E - S) * 0.55
                B.capsule(S, mid, 0.056 * k * gb, 0.054 * k * gb, TOP, "top")
        hc = W + hd * 0.045 * k
        B.ellipsoid(hc, (0.034 * k, 0.03 * k, 0.048 * k), skin, "hand" if pose != "phone" else "skin", segs=8, rings=6)
        if pose == "phone" and s < 0:
            # phone in the right hand, its back (and torch) towards the ring (-Y)
            pc = hc + Vector((0.0, -0.035, 0.05))
            B.ellipsoid(pc, (0.038, 0.007, 0.075), (0.05, 0.05, 0.06), "phone", segs=8, rings=4)

    me = B.mesh(f"{p['name']}_{pose}")
    ob = bpy.data.objects.new(f"{p['name']}_{pose}", me)
    return ob


# ---------------------------------------------------------------- scene, material
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

mat = bpy.data.materials.new("M_CrowdImpostorBake")
try:
    mat.use_nodes = True
except Exception:
    pass
nt = mat.node_tree
for n in list(nt.nodes):
    nt.nodes.remove(n)
out = nt.nodes.new("ShaderNodeOutputMaterial")
emit = nt.nodes.new("ShaderNodeEmission")
emit.inputs["Strength"].default_value = 1.0
nt.links.new(emit.outputs["Emission"], out.inputs["Surface"])
alb = nt.nodes.new("ShaderNodeVertexColor"); alb.layer_name = "alb"
msk = nt.nodes.new("ShaderNodeVertexColor"); msk.layer_name = "msk"
geo = nt.nodes.new("ShaderNodeNewGeometry")
vt = nt.nodes.new("ShaderNodeVectorTransform")
vt.vector_type = "NORMAL"; vt.convert_from = "WORLD"; vt.convert_to = "CAMERA"
nt.links.new(geo.outputs["Normal"], vt.inputs["Vector"])
mul = nt.nodes.new("ShaderNodeVectorMath"); mul.operation = "MULTIPLY_ADD"
mul.inputs[1].default_value = (0.5, 0.5, 0.5); mul.inputs[2].default_value = (0.5, 0.5, 0.5)
nt.links.new(vt.outputs["Vector"], mul.inputs[0])
ao = nt.nodes.new("ShaderNodeAmbientOcclusion")
ao.only_local = True
ao.samples = 16
ao.inputs["Distance"].default_value = 0.22
ao.inputs["Color"].default_value = (1, 1, 1, 1)
SRC = {"albedo": alb.outputs["Color"], "normal": mul.outputs["Vector"], "mask": msk.outputs["Color"], "ao": ao.outputs["AO"]}

def set_pass(name):
    for l in list(emit.inputs["Color"].links):
        nt.links.remove(l)
    nt.links.new(SRC[name], emit.inputs["Color"])

world = bpy.data.worlds.new("W_Black")
world.color = (0, 0, 0)
scene.world = world

col = {}
objs = {}
for pose in POSES:
    c = bpy.data.collections.new(f"Crowd_{pose}")
    scene.collection.children.link(c)
    col[pose] = c
    for i, p in enumerate(PEOPLE):
        ob = build(p, pose, i)
        ob.data.materials.append(mat)
        c.objects.link(ob)
        ob["person"] = i
        ob["pose"] = pose
        objs[(i, pose)] = ob

tris = sum(len(o.data.polygons) for o in bpy.data.objects if o.type == "MESH")
print("FACES total", tris, "per figure avg", tris // len(objs))

cam_data = bpy.data.cameras.new("C_Impostor")
cam_data.type = "ORTHO"
cam_data.ortho_scale = CELL_W * NPEOPLE
cam = bpy.data.objects.new("C_Impostor", cam_data)
scene.collection.objects.link(cam)
cam.location = (0, -30, CELL_BOTTOM + CELL_H / 2)
cam.rotation_euler = (math.radians(90), 0, 0)
cam_data.clip_start = 1; cam_data.clip_end = 60
scene.camera = cam

r = scene.render
r.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.samples = 48
scene.cycles.use_adaptive_sampling = False
scene.cycles.use_denoising = False
try:
    scene.cycles.filter_width = 1.2
except Exception:
    pass
r.film_transparent = True
r.resolution_x = 256 * NPEOPLE
r.resolution_y = 512
r.resolution_percentage = 100
scene.view_settings.view_transform = "Standard"
r.image_settings.file_format = "OPEN_EXR"
r.image_settings.color_mode = "RGBA"
r.image_settings.color_depth = "16"
r.image_settings.exr_codec = "ZIP"


def place(pose, yaw):
    for (i, ps), ob in objs.items():
        vis = ps == pose
        ob.hide_render = not vis
        ob.location = (-CELL_W * NPEOPLE / 2 + CELL_W * (i + 0.5), 0, 0)
        ob.rotation_euler = (0, 0, math.radians(yaw))


ROWS = [("seated", "front"), ("seated", "noseL"), ("seated", "noseR"),
        ("cheer", "front"), ("cheer", "noseL"), ("cheer", "noseR"),
        ("phone", "front"), ("phone", "noseR")]

if DO_RENDER:
    os.makedirs(RENDER_DIR, exist_ok=True)
    only = [int(x) for x in os.environ.get("CROWD_ROWS", "").split(",") if x]
    for ri, (pose, view) in enumerate(ROWS):
        if only and ri not in only:
            continue
        place(pose, VIEWS[view])
        for ps in os.environ.get("CROWD_PASSES", "albedo,normal,mask,ao").split(","):
            set_pass(ps)
            r.filepath = os.path.join(RENDER_DIR, f"row{ri}_{pose}_{view}_{ps}.exr")
            bpy.ops.render.render(write_still=True)
            print("RENDERED", r.filepath, flush=True)

# --- leave the .blend as a readable lineup: three rows of people, front view, the impostor camera on row 0
for (i, ps), ob in objs.items():
    ob.hide_render = False
    ob.location = (-CELL_W * NPEOPLE / 2 + CELL_W * (i + 0.5), POSES.index(ps) * 2.5, 0)
    ob.rotation_euler = (0, 0, 0)
set_pass("albedo")
scene["impostor_note"] = "Cell 1.15 x 2.30 m, pivot = seat surface centre (origin), floor z=-0.46; see README.md"
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, "crowd_people.blend"), compress=True)
print("SAVED blend")
