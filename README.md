# NipplePiercings

Nipple piercings from `Piercings.blend`: studs, barbells, rings, hoops and two pendants (crystal, ruby heart), on either nipple,
optionally with a chain hanging between the two. Hanging pieces swing with her - under gravity, out when she walks, stops or
turns, settling again - and rest against her breasts, arms and the floor instead of passing through. A hard swing jingles and
makes the gems sparkle.

Worn from the game's own costume menu: **Inventory > Costume > General (Upper Body)**, after the game's nipple items: "Nipple
Studs", "Crystal Nipple Pendants", "Heart Nipple Pendants", "Nipple Barbells", "Curved Nipple Barbells", "Small Curved Nipple
Barbells", "Nipple Rings", "Small Nipple Rings", "Big Nipple Hoops", "Nipple Chain Rings", "Big Nipple Chain Rings" and "Nipple
Chain". Equipping a piercing puts it on both nipples (and replaces the one worn); equip it again to take it off. Different
piercings left and right: `Piercing.Left` / `Right` in the config. Not a restraint (it holds and blocks nothing), so it does not
use RestraintHub.

## How it works

- **Anchors:** the game's own nipple-piercing points, the two `ChikubiPiasController` objects (`PiasPosition` under `Breast_L`,
  `PiasPositoin` under `Breast_R`). They follow the nipples with the breasts' jiggle and the nipple size; their frame is +z out of
  the nipple, +y up, +x her right. The point lies inside the nipple, so the piercings are placed from the **nipple's tip**, found
  on her body mesh: of the skin within 4 cm of the point, the vertex furthest out along +z (`Logic/NippleApex.cs`), re-read from a
  bake every 2 s and kept in the point's frame in between.
- **Kinds** (from the model file): studs and the crystal pendant sit on the tip, the ball half pressed in (`ForwardCm` moves
  them); barbells and rings go through the nipple `DepthCm` behind the tip. Studs and pendants are sized to her height (`Size`;
  the crystal pendant is 6.5 cm long at height 1, 8.5 cm on the first save); what goes through the nipple is sized to the nipple -
  the point's scale follows her height and the nipple size setting - times `NippleFit` (sized to her height alone, a barbell was
  shorter than her nipple is wide and vanished in it).
- **Models:** `tools/rig_piercings.py` gives every piercing an armature (one bone per rigid piece: stud / bar, chain links, pendant;
  a ring: a short "pierce" bone ending where its wire passes through the nipple, then "ring"); `tools/export_model.py` writes
  all of them and the chain link into `assets/piercings.npd` (embedded in the DLL). In game each bone is an object holding its
  piece; no skinning. Pieces modelled inside out (the heart's chain links) are turned right way out by the exporter.
- **Physics** (`Logic/PendantChain.cs`, plain C#): the joints are points; the first piece is fixed to the nipple, the rest fall
  under gravity, lose speed to `Damping` and keep their lengths exactly (follow the leader with velocity correction - plain
  position-based solving left the crystal's chain ~1 mm stretched, more than a link). Colliders (`Logic/Collider.cs`): a sphere
  behind each nipple for each breast (`BreastSize` x the breast root bone - nipple distance), capsules on her arms (10 % / 8 %
  of the upper arm / forearm, `ArmCollision`) and the floor under her (`FloorCollision`); pieces stay out by their front-to-back
  thickness. Stepped at 120 Hz before rendering (Camera.onPreCull), so it follows the drawn nipple; a jump of more than 0.5 m
  hangs the chain afresh.
- **Chain between the nipples** (`Logic/SaggingChain.cs`): links as points held at both piercings' hooks (a ring's bottom, a
  barbell's lower ball, under a stud), sagging under gravity; its length is `ChainSlack` x the hooks' distance when it is put on.
  Neighbouring links are turned 90 degrees, as real links interlock.
- **Look:** copies of the game's handcuff material (toon shader, lit and outlined like the game's items) with a third of its
  outline; metal light silver with a rim shine; gems (crystal, ruby) on the toon shader's see-through variant, slightly
  transparent, with rim light and a highlight that flashes on a jingle.
- **Jingle** (`Logic/Chime.cs`): a synthesised metal chime (the game has no bell sound), played from the piece when the
  fastest-moving end jolts past 0.5 m/s (at most every 0.3 s, louder the harder), on her own sounds' mixer group ("Environment",
  so the game's volume applies); `Sound.Volume`.
- **Costume menu** (`Closet.cs`): the items are records appended to the game's "General (Upper Body)" series (ids 33-44 today),
  plain `RCosplayParts` with the game's nipple piercing's settings. Hooks answer "owned", "equipped" and "equip" for these ids
  from the config and skip the game's body object for them; the game's own entries for them (it adds default enhancement entries
  for every record) are taken out of the save while it is written. A save written with the mod loads without it.

## Config (`BepInEx/config/com.local.nipplepiercings.cfg`)

| Key | Default | Meaning |
|---|---|---|
| General.Enabled | true | show the piercings (live) |
| Piercing.Left / Right | none | piercing per nipple: none, stud, crystal, heart, barbell, curved, curved_small, ring, ring_small, hoop, chain_ring, chain_ring_big (live; the costume menu sets both) |
| Piercing.Chain | false | chain between the two piercings (live; the menu's "Nipple Chain") |
| Model.Size | 1 | size of studs and pendants (1 = the crystal pendant 6.5 cm at height 1, scaled with her height) |
| Model.NippleFit | 1.7 | size of barbells and rings relative to her nipple |
| Model.ForwardCm | 0 | out of (+) or into (-) the nipple (0 = a stud's ball half pressed into the tip) |
| Model.DepthCm | 0.45 | how far behind the tip barbells and rings go through (cm, first save's nipples at height 1; scales with the nipple) |
| Physics.Damping | 1.5 | share of the speed lost per second (0 = swings on and on) |
| Physics.Gravity | 1 | gravity relative to the real one |
| Physics.BreastSize | 0.5 | breast sphere radius as a share of the root bone - nipple distance (0 = no breast collision) |
| Physics.ArmCollision / FloorCollision | true | hanging pieces stay out of her arms / above the floor |
| Physics.ChainSlack | 1.3 | chain length relative to the nipples' distance when it is put on |
| Sound.Volume | 0.4 | jingle loudness (0 = silent) |
| Debug.Disable | 0 | bit mask (live): 1 no physics (hang as modelled), 2 no collisions, 4 no sound, 8 no sparkle |

What she wears is kept in the config, so it is the same in every save.

## Rebuilding the models

```bash
blender -b --disable-autoexec "Piercings.blend" --python mods/NipplePiercings/tools/rig_piercings.py -- Piercings_rigged.blend
blender -b --disable-autoexec "Piercings_rigged.blend" --python mods/NipplePiercings/tools/export_model.py -- mods/NipplePiercings/assets/piercings.npd
```

## Diagnostics

`game get NipplePiercings.PiercingController::Status` (per side: what is worn, how far the tip hangs below and aside, the
colliders its joints come within 2 cm of; the chain's links and sag), `...::Jingles` (chimes played), `NipplePiercings.Closet::Status`.
`game set NipplePiercings.PiercingController::DebugView 'right 0.12 R'`: the main camera looks at a nipple (L / R, M between
them) from front, right, left, back, top or below at the given distance (SfmKit `DebugCamera`); `''` off.

## Verification

- L1: `./tools/dev.ps1 test NipplePiercings` (58): the model set (11 piercings + link, materials, kinds, ring pivot, hooks, axes,
  winding vs normals, thickness), format errors, the hanging chain (hangs from a tilted frame, swings, exact lengths, damping and
  its control, real-size stretch 0, colliders: pushed aside without passing in, rests, control without collisions, a stud on a
  sphere's surface hangs straight), colliders (sphere, capsule, floor), the chain between two hooks (sag, pitch, sphere, links
  turned 90 degrees, LookRotation), the chime and the jingle trigger, the nipple tip search.
- L3 0.1.x (2026-10-25, first save, her top's renderer hidden for the views): the crystal pendant hangs from the nipples' tips
  (ball on the tip; 8.1 cm), swings while walking and settles; crawling it hangs straight down, control `Debug.Disable 1`: along
  the breasts.
- L3 0.2.0 (2026-10-25):
  - every piercing worn and seen from the front (sheet), barbells and the curved barbell close up: the straight bar through the
    nipple side to side with both balls outside; the curved one from above the tip down through it to below, balls in front.
    Before `NippleFit` (sized by her height) barbells were inside the nipple and not visible.
  - rings, hoop and heart pendant hang from the nipple (side views: through it); chain between rings / studs: 38-41 links of
    0.6 cm, 7.7-8.0 cm sag ("chain:" lines).
  - jingle: walking + stopping 5-6 chimes; `Debug.Disable 4` (control): 0. Switching piercings standing still: 0 (it rang
    falsely before the "fresh" fix).
  - crawling: right after she drops the crystals swing out (7 cm aside), within ~1 s they hang straight down; no arm contact in
    crawling or Nipple Play (the arm capsules are covered by the tests only).
  - costume menu: the 12 items listed with their names in General (Upper Body); "Nipple Rings" equipped -> rings on both
    nipples, "Crystal Nipple Pendants" -> swapped, again -> off (none), "Nipple Chain" -> chain off; the save's equipped table
    (`PlayerCosplayPartsDict`) never held their ids. Saving at home (closing the closet): "24 entries of the piercing items kept
    out of the save"; that save loaded **without the mod**: no errors, the enhancement table back at 207 entries, the menu shows
    only the game's items.
  - gems close up: crystal see-through turquoise with a rim light, the ruby heart red; the heart's chain was dark (links modelled
    inside out) until the exporter turned them.
  - not checked: other nipple sizes or heights, map changes, clothes over the piercings, the sparkle on screen, the jingle by ear.

## Known limits

- **Clothes are not taken into account:** under a top or the coat the piercings are drawn through the clothes.
- What she wears is in the config, the same for every save; "My Set" outfits do not include the piercings.
- The breasts are spheres: on very differently shaped breasts or in extreme poses a piece can dip into the skin or hang a little
  off it (`BreastSize`).
- The items' ids in the costume series are the next free ones; a game update adding records to that series moves them (nothing
  is saved under them).
