# Changelog

Versions follow [Semantic Versioning](https://semver.org): **MAJOR** when saves or skins of an earlier version stop
working, **MINOR** for new features and changes to how the game plays, **PATCH** for fixes only. While the version is
0.x (the beta), a MINOR release may still break saves, and its notes say so.

## 0.2.0

Windows and Linux. Saves of 0.1.0 keep working.

- A Linux build (x86-64), in the release next to the Windows one; so far tested only without a graphics card
  (software Vulkan and OpenGL).
- OpenGG's own look letters in bundled open fonts (Anton, Archivo Black, Open Sans, a narrowed Barlow Condensed)
  instead of Windows' own, so it looks the same everywhere; the widths match, so the lettering fits as before.
- The original's look draws its 3D right on Godot's Compatibility renderer (OpenGL, used where there is no Vulkan or
  Direct3D 12): the cars and scenes came out far too dark there.
- OpenGG's own look: the WorkShop's view tabs sit on one band, as in the original, and the chosen one is a darker
  plate with its name in the other tabs' letters (it was in a wider face of its own).
- The original's look: the Catalog's part pictures take the Catalog's own light, measured on the original (they were
  lit as the WorkShop's car: tyres, fans and engine blocks came out too dark, sponsor panels too light).
- The Catalog no longer leaves a card empty when its picture takes more than 3 s to make.
- The sign-in's short game folder shows the system's own path separator.

## 0.1.0

The first public build, for Windows.

- Plays with your own copy of Gearhead Garage: its cars (all 165 of the tested copy), the Jobs Mode chain, Free
  Play's job packs and random jobs in the original's words, decals, customer portraits, screens, fonts, pointers,
  sounds and 3D scenes (Auction, Car Lot, JunkYard), read from the game folder at runtime.
- The rules as measured in the running game: prices and repairs to the cent, job fees and payouts, skill levels,
  the Auction's bidding, the JunkYard's shelves, the Car Lot's twelve bays, Start Engine, paint and decals.
- The WorkShop's view, light and camera fitted to the original's screenshots; the original's sounds where it plays
  them.
- Your `.mek` mechanics come over with IMPORT MECHANICS.
- Without the original: one generated placeholder car (the beta's one model), jobs and sounds of our own, and a look
  of its own; the first start says so.
- A BETA stamp on the sign-in sheet, with the version.
- A window or the full screen (F11, Alt+Enter), kept at 4:3.
