# NipplePiercings 1.0.0

Nipple piercings for **Secret Flasher Manaka**: studs, barbells, rings, hoops, a crystal and a heart pendant, and a chain
between the nipples. Hanging pieces swing with her movements, rest on her body instead of going through it, and jingle.

## Requirements
- Secret Flasher Manaka (tested with v1.1.3)
- BepInEx 6 for IL2CPP (tested with build 738)
- Does **not** need RestraintHub.

## Install
Extract the zip into the game folder (the folder with `SecretFlasherManaka.exe`). You get `BepInEx/plugins/NipplePiercings/`.

## How to use
Open **Inventory > Costume > General (Upper Body)**. After the game's own items you find: Nipple Studs, Crystal Nipple Pendants,
Heart Nipple Pendants, Nipple Barbells, Curved Nipple Barbells, Small Curved Nipple Barbells, Nipple Rings, Small Nipple Rings,
Big Nipple Hoops, Nipple Chain Rings, Big Nipple Chain Rings and Nipple Chain.

- Equip a piercing to put it on both nipples (it replaces the one she wears). Equip it again to take it off.
- **Nipple Chain** adds a chain between the two piercings.
- Different piercings left and right: set `Piercing.Left` / `Piercing.Right` in the settings file.

## Settings
`BepInEx/config/FlanderDev.SFM.NipplePiercings.cfg` (created on the first start). Most useful:

| Setting | Default | |
|---|---|---|
| Piercing.Left / Right / Chain | none / none / false | what she wears (also set by the costume menu) |
| Model.Size | 1 | size of studs and pendants |
| Model.NippleFit | 1.7 | size of barbells and rings compared to her nipple |
| Physics.Damping | 1.5 | how quickly swinging pieces settle |
| Sound.Volume | 0.4 | jingle volume (0 = silent) |

Most changes apply right away while the game runs.

## Good to know
- What she wears is stored in the settings file, so it is the same in every save, and outfits ("My Set") do not include it.
- The piercings are drawn over clothes (a top or the coat does not hide them).
- On very different body shapes or in extreme poses a piece can dip into the skin a little.

Single-player / offline only. Code: MIT licence (`LICENSE.txt`). Models: "Assorted Piercings" by hellsquad2, CC BY-NC-ND 4.0 (smutba.se; details in `LICENSE-MODELS.txt`).
