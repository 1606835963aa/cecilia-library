"""生成程序图标：塞西莉亚花，透明底。

用法：python scripts/make-icon.py
每个尺寸单独绘制；24 像素以下只画五片加宽的花瓣，省去花脉和花蕊，线条按尺寸加粗。
"""
import math
import pathlib

from PIL import Image, ImageDraw

root = pathlib.Path(__file__).resolve().parent.parent
output = root / "src" / "LightReader.App" / "app.ico"

INK = (23, 58, 51, 255)
PETAL = (253, 252, 247, 255)
SHADE = (207, 230, 223, 255)
GOLD = (196, 152, 64, 255)
FRONT = [((0, 0), (17, -22), (15, -66), (0, -88)), ((0, -88), (-15, -66), (-17, -22), (0, 0))]
WIDE = [((0, 0), (28, -20), (26, -66), (0, -90)), ((0, -90), (-26, -66), (-28, -20), (0, 0))]
BACK = [((0, 0), (12, -18), (11, -50), (0, -66)), ((0, -66), (-11, -50), (-12, -18), (0, 0))]


def bezier(p0, p1, p2, p3, steps=40):
    for i in range(steps + 1):
        t = i / steps
        a, b, c, d = (1 - t) ** 3, 3 * (1 - t) ** 2 * t, 3 * (1 - t) * t ** 2, t ** 3
        yield (a * p0[0] + b * p1[0] + c * p2[0] + d * p3[0], a * p0[1] + b * p1[1] + c * p2[1] + d * p3[1])


def petal(curves, angle, center, scale):
    r = math.radians(angle)
    points = [pt for curve in curves for pt in bezier(*curve)]
    return [(center + (x * math.cos(r) - y * math.sin(r)) * scale, center + (x * math.sin(r) + y * math.cos(r)) * scale) for x, y in points]


def render(size):
    factor = 8
    canvas = size * factor
    image = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    center = canvas / 2
    scale = canvas / 2 / 94
    line = max(canvas / 64, factor * (1.1 if size <= 24 else 1.0))
    small = size <= 24
    for angle in range(36, 360, 72) if not small else []:
        shape = petal(BACK, angle, center, scale)
        draw.polygon(shape, fill=SHADE)
        draw.line(shape + shape[:1], fill=INK, width=round(line * 0.85), joint="curve")
    for angle in range(0, 360, 72):
        shape = petal(WIDE if small else FRONT, angle, center, scale)
        draw.polygon(shape, fill=PETAL)
        draw.line(shape + shape[:1], fill=INK, width=round(line), joint="curve")
        if size >= 48:
            r = math.radians(angle)
            start, end = 12 * scale, 70 * scale
            draw.line([(center - math.sin(-r) * start, center - math.cos(r) * start), (center - math.sin(-r) * end, center - math.cos(r) * end)], fill=SHADE, width=round(line * 0.8))
    radius = (13 if size >= 32 else 19) * scale
    draw.ellipse([center - radius, center - radius, center + radius, center + radius], fill=GOLD, outline=INK, width=round(line * 0.7))
    if size >= 64:
        for angle in range(0, 360, 72):
            r = math.radians(angle + 36)
            x, y = center + math.sin(r) * 20 * scale, center - math.cos(r) * 20 * scale
            dot = 3 * scale
            draw.ellipse([x - dot, y - dot, x + dot, y + dot], fill=GOLD)
    return image.resize((size, size), Image.LANCZOS)


sizes = [16, 20, 24, 32, 40, 48, 64, 256]
images = [render(s) for s in sizes]
images[-1].save(output, format="ICO", sizes=[(s, s) for s in sizes], append_images=images[:-1])
images[-1].save(root / "tmp" / "icon-preview.png")
strip = Image.new("RGBA", (sum(sizes) + 10 * len(sizes), 256), (242, 238, 228, 255))
x = 0
for image in images:
    strip.alpha_composite(image, (x, 256 - image.size[1]))
    x += image.size[0] + 10
strip.save(root / "tmp" / "icon-sizes.png")
print(output, output.stat().st_size)
