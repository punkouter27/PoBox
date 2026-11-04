# Renders the ring-card faces T_RoundCard_2.png .. T_RoundCard_12.png (512 x 360, fills M_Card's UV 0..1:
# image left = card's left as seen from the front, image top = card top). Then checks one on the posed
# SK_RingCardWalker (card-up pose) so the orientation on the model is visible.
# Run:  "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe" --background --python render_round_cards.py
import bpy, os, math
from mathutils import Vector

ROOT = r"C:\Users\punko\Downloads\PoBox"
OUT = os.path.join(ROOT, "Assets", "Art", "Exports", "Textures")
SHOT = os.path.join(ROOT, "SCREENSHOTS", "2026-11-03-figures")
os.makedirs(OUT, exist_ok=True)
FONT = r"C:\Windows\Fonts\ariblk.ttf" if os.path.exists(r"C:\Windows\Fonts\ariblk.ttf") else r"C:\Windows\Fonts\arialbd.ttf"
W, H = 512, 360
HW = 0.25; HH = HW * H / W           # visible half extents in metres (card 0.50 wide)


def mat(name, col):
    m = bpy.data.materials.new(name); m.diffuse_color = (*col, 1); return m


def rect(name, cx, cy, hx, hy, z, m):
    bpy.ops.mesh.primitive_plane_add(size=1, location=(cx, cy, z))
    o = bpy.context.active_object; o.name = name; o.scale = (hx * 2, hy * 2, 1)
    o.data.materials.append(m); return o


def frame(name, hx, hy, t, z, m):
    rect(name + "T", 0, hy - t / 2, hx, t / 2, z, m); rect(name + "B", 0, -hy + t / 2, hx, t / 2, z, m)
    rect(name + "L", -hx + t / 2, 0, t / 2, hy, z, m); rect(name + "R", hx - t / 2, 0, t / 2, hy, z, m)


def text(body, size, y, maxw, m, small_caps=False):
    cu = bpy.data.curves.new("T", 'FONT'); cu.body = body; cu.font = bpy.data.fonts.load(FONT, check_existing=True)
    cu.align_x = 'CENTER'; cu.align_y = 'CENTER'; cu.size = size
    if small_caps:
        cu.small_caps_scale = 0.78
        for i in range(1, len(body)): cu.body_format[i].use_small_caps = True
    o = bpy.data.objects.new("T", cu); bpy.context.scene.collection.objects.link(o)
    o.location = (0, y, 0.002); o.data.materials.append(m)
    bpy.context.view_layer.update()
    if o.dimensions.x > maxw:
        f = maxw / o.dimensions.x; o.scale = (f, f, 1)
    return o


def setup():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'FLAT'; sc.display.shading.color_type = 'MATERIAL'
    sc.display.render_aa = '16'
    sc.view_settings.view_transform = 'Standard'
    sc.render.resolution_x = W; sc.render.resolution_y = H; sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = 'PNG'; sc.render.image_settings.color_mode = 'RGB'
    cd = bpy.data.cameras.new("C"); cd.type = 'ORTHO'; cd.ortho_scale = 2 * HW
    cam = bpy.data.objects.new("C", cd); sc.collection.objects.link(cam); cam.location = (0, 0, 1); sc.camera = cam
    white, red, blue, black = mat("W", (1, 1, 1)), mat("R", (0.62, 0.012, 0.02)), mat("B", (0.01, 0.04, 0.45)), mat("K", (0.0, 0.0, 0.0))
    rect("BG", 0, 0, HW + 0.01, HH + 0.01, 0, white)
    frame("Red", HW - 0.010, HH - 0.010, 0.008, 0.001, red)       # thin red outer line
    frame("Blue", HW - 0.022, HH - 0.022, 0.005, 0.001, blue)     # thinner blue inner line
    return black


paths = []
for n in range(2, 13):
    black = setup()
    text("Round", 0.072, HH - 0.078, 0.34, black, small_caps=True)               # top third
    text(str(n), 0.26, -0.040, 0.40, black)                                       # big numeral
    p = os.path.join(OUT, "T_RoundCard_%d.png" % n)
    bpy.context.scene.render.filepath = p; bpy.ops.render.render(write_still=True); paths.append(p)
print("CARDS", paths)

# ---- check on the model: card-up pose with T_RoundCard_2 on M_Card
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=os.path.join(ROOT, "Assets", "Art", "Exports", "GLB", "SK_RingCardWalker.glb"))
sc = bpy.context.scene
arm = [o for o in sc.objects if o.type == 'ARMATURE'][0]
m = bpy.data.materials["M_Card"]
if not m.node_tree: m.use_nodes = True
nt = m.node_tree; tex = nt.nodes.new("ShaderNodeTexImage"); tex.image = bpy.data.images.load(paths[0])
nt.links.new(tex.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
for pb in arm.pose.bones: pb.rotation_mode = 'QUATERNION'


def local(pb, R):
    M = pb.bone.matrix_local.to_3x3(); return (M.inverted() @ R @ M).to_quaternion()
ua, fa = arm.pose.bones["UpperArm.R"], arm.pose.bones["ForeArm.R"]
Rua = (ua.bone.tail_local - ua.bone.head_local).normalized().rotation_difference(Vector((-1, 0, 0.12)).normalized()).to_matrix()
Rt = (fa.bone.tail_local - fa.bone.head_local).normalized().rotation_difference(Vector((0, 0, 1))).to_matrix()
ua.rotation_quaternion = local(ua, Rua); fa.rotation_quaternion = local(fa, Rua.inverted() @ Rt)
sc.render.engine = 'BLENDER_WORKBENCH'; sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'TEXTURE'
sc.view_settings.view_transform = 'Standard'
sc.render.resolution_x = 600; sc.render.resolution_y = 800
cd = bpy.data.cameras.new("C"); cd.type = 'ORTHO'; cd.ortho_scale = 2.4
cam = bpy.data.objects.new("C", cd); sc.collection.objects.link(cam)
tgt = Vector((0, 0, 1.15)); cam.location = tgt + Vector((-0.8, -3.8, 0.3))
cam.rotation_euler = (tgt - cam.location).to_track_quat('-Z', 'Y').to_euler(); sc.camera = cam
sc.render.filepath = os.path.join(SHOT, "SK_RingCardWalker_cardup_textured.png")
bpy.ops.render.render(write_still=True)
print("CHECK", sc.render.filepath)
