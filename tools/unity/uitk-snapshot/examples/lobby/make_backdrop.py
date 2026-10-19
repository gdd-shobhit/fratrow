# Lobby backdrop stand-in: dark room gradient + the four 3D preview slots at their projected
# screen positions (from Lobby.unity camera fov 74.7 and Player1-4 transforms), 1920x1080.
from pathlib import Path

from PIL import Image, ImageDraw

W, H = 1920, 1080
img = Image.new("RGB", (W, H))
d = ImageDraw.Draw(img)
for y in range(H):
    t = y / H
    d.line([(0, y), (W, y)], fill=(int(58 - 20 * t), int(52 - 18 * t), int(66 - 18 * t)))
d.rectangle([0, 760, W, H], fill=(44, 36, 34))  # floor
slots = [  # name, centre x, head y, feet y, width px
    ("P2", 570, 440, 788, 100), ("P1", 812, 439, 840, 118),
    ("P4", 1030, 437, 667, 66), ("P3", 1211, 441, 729, 82)]
for name, cx, top, bottom, w in sorted(slots, key=lambda s: s[3]):
    col = (120, 128, 150)
    head = int(w * 0.42)
    d.ellipse([cx - head // 2, top, cx + head // 2, top + head], fill=col)
    d.rounded_rectangle([cx - w // 2, top + head + 4, cx + w // 2, bottom], radius=w // 3, fill=col)
    d.text((cx - 8, bottom + 6), name, fill=(200, 205, 220))
out = Path(__file__).with_name("backdrop.png")
img.save(out)
print("wrote", out)
