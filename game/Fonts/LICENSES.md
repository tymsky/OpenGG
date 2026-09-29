# Fonts

OpenGG's own look (the game without a skin) draws its lettering in these fonts, the same on every system. This
repository did not make them. All are under the **SIL Open Font License 1.1** (the `OFL-*.txt` files next to them):
free to use, bundle and change, not to sell on their own.

| File | Font | Source | In place of |
|---|---|---|---|
| `Anton-Regular.ttf` | Anton | [googlefonts/AntonFont](https://github.com/googlefonts/AntonFont) | Impact |
| `ArchivoBlack-Regular.ttf` | Archivo Black | [Omnibus-Type/ArchivoBlack](https://github.com/Omnibus-Type/ArchivoBlack) | Arial Black |
| `OpenGGNarrow-SemiBold.ttf`, `OpenGGNarrow-Bold.ttf` | Barlow Condensed, narrowed | [jpt/barlow](https://github.com/jpt/barlow) | Bahnschrift Condensed |
| `OpenSans-Variable.ttf` | Open Sans | [googlefonts/opensans](https://github.com/googlefonts/opensans) | Tahoma |

The files are as published in [google/fonts](https://github.com/google/fonts), but for OpenGG Narrow: Barlow
Condensed SemiBold and Bold drawn 7.5 % and 9 % narrower, without hinting, and renamed as the licence asks of a
modified version ([tools/fonts/narrow.py](../../tools/fonts/narrow.py)). Each font's widths are within a few per cent
of the one it stands in for, so the lettering fits where it was fitted with the Windows fonts.
