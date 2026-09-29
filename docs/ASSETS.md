# Assets and content packs

## The `ai_` placeholders

Every file this repository generates starts with `ai_`. `npm run gen:assets` (`tools/gen/main.ts`) writes them to
`data/ai/` (not committed) and the game loads them at runtime (`game/Scripts/Assets/AssetStore.cs`). The prefix marks
what is a placeholder, so that other assets can take their places one by one. The beta builds one car, the sedan
(`PACK_CARS` in `tools/gen/cars.ts`); the coupe's and the pickup's code stays for later.

| Files | What | Made by (`tools/gen/`) |
|---|---|---|
| `ai_model_*.glb` | 3D models: car bodies, every part, fasteners, garage and lot props | `parts.ts`, `body.ts`, `media.ts`: primitives with bevelled edges; the second generation in `body2.ts` (the sedan's body as curved sheet panels), `parts2.ts` (wheels, discs, calipers) and `sheet.ts` (meshing the sheets) |
| `ai_tex_*.png` | floor, wall, asphalt and pegboard textures; decals | `media.ts`: procedural noise and shapes |
| `ai_snd_*.wav` | tool, part and UI sounds | `media.ts`: synthesized |
| `ai_icon_*.svg` | tool and navigation icons | `media.ts`: hand-written SVG |
| `ai_portrait_*.svg` | cartoon customer portraits | `media.ts`: procedural SVG |
| `ai_*.json` | game data: tools, fasteners, parts, cars, jobs, names, decals, paints, rules | `content.ts`, `cars.ts` |
| `ai_manifest.json` | logical asset ids → the files above | `main.ts` |

## The HD pack (`data/hd`)

A few pictures made by others, for OpenGG's own look: surfaces for the JunkYard, the Car Lot and the Auction, and the
bare metal of damaged parts, from [ambientCG](https://ambientcg.com). They are committed, are not `ai_` files, and
must be CC0, each listed with its source in [data/hd/LICENSES.md](../data/hd/LICENSES.md).
`game/Scripts/View3D/ModernLook.cs` reads them (`textures/<asset>/color.jpg`, `normal.jpg`, `roughness.jpg`,
`opacity.jpg`); without them the look falls back to the plain placeholder materials.

## Fonts (`game/Fonts`)

OpenGG's own look letters in bundled fonts under the SIL Open Font License, the same on every system: Anton, Archivo
Black, Open Sans and a narrowed Barlow Condensed (`tools/fonts/narrow.py`), each within a few per cent of the width of
the Windows font it stands in for (Impact, Arial Black, Tahoma, Bahnschrift). Sources in
[game/Fonts/LICENSES.md](../game/Fonts/LICENSES.md); `game/Scripts/Ui/Look.cs` loads them.

## Logical ids

Content never names files, only ids resolved through the manifest: models `body.sedan`, `part.i4_head`,
`fastener.hex`, `prop.lift`; textures `tex.concrete`, `tex.decal.flames`; sounds `snd.ratchet`, `snd.part_off`; icons
`icon.tool.ratchet`, `icon.nav.office`.

Models are in metres: +X the car's front, +Y up, +Z the car's right, the car's origin on the ground, centred. The
paintable material is named `paint`. Fastener models are unit size, +Y the unscrew direction.

## The original's assets

Read from the player's own copy at runtime, never committed or shipped (see [ORIGINAL_IMPORT.md](ORIGINAL_IMPORT.md)).
Their ids start with `orig:` (car meshes), `orig-portrait:` and `orig-decal:`, served by `OriginalSource`. What the
copy lacks still comes from the `ai` pack: the bolts' models, and without its archives the scenes, props, icons and
sounds too.
