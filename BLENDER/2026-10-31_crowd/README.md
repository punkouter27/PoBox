# Crowd impostor atlas (2026-10-31)

Sixteen low-poly spectators baked to impostor cards for `PoBox/Crowd` (today: procedural head-and-shoulders
blobs in `Assets/Shaders/Crowd.shader`). Nothing here is imported into Unity yet: copy the three `T_Crowd_*.png`
into the project when the editor is free and wire the shader as described below.

| File | What |
|---|---|
| `crowd_people.blend` | the 16 people x 3 poses as meshes (collections `Crowd_seated`, `Crowd_cheer`, `Crowd_phone`), the bake material `M_CrowdImpostorBake` (vertex colours `alb`, `msk`) and the ortho camera `C_Impostor` |
| `T_Crowd_Albedo.png` | 4096 x 4096 RGBA8. RGB albedo (**sRGB**), A = coverage (straight alpha) |
| `T_Crowd_Normal.png` | 4096 x 4096 RGBA8. RGB = impostor-space normal * 0.5 + 0.5 (**linear**), A = coverage |
| `T_Crowd_Mask.png` | 4096 x 4096 RGBA8 (**linear**). R = recolour mask, G = skin, B = light spot, A = ambient occlusion |
| `crowd_stand_preview.png` | 2048 x 1152: six tiers of impostors lit and recoloured the way the shader below would, at 70-210 px per metre |
| `scripts/build_crowd.py` | builds the people, saves the .blend, renders 8 rows x 4 passes to linear EXR |
| `scripts/assemble_atlas.py` | packs the EXRs into the PNGs (un-premultiply, sRGB for albedo, colour dilation into transparent texels) and draws the preview |

Rebuild (about 5 min on CPU):

```
$B = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
& $B -b --factory-startup --python scripts/build_crowd.py   -- <tmp_render_dir>
& $B -b --factory-startup --python scripts/assemble_atlas.py -- <tmp_render_dir>
```

## Grid

- Atlas 4096 x 4096, **16 columns x 8 rows**, cell **256 x 512 px**, every cell the same size and scale.
- A cell shows **1.15 m wide x 2.30 m tall**, orthographic: **222.6 px per metre**, both axes.
- **Column = person** (0..15). **Row = pose + view**, counted from the TOP of the PNG:

| Row (from top) | Pose | View |
|---|---|---|
| 0 | seated | front |
| 1 | seated | nose-left (figure turned 40 deg, nose points to image left) |
| 2 | seated | nose-right (40 deg, nose points to image right) |
| 3 | standing, arms up (cheer) | front |
| 4 | cheer | nose-left |
| 5 | cheer | nose-right |
| 6 | standing, phone held up in the figure's right hand (torch towards the ring) | front |
| 7 | phone | nose-right only; for nose-left mirror the cell in u (and negate normal.x) |

UV of cell (col c, row r) in Unity's convention (v = 0 at the bottom of the image):

```
cellMin = float2(c / 16.0, 1.0 - (r + 1) / 8.0)
cellUV  = cellMin + localUV * float2(1.0 / 16.0, 1.0 / 8.0)      // localUV in 0..1 across the cell
```

### Pivot and height

- **Pivot = the seat-surface centre** (where the seated figure's buttocks meet the seat), in every cell, both poses.
  Cell-local UV of the pivot: **(0.5, 0.2174)** (0.50 m above the cell bottom).
- The cell spans **-0.50 m .. +1.80 m** vertically relative to the pivot and **-0.575 .. +0.575 m** sideways.
- The floor the feet stand on is **0.46 m below the pivot** (cell-local v = 0.0174). Standing cells (rows 3-7)
  show the person standing on that floor in front of their seat, so switching seated <-> standing needs **no
  geometry change**: the same quad, the figure just rises (head ~0.85 m -> ~1.3 m above the pivot).
- Figure heights (standing, floor to head top): 1.58-1.90 m, listed below. Seated head top is ~0.97 m above the
  pivot for a 1.75 m person; arms-up hands reach ~1.75 m above the pivot (the cell top is 1.80 m).

Quad for a figure: `pos = pivot + R * (localU - 0.5) * 1.15 + up * (localV * 2.30 - 0.50)`.

Today's quads are 0.62 x 0.95 m with the bottom at the pivot, and the procedural head centre sits 0.74 m above it;
the impostor's seated head centre is 0.86 m above the seat for a 1.75 m person. So the existing pivot is about
0.1 m above the seat surface: lower the pivot by ~0.1 m (or just accept a 0.1 m taller crowd).

### People

| Col | Name | Sex | Height | Build | Skin | Hair | Top (recolourable) | Fixed colours / extras |
|---|---|---|---|---|---|---|---|---|
| 0 | Dan | m | 1.80 | avg | light | short brown | t-shirt | jeans, white shoes |
| 1 | Maya | f | 1.65 | slim | tan | long black | hoodie | black trousers |
| 2 | Marcus | m | 1.86 | heavy | dark | buzz | open jacket | white inner shirt, khaki, beard |
| 3 | Emma | f | 1.62 | slim | pale | blonde ponytail | long sleeve | jeans |
| 4 | Ken | m | 1.74 | avg | east asian | short black | t-shirt | black cap, grey trousers |
| 5 | Aisha | f | 1.70 | avg | brown | afro | open jacket | black inner, jeans |
| 6 | Leo | m | 1.78 | slim | medium | short | hoodie | red beanie |
| 7 | Rose | f | 1.58 | heavy | rosy | grey bun | cardigan | cream inner, purple skirt, tights, glasses |
| 8 | Omar | m | 1.90 | avg | olive | short black | long sleeve | beard, jeans |
| 9 | Zuri | f | 1.68 | slim | deep | cropped | t-shirt | jeans |
| 10 | Frank | m | 1.72 | heavy | light | bald, grey sides | polo | khaki, belly |
| 11 | Lucia | f | 1.66 | avg | tan | bob | hoodie | jeans |
| 12 | Andre | m | 1.80 | avg | deep | buzz | open jacket | white cap backwards, sunglasses, black inner |
| 13 | Ciara | f | 1.74 | slim | pale | long red | t-shirt | black trousers |
| 14 | Raj | m | 1.68 | slim | brown | curly black | polo | glasses, jeans |
| 15 | Mei | f | 1.60 | avg | east asian | black bob, bangs | long sleeve | navy cap, khaki |

Cheer arm styles cycle by column (`col % 4`): 0/2 bent elbows fists up, 1 wide straight V, 3 hands together overhead.

## Normal convention

**Impostor space = the billboard's own tangent space, which for these renders is also view space**:
x = towards +u (image right), y = towards +v (up), z = out of the quad towards the viewer. Encoded
`n * 0.5 + 0.5`, renormalised, so a pixel facing the camera is (0.5, 0.5, 1.0). Rendered with a **level**
orthographic camera (no pitch), so on an upright camera-facing quad:

```
worldN = normalize(n.x * R + n.y * float3(0,1,0) + n.z * F)   // R = quad right (the direction u grows), F = horizontal direction to the camera
```

Same handedness as Unity view space (x right, y up, +z towards the camera). Import it as a **Default** texture with
**sRGB off**, not as a "Normal map" (that would repack it as DXT5nm / BC5 and expect tangent space with a
different z). Transparent texels are (0.5, 0.5, 1) with colours dilated from the edge.

## Mask channels (`T_Crowd_Mask.png`, linear)

- **R = recolour.** 1 on the one garment that takes the per-figure colour (t-shirt / hoodie / jacket / cardigan /
  polo, incl. sleeves, hood, pocket, collar). Its albedo is near-white (pockets and collars 0.86) so
  `albedo * tint` gives the tint directly. Trousers, inner shirts, caps, shoes and hair keep their baked colour.
- **G = skin** (face, neck, ears, bare arms, hands). Use it to vary skin brightness a little, or to warm it under
  the ring lights; the base tones are already varied per person.
- **B = light spot.** Rows 0-5: both hands (where a camera flash or a phone light can sit). Rows 6-7: the phone
  only (hands are 0 there): make it emissive while `_PoBoxCrowdPhones` is on.
- **A = ambient occlusion**, per person only (Cycles AO, 0.22 m, only-local), 1 = open. Outside the figure it is 1.

## Unity import

| Texture | sRGB | Alpha | Mipmaps | Wrap | Suggested compression |
|---|---|---|---|---|---|
| T_Crowd_Albedo | on | Alpha is transparency on | on | Clamp | ASTC 6x6 (Android) |
| T_Crowd_Normal | **off** | input alpha | on | Clamp | ASTC 6x6 |
| T_Crowd_Mask | **off** | input alpha | on | Clamp | ASTC 6x6, or 2048 max size |

Clamp keeps neighbouring cells apart at the atlas edge; inside the atlas each figure has transparent margin, and
colours are dilated 12 px into it, so mips down to 1/16 do not bleed dark fringes. If memory is tight on
phones, Max Size 2048 halves everything (111 px/m, still more than a far row needs).

## Suggested shader logic (`PoBox/Crowd`)

The mesh can stay as it is (pivot in POSITION, corner in TEXCOORD1, seed in TEXCOORD2, shirt colour in COLOR).
**Watch the u direction:** today's vertex shader builds `right = normalize(cross(float3(0,1,0), fwd))` with `fwd`
pointing to the camera, which is the camera's **left** in Unity, and TEXCOORD0.x = corner.x + 0.5. The procedural
figure is symmetric so it never showed; an atlas would come out mirrored (noses turned the wrong way). Use
`R = normalize(cross(fwd, float3(0,1,0)))` (the camera's right), or sample `1 - u`.

Vertex:

```hlsl
float3 toCam = _WorldSpaceCameraPos - pivot; toCam.y = 0;
float3 F = normalize(toCam + float3(0, 0, 1e-4));           // towards the camera
float3 R = normalize(cross(F, float3(0, 1, 0)));            // camera right: u grows this way
// size and pivot of the impostor cell
float3 pos = pivot + R * (v.corner.x * 1.15 + lean * cy) + float3(0,1,0) * (cy * 2.30 - 0.50 + bob + bounce + jump);

// person: from the seed (no seat id is stored; the seed is per figure)
float person = floor(hash2(v.seed * 17.3 + 1.7) * 16.0);

// view: the figure faces the ring (centre at the origin); where does its nose point on screen?
float2 facing = normalize(-pivot.xz + 1e-4);
float side = dot(facing, R.xz);                              // >0: nose points to image right
float view = abs(side) < 0.34 ? 0 : (side > 0 ? 2 : 1);      // 0 front, 1 nose-left, 2 nose-right (40 deg renders; 0.34 = sin 20)

// pose: seated unless this figure is up. Give each figure its own threshold so the hall rises in a wave, not at once.
float up = step(hash2(v.seed * 7.1) * 0.8 + 0.1, max(mood * energy * 0.6, burst));   // or o.lift
float phoneUp = phone;                                        // the existing per-figure phone flag
float row = phoneUp > 0.5 ? (view == 0 ? 6 : 7) : (up > 0.5 ? 3 : 0) + view;
float mirror = (phoneUp > 0.5 && view == 1) ? 1 : 0;         // row 7 is nose-right only

float lu = mirror > 0.5 ? 1 - (v.corner.x + 0.5) : (v.corner.x + 0.5);
o.uv = float2(person / 16.0, 1.0 - (row + 1) / 8.0) + float2(lu, v.corner.y) * float2(1.0 / 16.0, 1.0 / 8.0);
o.nflip = mirror > 0.5 ? -1 : 1;
o.R = R; o.F = F;
```

The bounce/jump offsets still move the whole quad; with the standing pose the jump reads better (whole body leaves the
floor). Swapping pose is a hard cut; with the per-figure threshold and the existing bob it reads as people getting
up. A fan (`fan`) of the scoring corner could be forced to `up = 1` on `_PoBoxCrowdBurst`.

Fragment:

```hlsl
half4 a = SAMPLE_TEXTURE2D(_Albedo, sampler_Albedo, i.uv);
clip(a.a - 0.5);
half4 m = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, i.uv);
half3 n = SAMPLE_TEXTURE2D(_Normal, sampler_Normal, i.uv).xyz * 2 - 1;
n.x *= i.nflip;
float3 N = normalize(n.x * i.R + n.y * float3(0,1,0) + n.z * i.F);

half3 tint = i.color.rgb;                               // the per-figure shirt colour, corner colour for fans
half3 col = a.rgb * lerp(1, tint, m.r);

// light: the ring's main light (or a fixed "house" direction over the ring) plus ambient, both with AO
Light L = GetMainLight();
half ndl = saturate(dot(N, L.direction));
half3 lit = col * (SampleSH(N) * m.a + L.color * ndl * (0.5 + 0.5 * m.a)) * _Brightness * (1 + 0.5 * i.lift);
lit = MixFog(lit, i.fog);
lit += half3(1, 0.97, 0.92) * flash * m.b * _FlashGain;       // camera flash at the hands
lit += half3(0.85, 0.92, 1.0) * i.lights.y * m.b * _PhoneGain; // phone torch (rows 6-7 mark only the phone)
return half4(lit, 1);
```

In the house look the arena is dark and the ring is lit from above; a cheap alternative to `GetMainLight` is a
fixed direction from the figure to a point 6 m above the ring centre: `normalize(float3(0,6,0) - pivot)`.

## Notes / limits

- One draw call is kept: three textures instead of none (3 x 4096^2; ~8 MB each with ASTC 6x6 + mips).
- Rendered from a level camera. From the broadcast camera the far rows are seen ~5-15 deg from above; the cards
  do not show the tops of heads/shoulders, which is invisible at those distances.
- Seated cells in front view show the shins as vertical columns under the lap (the thighs point at the camera).
  The row in front (seat backs, heads) hides that band in the stand; if a front row is ever fully visible, add a
  seat-back strip in front of it.
- Phone row 7 + mirroring: the mirrored phone ends up in the left hand. Fine for a crowd.
