# OpenGG

An open-source engine for **Gearhead Garage: The Virtual Mechanic** (1999), in the spirit of OpenTTD, OpenRCT2 and
OpenMW. It plays the original game with **your own copy** of it: cars, jobs, decals, screens, sounds and 3D scenes are
read from your game folder at every start, and nothing is copied out of it.

**Beta, Windows** ([changes](CHANGELOG.md)). Rules, prices, screens, sounds and views follow the original as measured
in the running game ([docs/FIDELITY.md](docs/FIDELITY.md)). Expect rough edges.

## Playing

1. Download `OpenGG-…-windows-x64.zip` from [Releases](../../releases), unpack it anywhere and start `OpenGG.exe`.
   The program is not signed, so Windows may warn about an unknown publisher: **More info → Run anyway** (or
   [build it yourself](#building-from-source)). Windows 11 with Smart App Control turned on blocks unsigned programs
   outright, your own build too: there OpenGG will not run until it is signed.
2. On the sign-in sheet press **GAME FOLDER…** and pick your Gearhead Garage folder, the one with `Data\Cars`.
3. Create a mechanic with **NEW**, or bring your `.mek` saves over with **IMPORT MECHANICS** (cash, skill, cars,
   Parts Bin, decals).

Without the original, OpenGG plays a beta of its own: one generated placeholder car and jobs of its own, in a look of
its own.

### The game folder

- Usually `C:\Program Files (x86)\HeadGames\Gearhead Garage` (only the CD? install the game first). Its `Data` folder,
  or the folder above the game (`HeadGames`), works too; a folder without the game is refused with a message.
- It is remembered and only read: nothing is written there.
- A line on the sign-in sheet says what is missing: blue while no folder is picked; red when the folder has gone (the
  placeholders play until you pick it again) or has no archives (`Data\*.dat`: its cars and jobs then play in
  OpenGG's own look).
- **CONTENT** and **LOOK** switch between the original's and OpenGG's own.
- For one run: `OpenGG.exe -- --original "D:\Games\Gearhead Garage"`.

### Controls

The original's help (the `Help` folder of the installed game) is the manual; [FIDELITY](docs/FIDELITY.md) lists what
OpenGG does differently. Those marked * are OpenGG's own.

| | |
|---|---|
| Turn the view | hold the arrow keys (or the number pad's 2, 4, 6, 8), or drag with the right button |
| Zoom the WorkShop's view* | mouse wheel |
| Show Condition | hold its button (or **C***) |
| Take a part off, bolts and all* | **Shift**+click the part |
| Impact Wrench, Body Paint, Camera, Start Engine* | **1**, **2**, **3**, hold **4** |
| Bolt mode's CANCEL, stop aiming a decal* | **Esc** |
| Answer a box* | **Enter** (OK), **Esc** (cancel) |
| Full screen or a window* | **F11**, **Alt+Enter**, or **SCREEN** on the sign-in sheet |

The original's 640 × 480 screens are scaled at 4:3; **SCALE** on the sign-in sheet keeps them at whole multiples.
Saves and settings live in `%APPDATA%\Godot\app_userdata\OpenGG`.

## Assets

Without the original, OpenGG uses placeholders: the `ai_` files in `data/ai` (one car with its parts, textures,
sounds, icons, customer portraits and job data). They are **generated** by code in `tools/gen` written with an AI
assistant, not drawn, modelled or recorded by hand ([docs/ASSETS.md](docs/ASSETS.md)). They are stand-ins:
**hand-made assets are planned for 1.0**, so that OpenGG can be played on its own too; until then it is for playing
the original's content. The photographic surfaces in `data/hd` (asphalt, grass, planks, metal) are CC0 pictures from
ambientCG ([data/hd/LICENSES.md](data/hd/LICENSES.md)).

## Legal

- OpenGG is not affiliated with or endorsed by Ratloop, Inc., Mekada, Head Games, Activision or Snap-on. Gearhead
  Garage and the other names are their owners' trademarks.
- This repository contains **no code, assets or text from the original game**, and OpenGG ships none. The engine was
  written from scratch without decompiling or disassembling the original's programs; the file formats were worked out
  from the data files alone ([docs/formats](docs/formats)).
- To play with the original's content you need your own copy. Its files are read at runtime from the folder you pick;
  its encrypted archives are decrypted in memory, and nothing from them is written to disk.

## Building from source

Needs [Godot 4.7.1 .NET](https://godotengine.org/download) (the "mono" build) with its export templates, the .NET
SDK 8 and Node.js 22.18 or newer.

```bash
npm ci
npm run gen:assets        # the placeholder pack: data/ai
dotnet test core/OpenGG.Core.Tests
godot --path game         # run from source (or open game/project.godot in the editor)
godot --headless --path game --export-release "Windows Desktop" ../build/OpenGG/OpenGG.exe
```

An exported build looks for `data/ai` and `data/hd` next to `OpenGG.exe`. The version is `config/version` in
`game/project.godot`; pushing the tag `v` + that version (such as `v0.1.0`) makes
[.github/workflows/release.yml](.github/workflows/release.yml) build the Windows package and attach it to a release.

More: [ARCHITECTURE](docs/ARCHITECTURE.md) (how it fits together), [ORIGINAL_IMPORT](docs/ORIGINAL_IMPORT.md) (what is
read from the original, and how), [ASSETS](docs/ASSETS.md) (the placeholders), [formats](docs/formats) (the
original's files).

## Contributing

Bug reports, differences from the original and pull requests are welcome: see [CONTRIBUTING.md](CONTRIBUTING.md).
Questions and ideas go to [Discussions](../../discussions).

## Licence

OpenGG is free software under the [GNU General Public License, version 3](LICENSE), the generated placeholder pack
included. The pictures in `data/hd` are CC0.
