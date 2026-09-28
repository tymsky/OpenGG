# Playing with the original game's content

OpenGG plays with the content of **your own copy** of Gearhead Garage, read at runtime, as OpenTTD, OpenRCT2 and
OpenMW do.

## Rules we follow

- **No decompiling or disassembling the executables.** The formats come from the data files alone
  ([formats/tagged-files.md](formats/tagged-files.md)), the behaviour from the manual and from playing. In the EU,
  observing, studying and testing a program you may use is allowed (Directive 2009/24/EC art. 5(3); Polish copyright
  act art. 75(2)(2)), contract terms against it are void (art. 76), and file formats and functionality are not
  protected by copyright (CJEU C-406/10 *SAS Institute*).
- **The encrypted archives** (`Data/*.dat`: 2D screens, sounds, 3D scenes, random jobs) are decrypted in memory since
  2026-09-26, by the project owner's decision ([formats/dat-archives.md](formats/dat-archives.md)). Nothing from them
  is written to disk.
- **Nothing from the original is committed or shipped**: no files, extracted assets or texts. They are read at
  runtime from the folder the player picks, and nothing is copied out of it.

## What is read

| Original | Becomes | Code |
|---|---|---|
| `Data/Cars/*.car` | Cars: slots and parts with names, prices, regions, dependencies (AttachDep, RemoveDep, alternatives, AMEA), bolts and minimum skill; meshes, materials and textures, drawn on demand | `CarFile`, `OriginalGame.BuildCar`, `game/Scripts/Assets/OriginalSource.cs` |
| `Data/Jobs/*.jpk` | The Jobs Mode chain and the car packs' jobs: car, fee (= budget), paint colour, allowed tabs, request, hint and thanks texts, portraits, start and finishing state | `OrigJobPack`, `OriginalGame.AddJobs` |
| `Data/Decals/*.dpk` | Catalog decals: name, price, uses per purchase, picture (black is transparent) | `OrigDecalPack`, `OriginalGame.AddDecals` |
| `Data/Jobs/random.dat` | Free Play's random jobs: the phrase file (customers, sentences, variables) and the customers' faces | `OriginalArchives`, `OriginalGame.ReadOwnPhrases`, `PhraseFile` |
| `Data/Gfx24.dat`, `Data/Sound16.dat`, `Data/Scenes.dat` | The look: screens, dialog and overlay pictures, bitmap fonts, pointers, the Body Paint palette, sounds, the Auction, Car Lot and JunkYard scenes | `CLibArchive`, `OriginalArchives`, `game/Scripts/Ui/UiSkin.cs` with `game/Skins/original.json` |
| `Data/Mechanics/*.mek` | The original's saved mechanics, brought over by IMPORT MECHANICS… | `MekFile`, `MekImport` |

The rest comes from OpenGG's own pack: the tools, the rules (prices of repairs, the Auction's and so on, measured on
the running game: [FIDELITY.md](FIDELITY.md)), customer names, and, without the archives, the paint palette and our
own random Free Play jobs.

## Conversions

- **Space.** The original is left-handed, the front at −X, about 1.8 units per inch; OpenGG uses metres, the front at
  +X and right-handed axes, so X is negated and scaled, and rotations become `S R S`. Triangles are turned for Godot's
  clockwise front faces, using the stored normals.
- **Slots.** Parts sharing a mutual-exclusion bit share a slot (alternatives); the stock part is the default.
  Accessories (Special = 4) are optional.
- **Loops.** A few fan-made cars have loops such as "A before B before A", which would make the car impossible to take
  apart: the importer drops the closing link and logs it.
- **Static meshes.** Meshes no part uses become fixed pieces per region (shell, frame).
- **Bolts** are in car space; their unscrew direction is taken from the nearest face of the part.

## Using it

In the game **GAME FOLDER…** on the sign-in sheet (see the README); from source `godot --path game -- --original
"<folder>"` (and `--look original|opengg`); tests with `OPENGG_ORIGINAL="<folder>" dotnet test`. What is still
missing or differs: [FIDELITY.md](FIDELITY.md).
