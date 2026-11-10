# Generates xenostudio.ico: Xeno frame, Claude (clay) + Codex (green) chat bubbles.
# Run: <any python with Pillow> make_icon.py   (writes ..\xenostudio.ico)
import os
from PIL import Image, ImageDraw

S = 1024
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "xenostudio.ico")

def bubble(d, box, color, tail_left):
    x0, y0, x1, y1 = box
    h = y1 - y0
    d.rounded_rectangle(box, radius=int(h * 0.32), fill=color)
    if tail_left:
        tx = x0 + int((x1 - x0) * 0.18)
        d.polygon([(tx, y1 - 10), (tx + int(h * 0.30), y1 - 10), (tx - int(h * 0.08), y1 + int(h * 0.28))], fill=color)
    else:
        tx = x1 - int((x1 - x0) * 0.18)
        d.polygon([(tx, y1 - 10), (tx - int(h * 0.30), y1 - 10), (tx + int(h * 0.08), y1 + int(h * 0.28))], fill=color)

def dots(d, box, color):
    x0, y0, x1, y1 = box
    cy = (y0 + y1) // 2; w = x1 - x0; r = int((y1 - y0) * 0.085)
    for k in (0.32, 0.5, 0.68):
        cx = x0 + int(w * k)
        d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=color)

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
d.rounded_rectangle((24, 24, S - 24, S - 24), radius=210, fill=(30, 31, 34, 255), outline=(79, 156, 249, 255), width=34)
b1 = (150, 190, 700, 560)      # Claude, back
b2 = (330, 450, 880, 820)      # Codex, front
bubble(d, b1, (217, 119, 87, 255), True)
rim = (b2[0] - 30, b2[1] - 30, b2[2] + 30, b2[3] + 30)
d.rounded_rectangle(rim, radius=int((rim[3] - rim[1]) * 0.32), fill=(30, 31, 34, 255))
bubble(d, b2, (78, 201, 138, 255), False)
dots(d, b2, (30, 31, 34, 255))
img.save(OUT, sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
print("wrote", os.path.abspath(OUT))
