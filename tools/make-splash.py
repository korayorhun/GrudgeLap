"""Draws the boot splash shown while the game loads (game/splash.png).
Usage: python -I make-splash.py <cockpit screenshot.png> <out.png>
The screenshot (a cockpit view taken with --screenshot) is cropped below the HUD's top bar, blurred and darkened
behind the title, so the window shows the game rather than the engine's default splash during the first seconds."""
import sys, site
sys.path.append(site.getusersitepackages())
from PIL import Image, ImageDraw, ImageFilter, ImageFont

src, out = sys.argv[1], sys.argv[2]
W, H = 1600, 900
shot = Image.open(src).convert("RGB")
# Drop the HUD's top bar and the strip at the foot, then fill the frame again.
body = shot.crop((0, 70, shot.width, shot.height - 40))
scale = max(W / body.width, H / body.height)
body = body.resize((round(body.width * scale), round(body.height * scale)), Image.LANCZOS)
left, top = (body.width - W) // 2, (body.height - H) // 2
body = body.crop((left, top, left + W, top + H)).filter(ImageFilter.GaussianBlur(3))
dark = Image.new("RGB", (W, H), (6, 6, 8))
img = Image.blend(body, dark, 0.62)

draw = ImageDraw.Draw(img)
fonts = "C:/Windows/Fonts/"
title = ImageFont.truetype(fonts + "segoeuib.ttf", 132)
small = ImageFont.truetype(fonts + "segoeuib.ttf", 30)
def centred(text, font, y, fill):
    w = draw.textlength(text, font=font)
    draw.text(((W - w) / 2, y), text, font=font, fill=fill)
centred("GRUDGELAP", title, 300, (245, 245, 240))
draw.rectangle((W / 2 - 220, 470, W / 2 + 220, 474), fill=(200, 24, 28))
centred("loading...", small, 520, (200, 200, 195))
img.save(out, optimize=True)
print(out, img.size)
