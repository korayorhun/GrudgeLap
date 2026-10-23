"""Puts the Godot and Unreal pictures of the same shot side by side (or one above the other) with a caption.

Usage: python compare.py <out.jpg> <caption> <godot.png> <unreal.png> [<caption> <godot.png> <unreal.png> ...]
Each row is one shot: Godot on the left, Unreal on the right, both scaled to the same height.
"""
import sys

from PIL import Image, ImageDraw, ImageFont

WIDTH = 960      # each picture in the sheet
BAR = 34         # caption strip over each row


def font(size):
    for name in ("segoeuib.ttf", "arialbd.ttf", "DejaVuSans-Bold.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


def fit(path):
    image = Image.open(path).convert("RGB")
    return image.resize((WIDTH, round(image.height * WIDTH / image.width)), Image.LANCZOS)


def main():
    out, rest = sys.argv[1], sys.argv[2:]
    rows = [(rest[i], fit(rest[i + 1]), fit(rest[i + 2])) for i in range(0, len(rest), 3)]
    height = sum(BAR + max(g.height, u.height) for _, g, u in rows)
    sheet = Image.new("RGB", (WIDTH * 2 + 8, height), (18, 18, 20))
    draw = ImageDraw.Draw(sheet)
    y = 0
    for caption, godot, unreal in rows:
        draw.text((10, y + 6), f"{caption} - Godot 4.7 (today)", fill=(235, 235, 235), font=font(18))
        draw.text((WIDTH + 18, y + 6), f"{caption} - Unreal Engine 5.8 (feasibility)", fill=(235, 235, 235), font=font(18))
        sheet.paste(godot, (0, y + BAR))
        sheet.paste(unreal, (WIDTH + 8, y + BAR))
        y += BAR + max(godot.height, unreal.height)
    sheet.save(out, quality=90)
    print(out, sheet.size)


main()
