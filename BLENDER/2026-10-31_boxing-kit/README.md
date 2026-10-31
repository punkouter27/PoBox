# Boxing kit (2026-10-31): trunks + boots per boxer

Gloves were dropped by the owner mid-task (no glove files were made). Hand wraps skipped (hidden by gloves anyway).

## Files

| File | What |
|---|---|
| `Kit_<Name>.glb` (8) | Skinned satin trunks + high-top laced boots for that boxer, exported **with that boxer's armature only** (no body mesh). |
| `Kit_<Name>.blend` (8) | Same kit + armature in Blender (no body; re-import `Assets/Models/<Name>.glb` beside it to edit the fit). |
| `renders/<Name>_front.png`, `_34.png`, `_legs34.png` | Eevee fit checks, rest pose, trunks shown red/white, boots black (render colours only). |
| `scripts/build_kit.py` | Builds everything: `blender -b --python scripts/build_kit.py -- [Name ...]` (all eight if no names). |
| `scripts/check_bind.py` | Verifies each kit binds to the boxer's own skeleton (writes `scripts/check_bind.json`). |
| `scripts/report_all.json`, `build_all.log` | Per-boxer triangle counts, bones, waist/crotch heights. |
| `scripts/inspect.py`, `probe.py` | Dump the source GLBs' meshes, bones and leg-bone positions. |

Sources are only read: `Assets/Models/*.glb` were not modified; nothing under `Assets/` or `training/` was written.

## Boxers

All eight built, none skipped: Matt, Zombie, Nick, Trump, Grandma, Grandpa, Kim, Sokol.

| Boxer | Tris | Armature | Bones the kit is weighted to |
|---|---|---|---|
| Matt | 3872 | `Armature` | Hips, Spine, Spine1, Left/Right UpLeg, Leg, Foot, ToeBase |
| Nick | 3792 | `Armature` | Hips, Spine01, Spine02, Left/Right UpLeg, Leg, Foot, ToeBase |
| Grandma | 3792 | `Armature` | as Nick |
| Grandpa | 3872 | `Armature` | as Nick |
| Trump | 3872 | `Armature_Trump` | `mixamorig:` Hips, Spine, Spine1, Left/Right UpLeg, Leg, Foot, ToeBase |
| Zombie | 3872 | `Armature` | CC_Base_Hip, CC_Base_Waist, CC_Base_Spine01/02, CC_Base_{L,R}_ThighTwist01/02, CalfTwist01/02, Foot, ToeBase |
| Kim | 3792 | `Rig` | Hips, Spine, UpperLeg/LowerLeg/Foot/Toe .L/.R |
| Sokol | 3872 | `Rig` | hips, spine, thigh/shin/foot .L/.R |

Per kit: Trunks 1364 tris, Waistband 300, Boots 1840-1920, Laces 288. All quads except the boot heel/toe fans
and the lace bars. Every vertex is weighted (at most 4 influences, normalised), and no arm or head bones are used.

## Materials (4 slots, all double-sided, no textures)

| Slot | Base colour | Roughness | Use |
|---|---|---|---|
| `Trunks` | 0.8 grey (tint per corner) | 0.30 | trunk body |
| `Waistband` | 0.92 white | 0.40 | waistband (fold + 2 rings) and the side stripe on both hips and the outside of each leg |
| `Boots` | 0.8 grey (tint, or black) | 0.45 | boot shaft + foot |
| `Laces` | 0.92 white | 0.70 | X lacing bars on the boot fronts |

The spec asked for Trunks + Waistband + Boots. `Laces` is a fourth slot so the laces can stay white when the boots
are tinted. The stripe shares the Waistband slot.
UVs: each piece is unrolled cylindrically into its own rectangle (pelvis v 0.62-1, legs 0.32-0.6, boot shafts 0.16-0.32,
feet 0-0.16, laces a single texel at (0.97, 0.03)). The UVs are clean but texel density is uneven, which is fine for flat colours.

## Axes and scale

Exported with Blender's glTF exporter, +Y up, metres. The Blender scenes are Z-up; every boxer faces -Y in Blender, which is +Z in glTF and Unity.
The kit's armature node keeps each boxer's own root transform (for example Nick, Grandma and Grandpa keep their 0.01 scale,
and the Zombie keeps its rotated, 0.01-scale root), so the kit lines up with the boxer at identity.

## How it was fitted

The pieces are lofts of rings, fitted in the boxer's rest pose (the source GLBs carry idle animations, which were ignored).
- **Trunks.** A pelvis tube runs from the waist (hip joint + 0.30 x thigh length) down to the crotch. It has 40
  segments with a 7-10 cm waistband. Two leg tubes of 24 segments run from just above the crotch to 56 % of the
  thigh, with the hem flaring outward.
- **Boots.** Each boot has a shaft tube around the shin from the ankle to 42 % of the shin, and a foot tube along the
  foot's principal axis with a flat sole and closed heel and toe.
- **Placing the rings.** Each ring vertex is placed by casting a ray from outside toward the ring's centre onto the
  boxer's own outer surface, then pushing it outward:
  - The surface is the lower body only: vertices whose weight on arm or head bones is 0.3 or more are removed.
    For leg and boot pieces, only that leg's vertices are used.
  - Each ring is made convex and smoothed, so the satin and leather bridge creases.
  - Offsets are 1.2-3.4 cm for the trunks and 1.4-1.5 cm for the boots.
- **Weights.** A Data Transfer modifier (vertex groups, *nearest face interpolated*) copies the weights from that same
  surface. Each leg piece takes them from its own leg only, so an inner-thigh hem can never pick up the other leg.

**Bind check** (`check_bind.py`): for every joint a kit uses, its inverseBindMatrix equals the one in the boxer's
original GLB. The largest difference is 1e-3, on the 0.01-scale rigs, where matrix entries are around 100. So the kit's
vertices are in the same bind space as the boxer's body mesh.

## Attaching in Unity

The kit binds to the boxer's **existing** bones by name. Its own armature is only a carrier for names and bind poses.

1. Import `Kit_<Name>.glb` with glTFast, the same importer the boxers use, so node names are produced the same way
   (including names like `mixamorig:Hips` and `Foot.L`).
2. Instantiate it as a child of the boxer's skin root, the GameObject that `SkinBinder.skin` points to. Use local
   position 0, rotation identity and scale 1. Do this before `SkinBinder.Bind()` runs (in Awake), or at least before
   play. Bind re-poses and rescales the skin root, and the kit has to ride along.
3. For the kit's `SkinnedMeshRenderer` (`Kit_<Name>`), rebuild the bone array by name, then delete the kit's own
   armature GameObject:
   ```csharp
   var byName = new Dictionary<string, Transform>();
   foreach (var t in skinRoot.GetComponentsInChildren<Transform>(true)) if (!t.IsChildOf(kitRoot) && !byName.ContainsKey(t.name)) byName[t.name] = t;
   var bones = kitSmr.bones;
   for (int i = 0; i < bones.Length; i++) bones[i] = byName[bones[i].name];   // every name exists on the boxer (verified)
   kitSmr.bones = bones;
   kitSmr.rootBone = byName[kitSmr.rootBone != null ? kitSmr.rootBone.name : "<hips bone>"];
   kitSmr.updateWhenOffscreen = true;   // SkinBinder sets this only on renderers present at Bind
   // keep sharedMesh.bindposes as imported: they equal the boxer body's bind poses
   ```
4. Set colours by corner on the material instances: `Trunks` (and optionally `Boots`) red or blue. `Waistband` and
   `Laces` stay white.
5. There is no physics change: these are visual only, and no collider, MuJoCo geom or parity file is touched.

Bones to expect per boxer are in the table above. The hips bone (the root bone) is `Hips`, `mixamorig:Hips`,
`CC_Base_Hip` (Zombie) or `hips` (Sokol). SkinBinder drives thighs, shins and feet. The Zombie's twist bones and
`CC_Base_Hip` are not driven directly: they follow their driven parents, exactly as the Zombie's body mesh does.

## Known issues seen in the renders

- **Clothes underneath.** Every boxer is a single clothed mesh, so the trunks are fitted over the clothes: Matt's
  camouflage jacket, Trump's suit jacket, the T-shirts. The trunks read as worn over the outfit, and Matt's and
  Trump's waists come out bulky. The jackets and trousers still show above and below. A proper fix needs the
  clothing removed or masked from the body meshes.
- **Pelvis tube lip.** The bottom edge of the pelvis tube overlaps the tops of the leg tubes by design, which reads
  as a seam. On Kim (short legs) it hangs nearly to the hem.
- **Shoes poking through.**
  - Kim's pink soles show slightly at the sole edges.
  - The Zombie's toe claws poke through the boot toes.
  - Grandpa and Grandma have small bits of shoe at the foot/shaft join.
- **Bulky boots.** Trump's boots are large because his shoes are large.
- **Zombie.** The rags on its legs show below the hem, and its shins are so short that its laces are only about two crosses.
- **Rest pose only.** Fitted and rendered in rest pose (T-pose or A-pose). There is no deformation test under motion: weights come
  from the body, so it should follow, but check the crotch and knees in play mode.
