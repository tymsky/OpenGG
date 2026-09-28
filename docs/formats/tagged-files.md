# Tagged data files: `.car`, `.jpk`, `.dpk`, `.mek`

Worked out from the data files of an installed copy (2026-09-25), without examining the executables; structure only,
no original content quoted. Evidence: the two car-kit spec files and the two source `.3DS` models and one `.tga` that
ship in `Data\Cars`; statistics over all 166 cars, 8 job packs, 5 decal packs and the saves; the manual and its online
FAQ; two fan-written guides (2001) to the text formats the job and decal packs are compiled from. See
[How this was checked](#how-this-was-checked).

Unmarked means confirmed by independent checks, *probably* one line of evidence, **?** unknown.

## Container

These four formats share one little-endian, self-describing tree of tagged nodes:

```
u32 tag     low 16 bits = field id, top 3 bits = type
u32 length  payload size in bytes
u8[length]  payload
```

| type (tag >> 29) | meaning | payload |
|---|---|---|
| 4 | node | child nodes, back to back |
| 3 | string | Latin-1, no terminator |
| 2 | float | f32 |
| 1 | int | i32 |
| 0 | raw | bytes (vectors, arrays, pixels) |

Each file is a single root node. Its first child is `0: int = 1`, probably a format version.

| root id | file |
|---|---|
| 1 | `.car` car |
| 2 | `.dpk` decal pack |
| 3 | `.mek` mechanic (save) |
| 4 | `.jpk` job pack |

A reader should skip ids it does not know. Raw vec3 = 3 × f32. Unset floats often hold `CD CD CD CD` (uninitialised memory in the tool that wrote the file), and a few hold NaN bit patterns. Treat both as "no value".

## Image node (shared)

| id | type | meaning |
|---|---|---|
| 1000 | int | width |
| 1001 | int | height |
| 1002 | int | bits per pixel: 8, 16, 24 or 32 |
| 1003 | raw | pixels: exactly w × h × bpp/8 bytes (no row padding), **top row first** |
| 1004 | raw | 8 bpp only: palette of 256 entries, each R, G, B |

| bpp | pixel | used by |
|---|---|---|
| 8 | palette index | car textures |
| 16 | u16 little-endian RGB565 (red in bits 15–11) | car textures, the `.mek` paint texture |
| 24 | bytes B, G, R | decals, portraits |
| 32 | bytes B, G, R, A. *Probably*: the fourth byte varies like alpha. | a few car textures |

## `.car`: `1 / 100` car

The car kit builds a car from a `.3DS` model plus a text spec. Spec keys are given in the tables and listed in [Car-kit spec](#car-kit-spec).

| id | type | meaning |
|---|---|---|
| 200 | int | car id (`CarID`). Job packs refer to it. |
| 201 | string | display name (`Car`) |
| 210 | int | minimum skill level (`CarMinSkill`): 0 Learning, 1 Novice, 2 Handy, 3 Expert, 4 Mekada |
| 202 | node × n | texture: `400` source path at build time (string), `401` int (always 1, meaning **?**), `402` image node |
| 203 | node × n | material (list index = material id) |
| 204 | node × n | mesh |
| 205 | node × n | overlay mesh, one per body mesh (see below) |
| 206 | node × n | part |
| 209 | node × n | part sound (optional): `2100` part number, `2101` raw bytes of a whole RIFF/WAVE file (PCM, 8–44.1 kHz, 8 or 16 bit, mono or stereo). `2101` can be missing. |

Every 209 names an existing part (105 of 105): usually the starter (312 = 2) or the block (312 = 1), sometimes an intake, exhaust or an accessory such as a horn. It is the part's own sound: a car's starter, engine and exhaust sounds play at Start Engine ([FIDELITY](../FIDELITY.md)); an accessory's was not heard. Most stock cars have none.

**Material (203)**

| id | meaning |
|---|---|
| 600 | name. The kit adds `SpecMat` (overlay) and `MultiMatPaint` (paint). |
| 601 | diffuse colour, vec3 RGB 0..1 (the model's diffuse colour) |
| 602 | source blend factor. *Probably* in the usual order 0 zero, 1 one, 2 source colour, 3 inverse source colour, 4 source alpha, 5 inverse source alpha. Only 1 and 4 occur. |
| 603 | destination blend factor, same order. Only 0, 1 and 5 occur. |
| 604 | blend class: 0 opaque, 1 alpha-blended (set exactly when 606 < 1), 2 additive |
| 605 | shade mode: 1 flat, 2 smooth (the running game lights mode 1 per face). 1 occurs almost only on materials named `Bug` on boxy low-poly engine parts. |
| 606 | opacity, = 1 − the model's transparency |
| 607 | material class, rendering effect **?**: 0 matte (engine and running gear only, never on a body mesh), 1 `SpecMat` and a few others (mostly transparent), 2 the default for body and paint materials (also on some engine parts), 3 transparent (glass) |
| 608 | texture index into the 202 list; only textured materials have it |

The usual combinations of 602, 603 and 604 are: opaque 1, 0, 0; glass 4, 5, 1; `SpecMat` 1, 1, 2 (additive).

The material list holds, in order:
1. The model's own materials. Those that carried the paint placeholder texture are renamed `MultiMatPaint`, and no face refers to them.
2. `SpecMat`.
3. One `MultiMatPaint` per paint run of each part, referenced only from 314.

**Mesh (204 / 205)**

| id | meaning |
|---|---|
| 700 | mesh name. The prefix gives the region: `#` engine, `$` body, `^` running gear. Meshes without a prefix belong to no part: `model` is a person figure or a backdrop, *probably* for the "Model In Photo" snapshot option; the rest are leftover helper objects. About 30 body meshes end in `&` (meaning **?**). |
| 701 | mesh index, equal to the position in the 204 list |
| 706 / 707 | face count / vertex count |
| 702 | pivot position (vec3, car space) |
| 703–705 | rotation rows (vec3 × 3): the local X, Y, Z axes in car space. car = local · R + pivot (row vectors). |
| 708 × n | vertex: `900` position (local), `901` normal (local) |
| 709 × n | triangle: `800–802` vertex indices, `803–805` UV per corner (2 × f32), `806` material id; 666 = the car's paint |

Coordinates and conventions:

- **Left-handed (as in Direct3D):** +Y is up, the front is at −X, and the car's **left** side is at −Z. For a right-handed renderer such as three.js, mirror everything:
  - negate z of positions, normals, pivots and bolts;
  - negate R[0][2], R[1][2], R[2][0] and R[2][1];
  - swap two corners of every triangle.

  Without this the car is drawn mirrored, and it still looks plausible.
- Winding: for 99.6 % of faces, (b − a) × (c − a) points the same way as the stored normals. In the left-handed frame this makes triangles clockwise seen from outside, which is Direct3D's default front face.
- Units: about 1.8 per real inch (1.65–2.1 from the wheelbase, track and length of five stock cars). A car is about 320 units long. They are not inches.
- Rotations are proper (orthonormal, determinant +1). 1,624 of 10,540 meshes are rotated: quarter turns (spinners usually have their local Z along the car's X axis) and small tilts. In the car compared with its source model, a mesh made from a mirrored object stores a rotation that is already baked into its vertices, a tool quirk.
- **UV:** UVs mean something only on textured faces (material has 608) and paint faces. Most untextured corners (71 %) and 37 % of textured corners hold `CD CD CD CD`.
- On textured faces the kit stored V as the negated model V, so 96 % of valid V values are negative. U is outside 0..1 on 4 % of corners.
  - Sample with wrap (repeat) addressing, with pixel rows in stored order (top row first) starting at v = 0, as Direct3D does.
  - In WebGL, upload the rows as stored (`flipY` false).
  - No extra V flip is needed.
- Paint faces (666) have UVs mostly in 0..1, made by the kit rather than taken from the model. They point into the 256 × 256 paint texture (see `.mek` 1507). Their orientation is unverified.

**Overlay meshes (205)**

There is exactly one overlay per mesh whose name starts with `$` (in all 166 cars), and none for `#`, `^` or unprefixed meshes. Each is named `(*)` and repeats its base mesh: same 701 index, pivot, rotation, vertices and triangles.

Every triangle uses `SpecMat` (white, additive: one + one), and the UVs are uninitialised, so the game computes them at run time. It is the body's highlight pass (measured as a specular highlight: [FIDELITY](../FIDELITY.md)). A reader can rebuild these meshes from the base meshes.

**Part (206)**

| id | type | meaning | spec key |
|---|---|---|---|
| 300 | int | part number: 0–99 engine, 100–999 body, 1000+ running gear | `Part#` |
| 301 | string | name | `Name` |
| 302 | int | 1 custom, 0 stock. The stock parts form the default car; custom parts are alternatives and extras. | `Stock` / `Custom` after the name |
| 303 | int | region: 1 engine, 2 body, 3 running gear | `Region` |
| 304 | string | mesh name | `MeshName` |
| 305 / 306 | f32 | price range, min / max dollars | `CostRange` |
| 307 | i32[] | parts this part is mounted on; all of them must be attached first | `AttachDep` |
| 308 | i32[] | parts that must come off before this one, in spec order. Usually the inverse of 307 (97 % of parts), but written separately. | `RemoveDep` |
| 309 | int | mutual-exclusion bit mask. Parts sharing a bit are alternatives (hard top or convertible, stock or custom wheel). | `MutualExc` |
| 310 | int | 1 = the 307 parents may be replaced by their mutually exclusive alternatives (see below) | `AMEA: yes` |
| 311 | int | 1 on every part except ten spinners, mostly crankshafts of the original cars, where it is 0. Meaning **?** | **?** |
| 312 | int | special role: 0 none, 1 block, 2 starter, 3 spinner (crankshaft, flywheel, fan: parts that turn when the engine runs), 4 accessory (*probably*; examples are fuzzy dice, taxi sign, surfboard, spoilers, subwoofers and fender skirts) | `Special` |
| 313 × n | vec3 | bolt position, in **car space** like the pivots | |
| 314 | node | material runs over the part's faces: `500` × n material ids, `501` i32[] face index where the next run starts, −1-terminated | |

Notes:
- **307 and 310.** Without AMEA, none of the 635 parent lists with several parents contains two mutually exclusive parents, so all listed parents are needed. With AMEA, 43 of 47 such lists do contain them, so AMEA must relax the rule. The reading that fits every case is that a parent may be replaced by a part from its mutual-exclusion group, so a windshield then fits on the hard roof or on the convertible frame. "Any one of the listed parents" is also possible.
- **312 = 4** marks 94 optional add-ons. Some are stock parts that can only be mounted on a custom part. They are *probably* not needed for a region to count as assembled.
- **313.** Engine and running-gear parts may have bolts (607 of 2,812 and 443 of 2,547 parts). Body parts almost never do (6 of 4,640, all in two cars).
- **314.** The runs cover the part mesh's faces in order. A run whose material is a `MultiMatPaint` covers exactly the paint (666) faces; other runs repeat the faces' own 806 (all 3.3 million faces checked). So each part has its own paint material for each run.

### Car-kit spec

The kit's text spec is written by the "SpecEd" tool. `;` starts a comment, and several `key: value` pairs may share a line, separated by `/`.

| key | stored as |
|---|---|
| `Car: "…"` | 201 |
| `Car3dFile: "…"` | not stored (the source model) |
| `CarID` | 200 |
| `CarMinSkill` | 210 |
| `Part#` (four digits) | 300 |
| `Name: "…"` followed by `Stock` or `Custom` | 301, 302 |
| `Region: Engine`, `Body` or `Rgear` | 303 (1, 2, 3) |
| `MeshName` | 304 |
| `CostRange: lo, hi` | 305, 306 |
| `AttachDep: a, b, …` | 307 |
| `RemoveDep: a, b, …` | 308 |
| `MutualExc: n` | 309 |
| `AMEA: yes` | 310 |
| `Special: block`, `starter` or `spinner` | 312 (1, 2, 3) |

The keys behind 311, 312 = 4, 209 and the material settings do not occur in the two samples. One compiled car differs from its spec in two parts: the spec marks them `Custom`, but the car stores them as stock. The spec was probably edited after compiling.

## `.jpk`: job pack (`4`)

A job pack is compiled from a text job file. The right-hand column gives that file's key names, taken from a fan-written guide to the format. The root holds `0`, then `104` × n portraits, then `103` × n jobs.

| path | type | meaning | job-file key |
|---|---|---|---|
| `104` × n | image | portrait, 75 × 75 × 24 bpp; each image stored once, in order of first use | `Image` |
| `103/1700` | int | job number; jobs are played in this order | `JobID` |
| `103/1701` | int | car id (`.car` 200) | `CarID` |
| `103/1702` | int | 0 in every job; meaning **?** | **?** |
| `103/1703` | f32 | fee: the customer's money for the job | `Fee` (dollars, or `AUTO` with a percentage) |
| `103/1704` | vec3 | the car's paint colour, RGB 0..1 | `CarColor` |
| `103/1706` | i32[4] | legal workshop views: flags in the order Complete, Engine, Body, Running gear. Complete is always 1. | `LegalModes` |
| `103/1710` | int | 0 in all 9 tutorial jobs, 1 in all 22 others; meaning **?** | **?** |
| `103/1707` × n | node | phase: `1800` kind (0 request, 1 hint or nag, 2 thanks), `1801` text, `1802` portrait index into the 104 list | `Phase { Type, Text, Image }` |
| `103/1708` × n | node | start stats: `1900` scope, `1901` part or region, `1902` condition | `StartStats` |
| `103/1709` × n | node | complete stats, same fields: the state that finishes the job | `CompleteStats` |

**Stats**

- `1900` = 0 is a single part. `1901` is a part number of car 1701 (`.car` 300); all 130 such references exist.
- `1900` = 1 is a region (`Region:` in the source). `1901` is 0 for the whole car, 1 engine, 2 body, 3 running gear. It *probably* stands for all stock (302 = 0) parts of that region, because custom parts are always listed one by one.
- `1902` is the condition: −1 part absent (removed), 0 black (beyond repair), 1 red, 2 yellow, 3 green.
- Entries apply in order, so later ones override earlier ones. One job removes a part and adds it back further down the same list.

More on jobs:
- **Start stats** begin with "whole car, 3", then change single parts: damaged, missing, or custom extras already fitted. One job lists only body and running gear, because the customer has removed the engine.
- **Complete stats** always use condition 3: "whole car, 3" plus each part the job is about, such as the custom parts the customer asked for or parts that were missing.
- **Texts:** each job has one request, one or more hints and one thanks. The tutorial uses up to five hints as step-by-step help, shown in turn. Request and hints share a portrait and the thanks has its own, so each job adds two portraits.
- **1703 (fee).** In several jobs the fee is exactly 100, 110 or 120 % of the summed maximum prices (306) of the parts that have to be bought: the source's `AUTO` percentage. The manual says the player's cash during a job is limited to the job budget, so the fee is also the money available for parts. Tutorial fees run 100, 125 … 300.
- **Unused source keys.** The source format also has `CarTex` (a texture instead of a colour) and `Anxiety` (described as unused). No shipped job uses them.

| file | jobs | job numbers | car id | portraits |
|---|---|---|---|---|
| tutorial | 9 | 0–8 | 4 and 8 | 18 |
| escort | 6 | 20–25 | 21 | 12 |
| cab | 4 | 30–33 | 6 | 8 |
| ecoline | 5 | 40–44 | 23 | 10 |
| tbird | 2 | 50–51 | 20 | 4 |
| f150 | 1 | 60 | 8 | 2 |
| mustang | 2 | 70–71 | 4 | 4 |
| fairmont | 2 | 80–81 | 31 | 4 |

## `.dpk`: decal pack (`2`)

A decal pack is compiled from 24-bit TGA files plus a text info file. Key names are taken from a fan-written guide.

`101` × n decal:

| id | type | meaning | info-file key |
|---|---|---|---|
| 1100 | int | decal id | `ID` |
| 1101 | string | name in the catalog | `Name` |
| 1102 | f32 | price of one order, dollars | `Cost` |
| 1103 | int | recolouring: 0 none, 1 tint all colours. The third option, `key` (recolour pure white only), is *probably* 2; no pack uses it. | `Color`: none / tint / key |
| 1104 | int | applications per order | `Num` |
| 1105 | image | 24 bpp, 29–128 px, not always square. Pure black (0, 0, 0) is transparent. | `File` |

Decals with 1103 = 1 are grayscale; those with 1103 = 0 are coloured (flags, flames). For transparency: every decal's corners are pure black, and a flag whose black stripe must stay visible stores that stripe as (18, 18, 18).

## `.mek`: mechanic (`3`)

Read from the saves the running game wrote, each compared with the one before it and with what the game showed (see
[FIDELITY](../FIDELITY.md)). `mek.tmp` is a copy of the last save written.

`102` mechanic, fields in file order:

| id | type | sample | meaning |
|---|---|---|---|
| 1401 | string | | name |
| 1400 | int | 0, 1 | **?** |
| 1403 | f32 | 3674.99 | cash |
| 1402 | int | 1 | skill level: 0 Learning, 1 Novice once the tutorial jobs are done (as in `.car` 210) |
| 1406 | int | 1 | cars you own (the sign-in screen's CARS) |
| 1404 | int | 0 … 16 | cars bought so far; it keeps counting after they are sold, and the next car bought gets it as its Number (1505) |
| 1405 | int | 1341 | total play time in seconds (the sign-in screen's TOTAL TIME) |
| 1410 | int | 0 … 15 | the Number (1505) of your car in the WorkShop; absent while the WorkShop is empty or holds a customer's car |
| 1407 × n | node | | parts kept for one car model: `1300` car id, then items `1301` × n and `1302` × n |
| 1408 × n | node | | one per car you own |
| 1409 × n | node | | a decal bought: `1600` decal id (`.dpk` 1100), `1601` uses left |
| 1411 | raw, 256 B | | jobs done: a bit set indexed by job number (1700), least significant bit first |
| 1412 | int | 1, 0 | 1 until the tutorial's jobs are done (Jobs Mode), then 0 |

Item (1301, 1302, 1506):
- `1200` part number
- `1201` car id
- `1202` condition, on the same scale as the jobs (0 black … 3 green)
- `1203` f32 in about −0.1..0.1: the part's own share of its condition value (see FIDELITY, "Money")

1302 is the JunkYard shelf of that car model, made when the model's first car comes in (what it holds: FIDELITY,
JunkYard); scrapped parts go back to it. 1301 is *probably* the Parts Bin's parts of that model.

1408, a car you own, in the order they were bought:
- `1500` car id
- `1501` int: 1 once "Car Complete" has come up for the car (its Repair Time stops counting there), 0 before
- `1502` time spent on it in the WorkShop, in milliseconds (the Car Lot's Repair Time)
- `1503` f32 money spent on it while you own it (Repair Cost)
- `1504` f32 what you paid for it (Orig Cost)
- `1505` int: its Number in the Car Lot, 1404 at the time it was bought (0 for the first car, then 1, 2 …; seen up to 15)
- `1506` × n installed parts (items)
- `1507` the paint texture: a texture node (`400` a generated name, `401` 1, `402` 256 × 256 RGB565) that the paint faces' UVs point into

OpenGG reads these saves to bring the original's mechanics over (`MekImport`: the IMPORT MECHANICS button
on the sign-in screen).

## Not in this format

`Data/*.dat` and `Data/Jobs/random.dat` are encrypted archives of TGA and WAV files, 3D scenes and the random jobs: see [dat-archives.md](dat-archives.md).

## How this was checked

- **Spec and car.** Every key of both spec files was matched to its compiled car. `RemoveDep` is not the computed inverse of 307: one car has eight parts mounted on its roof but an empty 308 on the roof.
- **Images.** The source `.tga` of one 8-bit texture ships next to the cars.
  - Its 256 palette entries appear in the car byte-swapped from B, G, R to R, G, B.
  - The TGA stores its 64 rows bottom-up; the car stores them in reverse, top row first.
  - 16 bpp: RGB565 gives the smoother image in 566 of 626 textures (the rest are flat colours), and a texture with known colours renders correctly only as RGB565.
  - 24 bpp: flag decals come out right only as B, G, R, and lettering in images is upright.
- **Source model and car.** Two cars ship with their `.3DS`; only one of these models has UVs. For that car:
  - car coordinates are (−x, z, −y) of the model's, plus an offset;
  - the vertex order is kept;
  - every triangle's corner order is reversed (19,003 of 19,003);
  - textured V is negated;
  - 601 is the diffuse colour and 606 is 1 − transparency.
- **Handedness.** Parts named front-left sit at −Z. The steering wheel is on that side in the US cars and at +Z in the right-hand-drive Australian cars.
- **Jobs.** 1706 matches the regions of the parts each job changes, and the condition colours follow the tutorial texts. The job-file guide supplies the key names, and its `AUTO` fee explains the exact 100 / 110 / 120 % ratios.
- **Skill.** The FAQ names five skill levels and says that two particular cars unlock at the top one. Both are stored with 210 = 4.
