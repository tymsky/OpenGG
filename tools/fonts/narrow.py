"""Barlow Condensed drawn 7.5 % narrower (its Bold 9 %), as wide as Bahnschrift's condensed width (the Windows font
OpenGG's own look was fitted with): game/Fonts/OpenGGNarrow-*.ttf. OFL allows modified versions under another name.

    pip install fonttools
    python tools/fonts/narrow.py path/to/BarlowCondensed-SemiBold.ttf path/to/BarlowCondensed-Bold.ttf
"""
import sys
from pathlib import Path

from fontTools.ttLib import TTFont

SCALES = {"SemiBold": 0.925, "Bold": 0.908}
FAMILY = "OpenGG Narrow"
OUT = Path(__file__).resolve().parents[2] / "game" / "Fonts"

for src in sys.argv[1:]:
    font = TTFont(src)
    style = "SemiBold" if "SemiBold" in src else "Bold"
    SCALE = SCALES[style]
    glyf, hmtx = font["glyf"], font["hmtx"]
    for name in font.getGlyphOrder():
        g = glyf[name]
        if g.isComposite():
            for c in g.components:
                c.x = round(c.x * SCALE)
        elif g.numberOfContours > 0:
            g.coordinates.scale((SCALE, 1))
            g.coordinates.toInt()
        g.removeHinting()  # made for the old outlines
        adv, lsb = hmtx[name]
        hmtx[name] = (round(adv * SCALE), round(lsb * SCALE))
    for table in ("fpgm", "prep", "cvt ", "hdmx", "LTSH", "VDMX"):
        if table in font:
            del font[table]
    font["head"].flags &= ~(1 << 4)  # no instructions left to change the widths
    for rec in font["name"].names:
        if rec.nameID in (1, 16):
            rec.string = FAMILY
        elif rec.nameID in (3, 4):
            rec.string = f"{FAMILY} {style}"
        elif rec.nameID == 6:
            rec.string = f"OpenGGNarrow-{style}"
    font.save(OUT / f"OpenGGNarrow-{style}.ttf")
    print(OUT / f"OpenGGNarrow-{style}.ttf")
