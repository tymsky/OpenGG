# `.dat` archives

The original keeps its 2D art, sounds, 3D scenes and the random jobs' words and faces in six archives. Since
2026-09-26, by the project owner's decision, OpenGG reads four of them in memory from the player's own copy
(`core/OpenGG.Core/Original/CLibArchive.cs`), so a player needs nothing but the game folder. Nothing from them is
written to disk or kept in this repository.

| File | Holds | Read |
|---|---|---|
| `Data/Gfx24.dat` | 91 pictures (TGA): the 640 × 480 screens (empty, every button up, every button down), dialog and overlay pictures, bitmap fonts, pointers | yes |
| `Data/Gfx8.dat` | the same pictures in 8-bit colour | no |
| `Data/Sound16.dat` | 83 sounds (WAV): buttons, tools, parts, dialogs, the auction, ambience, music | yes |
| `Data/Sound8.dat` | the same sounds in 8-bit | no |
| `Data/Scenes.dat` | 23 files: the Auction, Car Lot and JunkYard scenes (3DS) and their textures | yes |
| `Data/Jobs/random.dat` | the random jobs' phrase file and the customers' faces (JPEG) | yes |

A file is named by its archive and its path inside it: `Gfx24/workshopup.tga`, `Sound16/misc/error.wav`,
`Scenes/carlot.3ds`, `random/comments.txt`.

## Layout

- A plain-text notice ending in `1A 00` (78 bytes), then the body.
- The body opens with 8 bytes X and 8 bytes X XOR `cLib!317`; the files follow one after another.
- Each file: 28 bytes OpenGG does not need, a 288-byte name block (`dir/name.ext`, a NUL, filler), then its data: a
  zlib stream (with the file's Adler-32 at its end) or, for a few files, the file as it is. The next file's 28 bytes
  come straight after, so a file's data ends 28 bytes before the next name block.
- The first file, `the lstream header`, holds 8 bytes.
- Names and data are XORed with one 1024-byte table, wrapping every 1024 bytes: a name block with the table from its
  byte 28 on, a file's data from its byte 0 on.

The reader walks the body from name block to name block. A name block is known by decrypting to a name; a compressed
file ends only where its stream inflates whole and its Adler-32 matches, so a name look-alike inside compressed data is
passed over. A file is decrypted and inflated only when asked for.

## How it was worked out

From the data files alone; the executable was neither read nor run for it.

1. The same bytes open every file of an archive: a key stream that starts again for each file.
2. Names differing only in a digit (numbered sounds), and the resource names in the game's own log file, gave the
   table under the name blocks.
3. The rest of the table came from the compressed files: each has to stay a valid zlib stream and end with a matching
   Adler-32. The table repeats after 1024 bytes.
