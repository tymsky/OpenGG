# Architecture

```
 ai pack (data/ai, JSON + GLB/PNG/WAV/SVG) ──┐
                                             ├─► ContentPack ─► ContentIndex ─► Game (rules) ◄─► GameState (save, JSON)
 your copy of the original (Data/Cars ...) ──┘   (OriginalGame builds it)         │ events
                                                                                  ▼
 AssetStore ◄── ManifestSource (ai) + OriginalSource (.car meshes, portraits, decals, faces)
 UiSkin ◄── Skins/original.json over the original's archives (Data/*.dat, read in memory), or a skin folder
     │
     ▼
 App (Godot UI) ─► WorkshopScene / ShowroomScene ─► VehicleView (one car, reconciled with state)
```

```
core/OpenGG.Core/        rules and content, no Godot: Content/, Sim/, Original/
core/OpenGG.Core.Tests/  xUnit tests (every car taken apart and put back together, among others)
game/                    the Godot project: Scripts/App*.cs (screens, input), Scripts/View3D (scenes, car view,
                         picking), Scripts/Ui (classic look, dialogs, skins), Scripts/Assets, Skins/original.json
tools/gen/               generator of the placeholder pack (TypeScript on Node): npm run gen:assets -> data/ai/
data/hd/                 CC0 pictures for OpenGG's own look
```

## Core

`core/OpenGG.Core`: plain .NET 8, no Godot, tested by `core/OpenGG.Core.Tests`.

- **Content** (`Content/`): no game data is hard-coded. A `ContentPack` holds tools and fastener kinds, parts and cars
  (with slots and static pieces), job templates, decals, paints and the tunable `rules`. `Validation.Validate` checks
  every reference. The bundled pack's JSON schema is written by `tools/gen/types.ts` and read by `PackLoader`.
- **Rules** (`Sim/`): `Game` holds the state and the commands (`UseFastener`, `RemovePart`, `InstallPart`,
  `AcceptJob`, `ReserveJunk`, `PlaceBid`...); the UI only reads `Game.State` and calls them. A command returns a
  `Result` (a player-facing message on failure) and raises `GameEvent`s. The random generator's state is saved, so a
  seed and a list of commands always give the same game.
- **Original** (`Original/`): `TagReader` reads the original's tagged files and `CarFile`, `OrigJobPack` and
  `OrigDecalPack` decode them; `OriginalGame` builds a `ContentPack` from the player's copy, borrowing tools, rules,
  paints and names from the ai pack. `CLibArchive` and `OriginalArchives` read the encrypted archives (`Data/*.dat`) in
  memory: the look's pictures, sounds and scenes, the random jobs' phrase file and faces. See
  [ORIGINAL_IMPORT.md](ORIGINAL_IMPORT.md), [formats/tagged-files.md](formats/tagged-files.md) and
  [formats/dat-archives.md](formats/dat-archives.md).

## Game

`game/`: Godot 4.7 .NET.

- **`Main`** finds the data folder, loads the ai pack and, if set, the original on top (its content, and its look
  through `UiSkin`), then starts `App`. For testing, `-- --autoshot <dir>` screenshots every screen on a scripted tour;
  `-- --soundcheck <dir>` repeats the actions measured on the original, at the same places, logging their times, so
  the two recordings can be matched. `-- --replay <scenario.jsonl>` (run with Godot's `--fixed-fps 60`) plays a
  scenario with the mouse and keys on the 640 × 480 screen, the same file a script can play in the original, and saves
  its screenshots, films of chosen boxes (every frame, with its time) and the game's state (`StateDump`, the form an
  original's save is written in after `MekImport`); `--mechanics <folder>` brings the original's saves there over first.
- **`ScreenMode`**: a window sized to the screen or the full screen (F11, Alt+Enter, the settings); the 640 × 480
  game scaled at 4:3 with bars, at whole multiples if asked. `GameImage` is the game's picture without the bars
  (snapshots, the tours' screenshots).
- **`App`** (`App*.cs`): the screens (WorkShop, Catalog, JunkYard, Auction, Car Lot, Sign In), input and sounds, in a
  fixed 640 × 480 layout like the original's (`App.Layout.cs`), drawn in code (`Ui/Look.cs`, `Ui/Classic*.cs`).
- **`AssetStore`** turns asset ids into Godot resources: `ManifestSource` for the ai pack (glTF loaded at runtime,
  PNG, SVG, WAV), `OriginalSource` for `.car` meshes (made into `ArrayMesh` with materials and textures), portraits,
  decals and the random jobs' faces.
- **`VehicleView`** mirrors one vehicle's state in 3D and animates the differences (bolts spinning out, parts sliding
  off, the hood swinging). It reconciles against the state instead of replaying events, so it cannot drift out of
  sync.
- **`WorkshopScene`**: the car on the original's plain grey, the orbit camera placed by **`WorkshopRig`** (the
  original's tilt, fitted to its screenshots). **`ShowroomScene`**: the Auction and the Car Lot (your cars in its
  bays, the camera gliding along the lane). **`JunkyardScene`**: the camera gliding along each area's path. With the
  original's `.3ds` scenes (or a skin's) they light them as the original does (`Model3ds.SceneLight`, fitted per
  scene), and the cars and parts on them through `OrigLook`.
- **`OrigLook`** draws the original's cars as its engine lit them (per-vertex light in stored colour numbers, colours
  doubled, a highlight pass on body meshes), with values fitted to screenshots; `--lab <script.json>` renders the
  WorkShop at given cameras and lights for such fits.
- **`ModernLook`**: OpenGG's own 3D look, when no skin is active. It restyles the placeholder materials as they load
  (`AssetStore.Styler`: clear-coat paint, mirror chrome, grained metals and rubber), sets up the WorkShop's studio (the
  original's grey behind; a generated dark studio with softboxes for the reflections, turned with the view like the
  key, fill and rim lights; soft and contact shadows) and the outdoor daylight, and grades the CC0 surfaces of
  `data/hd` to the placeholders' palette. The 3D is then drawn at the screen's resolution: the `SubViewport` is
  screen-sized but keeps a 640 × 480 2D size, so camera, picking and layout are unchanged.
- **`Picker`** casts rays against mesh triangles on the CPU through a bounding volume hierarchy per mesh (`MeshBvh`,
  built at the first ray), with proxy boxes for bolts and ghost parts; no physics bodies.
- **`UiSkin`** (`Ui/UiSkin.cs`) supplies the look's screens, dialog pictures, bitmap fonts, cursors, sounds and `.3ds`
  scenes: the original's (`Skins/original.json`, built in, over the player's archives) or a skin's. A skin is a folder
  whose `skin.json` maps each screen (empty, every button up, every button down), picture, strip font, cursor, sound
  id and scene to its files, and can name a phrase file for the random jobs (`jobPhrases`) and reword any sentence
  (`texts`). With `"extends": "original"` it changes only what it names (`SkinFiles`: its folder first, then the
  archives). Start one with `--skin <folder>` or `skinFolder` in `settings.json`.
- **`Words`** (`Ui/Words.cs`): every sentence of the dialogs, hints and messages, by key, in OpenGG's words.

## Game flow (as in the original)

- **Mechanics** have cash, a skill level and play time. The skill level needs both cash ever held and cars repaired.
- **Jobs Mode, then Free Play.** A new mechanic works through the tutorial pack's chain of jobs
  (`JobTemplateDef.Sequence`); the Car Lot and the Auction open in Free Play. Free Play's jobs come from the car job
  packs (each done once) and at random: `Phrased` templates make up the damage and the customer's words from the pack's
  `JobPhraseBook` (the original's phrase file, read by `PhraseFile`, written by `PhraseWriter`), or, without one, our
  own templates with fixed texts.
- **A job** comes with a car in a given state (`Start`), a fee that is also its budget and the workshop tabs it allows
  (`Tabs`). It finishes by itself when the car matches `Complete` and is fully assembled, paying the fee plus the
  budget left. Job Help shows the hints in turn; it restarts the job in Jobs Mode and gives it up in Free Play.
- **Your own cars**: purchases come out of your cash and add to the car's repair cost, and time in the WorkShop counts
  as repair time. **Auction Car** sells one.

## The assembly model ("bolt-em up")

A car is a set of **slots**; alternatives (the original's mutually exclusive parts, such as a hard top or a
convertible) share one.

- A slot has `Parents` (what its default part is mounted on) and `BlockedBy` (parts in the way). A part can add its own
  `Mounts` (with `MountsAny`, any one is enough: the original's AMEA) and `RemoveAfter` (what must come off first: the
  original's RemoveDep).
- Parts carry fasteners: kind, position and direction (zero = worked out from the mesh).
- A part comes off when all its bolts are out, nothing is mounted on it and nothing in its `RemoveAfter` is still on.
  It goes on when its slot is empty, it fits and its parents and mounts are in place; it goes on loose, to be bolted.
- `core/OpenGG.Core.Tests` takes every car apart and puts it back: the placeholder car, and every original car when
  `OPENGG_ORIGINAL` is set.

## Adding a car to the ai pack

1. A body spec in `tools/gen/body.ts`: silhouette, windows, lights.
2. The car in `buildCars()` in `tools/gen/cars.ts`, its id in `PACK_CARS` (the beta builds only the sedan): engine (`addI4`, `addV8` or a new layout), cooling, exhaust route
   and chassis. `addBody(..., gen2 = true)` builds the body in the second generation (`body2.ts`: curved sheet-metal
   panels, rounded edges, lamps, grille, bumpers, seals); so far only the sedan's.
3. `npm run gen:assets && dotnet test`, then look at it: `-- --lab <script.json> --view-scale 3` renders the WorkShop
   at given angles (a `"model"` shot shows one model alone, a `"boltSlot"` shot a part's bolts).
