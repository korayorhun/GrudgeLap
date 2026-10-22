"""Draws the GrudgeLap icon (a red wedge on a dark disc, a hint of road) and writes the PNG used by the window
and the multi-size ICO used by the installer. Run with the user Python: python -I tools/make-icon.py"""
import sys, site, os
sys.path.append(site.getusersitepackages())
from PIL import Image, ImageDraw

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
size = 512
img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
# Dark disc with a thin orange rim (the shoulder of the road).
d.ellipse((8, 8, size - 8, size - 8), fill=(24, 26, 34, 255), outline=(200, 102, 30, 255), width=10)
# Road wedge receding to the horizon.
d.polygon([(150, 440), (362, 440), (300, 215), (212, 215)], fill=(72, 72, 78, 255))
for y in (420, 370, 325, 285, 250):
    w = 4 + (y - 215) // 14
    d.rectangle((256 - 3, y - w, 256 + 3, y), fill=(236, 200, 60, 255))
# The car: a low red wedge seen from behind, tail lamps as two bars.
body = [(118, 362), (394, 362), (384, 290), (330, 236), (182, 236), (128, 290)]
d.polygon(body, fill=(196, 16, 24, 255))
d.polygon([(190, 236), (322, 236), (300, 198), (212, 198)], fill=(12, 16, 24, 255))
d.rectangle((140, 310, 232, 332), fill=(255, 56, 40, 255))
d.rectangle((280, 310, 372, 332), fill=(255, 56, 40, 255))
d.rectangle((118, 340, 394, 362), fill=(18, 18, 20, 255))
d.rectangle((110, 356, 160, 400), fill=(20, 20, 22, 255))
d.rectangle((352, 356, 402, 400), fill=(20, 20, 22, 255))

assets = os.path.join(root, "game", "assets")
os.makedirs(assets, exist_ok=True)
img.save(os.path.join(root, "game", "icon.png"))
img.save(os.path.join(assets, "GrudgeLap.ico"), sizes=[(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print("icon written")
