# Packs the row EXRs from build_crowd.py into the impostor atlases and renders the stand preview.
#
#   blender -b --factory-startup --python assemble_atlas.py -- <render_dir>
#
# Writes (next to this scripts/ folder):
#   T_Crowd_Albedo.png  sRGB RGB albedo, A = coverage (straight alpha)
#   T_Crowd_Normal.png  linear, impostor/view-space normal * 0.5 + 0.5 (x = +u, y = +v, z = out of the quad, towards the viewer), A = coverage
#   T_Crowd_Mask.png    linear, R = recolour (garment), G = skin, B = light spot (hands; the phone in the phone rows), A = ambient occlusion
#   crowd_stand_preview.png
import bpy, os, sys, math
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
RENDER_DIR = argv[0] if argv else os.path.join(ROOT, "renders")

CW, CH, COLS, ROWS = 256, 512, 16, 8
W, Hh = CW * COLS, CH * ROWS
ROWNAMES = [("seated", "front"), ("seated", "noseL"), ("seated", "noseR"),
            ("cheer", "front"), ("cheer", "noseL"), ("cheer", "noseR"),
            ("phone", "front"), ("phone", "noseR")]


def load(path):
    img = bpy.data.images.load(path)
    w, h = img.size
    a = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(a)
    bpy.data.images.remove(img)
    return a.reshape(h, w, 4)     # row 0 = bottom


def save(path, arr, srgb):
    h, w, _ = arr.shape
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=True, float_buffer=False)
    img.colorspace_settings.name = "sRGB" if srgb else "Non-Color"
    img.alpha_mode = "STRAIGHT"
    img.pixels.foreach_set(np.clip(arr, 0, 1).astype(np.float32).ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def to_srgb(x):
    x = np.clip(x, 0, 1)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1 / 2.4) - 0.055)


def unpremul(px):
    a = px[..., 3:4]
    rgb = np.where(a > 1e-4, px[..., :3] / np.maximum(a, 1e-4), 0)
    return rgb, a


def dilate(rgb, a, iters=12):
    """Push colours into the transparent pixels so bilinear/mip sampling has no dark fringe."""
    rgb = rgb.copy()
    filled = (a[..., 0] > 0.0)
    for _ in range(iters):
        acc = np.zeros_like(rgb); cnt = np.zeros(filled.shape, np.float32)
        for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
            f = np.roll(filled, (dy, dx), (0, 1))
            acc += np.roll(rgb, (dy, dx), (0, 1)) * f[..., None]
            cnt += f
        grow = (~filled) & (cnt > 0)
        rgb[grow] = acc[grow] / cnt[grow][:, None]
        filled = filled | grow
    return rgb


albedo = np.zeros((Hh, W, 4), np.float32)
normal = np.zeros((Hh, W, 4), np.float32)
normal[..., :3] = (0.5, 0.5, 1.0)
mask = np.zeros((Hh, W, 4), np.float32)
mask[..., 3] = 1.0

for ri, (pose, view) in enumerate(ROWNAMES):
    base = os.path.join(RENDER_DIR, f"row{ri}_{pose}_{view}_")
    if not os.path.exists(base + "albedo.exr"):
        print("missing row", ri); continue
    y0 = Hh - (ri + 1) * CH            # PNG row ri counted from the top; numpy row 0 = bottom
    sl = slice(y0, y0 + CH)
    rgb, a = unpremul(load(base + "albedo.exr"))
    albedo[sl, :, :3] = to_srgb(rgb); albedo[sl, :, 3:] = a
    if os.path.exists(base + "normal.exr"):
        n, _ = unpremul(load(base + "normal.exr"))
        v = n * 2 - 1
        v /= np.maximum(np.linalg.norm(v, axis=-1, keepdims=True), 1e-6)
        v[a[..., 0] <= 0] = (0, 0, 1)
        normal[sl, :, :3] = v * 0.5 + 0.5; normal[sl, :, 3:] = a
    if os.path.exists(base + "mask.exr"):
        m, _ = unpremul(load(base + "mask.exr"))
        mask[sl, :, :3] = m
    if os.path.exists(base + "ao.exr"):
        o, _ = unpremul(load(base + "ao.exr"))
        mask[sl, :, 3] = np.where(a[..., 0] > 0, o[..., 0], 1.0)
    print("row", ri, pose, view, "coverage px", int((a > 0.5).sum()))

cov = albedo[..., 3:4]
albedo[..., :3] = dilate(albedo[..., :3], cov)
normal[..., :3] = dilate(normal[..., :3], cov)
mask[..., :3] = dilate(mask[..., :3], cov, iters=4)

save(os.path.join(ROOT, "T_Crowd_Albedo.png"), albedo, True)
save(os.path.join(ROOT, "T_Crowd_Normal.png"), normal, False)
save(os.path.join(ROOT, "T_Crowd_Mask.png"), mask, False)
print("atlases written")

# ---------------------------------------------------------------- stand preview (emulates the suggested shader)
def lin(x):
    x = np.clip(x, 0, 1)
    return np.where(x <= 0.04045, x / 12.92, np.power((x + 0.055) / 1.055, 2.4))

ALB = lin(albedo[..., :3]); COV = albedo[..., 3]; NRM = normal[..., :3] * 2 - 1; MSK = mask

def cell(person, row):
    y0 = Hh - (row + 1) * CH
    s = (slice(y0, y0 + CH), slice(person * CW, (person + 1) * CW))
    return ALB[s], COV[s], NRM[s], MSK[s]

def resize(arr, f):
    """Box-filter downscale by f (<=1) via area averaging on an integer-supersampled grid."""
    h, w = arr.shape[:2]
    nh, nw = max(1, int(round(h * f))), max(1, int(round(w * f)))
    yi = (np.arange(nh + 1) * h / nh).astype(int); xi = (np.arange(nw + 1) * w / nw).astype(int)
    c = np.cumsum(np.cumsum(np.pad(arr, ((1, 0), (1, 0)) + ((0, 0),) * (arr.ndim - 2)), 0), 1)
    tot = c[yi[1:]][:, xi[1:]] - c[yi[:-1]][:, xi[1:]] - c[yi[1:]][:, xi[:-1]] + c[yi[:-1]][:, xi[:-1]]
    area = ((yi[1:] - yi[:-1])[:, None] * (xi[1:] - xi[:-1])[None, :]).astype(np.float32)
    return tot / (area[..., None] if arr.ndim == 3 else area)

rng = np.random.default_rng(7)
PW, PH = 2048, 1152
canvas = np.zeros((PH, PW, 3), np.float32)
canvas[:] = lin(np.array([0.05, 0.05, 0.07]))
PALETTE = lin(np.array([[0.89, 0.24, 0.24], [0.20, 0.49, 0.94], [0.15, 0.15, 0.17], [0.85, 0.85, 0.82], [0.95, 0.75, 0.15],
                        [0.25, 0.55, 0.30], [0.55, 0.20, 0.55], [0.35, 0.35, 0.38], [0.90, 0.45, 0.15], [0.20, 0.25, 0.45]]))
L = np.array([0.25, 0.55, 0.80]); L /= np.linalg.norm(L)      # house light: above and in front (impostor space)
PXM = 222.6     # atlas px per metre

# rows from the back (far, small, high on screen) to the front (near, big, low on screen)
tiers = [  # px per metre on the preview, baseline y (from top), stand fraction
    (70, 300, 0.30), (85, 420, 0.25), (105, 560, 0.20), (130, 720, 0.15), (165, 900, 0.10), (210, 1120, 0.05)]
for scale, base_y, standing_share in tiers:
    f = scale / PXM
    spacing = 0.62 * scale
    x = -spacing * 0.5 + rng.uniform(0, spacing * 0.5)
    while x < PW + spacing:
        person = int(rng.integers(0, 16))
        stand = rng.random() < standing_share
        phone = (not stand) and rng.random() < 0.05
        yaw = (x / PW - 0.5) * 2          # left of frame sees figures turned to image right, etc.
        view = 0 if abs(yaw) < 0.33 else (2 if yaw < 0 else 1)
        if phone:
            row = 6 if view == 0 else 7
            mirror = view == 1
        else:
            row = (3 if stand else 0) + view
            mirror = False
        a_, c_, n_, m_ = cell(person, row)
        if mirror:
            a_, c_, n_, m_ = a_[:, ::-1], c_[:, ::-1], n_[:, ::-1].copy(), m_[:, ::-1]
            n_[..., 0] *= -1
        tint = PALETTE[int(rng.integers(0, len(PALETTE)))]
        col = a_ * (1 - m_[..., 0:1] + m_[..., 0:1] * tint)
        ndl = np.clip((n_ * L).sum(-1), 0, 1)
        light = (0.30 * m_[..., 3] + 0.95 * ndl * (0.5 + 0.5 * m_[..., 3]))[..., None]
        shade = col * light * (1.0 - 0.35 * (1 - (scale - 70) / 140))   # far rows a touch darker
        if phone:
            shade += m_[..., 2:3] * np.array([0.85, 0.92, 1.0]) * 2.0
        sh = resize(np.concatenate([shade, c_[..., None]], -1), f)
        h, w = sh.shape[:2]
        # pivot: cell (0.5, 0.2174) sits at (x, base_y); 'bounce' a standing figure up a little
        px = int(x - w / 2); py = int(base_y - h * (1 - 0.2174))
        ys, xs = max(0, py), max(0, px)
        ye, xe = min(PH, py + h), min(PW, px + w)
        if ye > ys and xe > xs:
            sub = sh[::-1][ys - py:ye - py, xs - px:xe - px]   # flip: numpy row 0 = bottom
            al = sub[..., 3:4]
            canvas[ys:ye, xs:xe] = canvas[ys:ye, xs:xe] * (1 - al) + sub[..., :3] * al
        x += spacing * rng.uniform(0.85, 1.15)
    # the seat-back row in front of this tier hides legs: a dark rail
    rail_top = int(base_y + 0.05 * scale)
    canvas[rail_top:rail_top + int(0.42 * scale)] = lin(np.array([0.07, 0.07, 0.09]))

out = np.concatenate([to_srgb(canvas), np.ones((PH, PW, 1), np.float32)], -1)[::-1]   # to bottom-up for Blender
save(os.path.join(ROOT, "crowd_stand_preview.png"), out, True)
print("preview written")
