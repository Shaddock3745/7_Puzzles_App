# 巧級的 App icon：海報／簡介上那個標誌（七片拼成正方形、綠色小三角掀起來）＋粉彩七色斜條、波浪邊的背景。
# 需要 Pillow 與 numpy。
#   py make_icon.py "<repo>\Assets\Icon" final   → 2026-11-04 使用者選定的那組（微扭），覆蓋 Assets/Icon 的四張：
#       Tangrade_icon.png                 一般 icon（1024，不透明；Player Settings 的 Default Icon、Android Legacy）
#       Tangrade_icon_round.png           圓形 icon（Android Round，API 25）
#       Tangrade_adaptive_background.png  自適應 icon 的背景（432）
#       Tangrade_adaptive_foreground.png  自適應 icon 的前景（432，透明；系統會切外圈再切成圓或圓角方形，標誌留在中間）
#   py make_icon.py <資料夾>                     → 比較用：直條紋、微扭、大扭三種並排的 preview.png
import math
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

OUT = Path(sys.argv[1])
OUT.mkdir(parents=True, exist_ok=True)
SUPER = 4   # 先畫 4 倍再縮，邊緣才平滑

PAPER, TEAL, INK = "#FAF6EE", "#40D5C5", "#2B2A33"
# 海報那組座標（正方形邊長 280）
PIECES = [
    ("#E8613C", [(0, 0), (280, 0), (140, 140)]),
    ("#E9A13B", [(0, 0), (140, 140), (0, 280)]),
    ("#A8C13F", [(280, 140), (280, 280), (140, 280)]),
    ("#3E9BD4", [(140, 140), (210, 210), (70, 210)]),
    ("#6B72C9", [(140, 140), (210, 70), (280, 140), (210, 210)]),
    ("#D4589B", [(0, 280), (70, 210), (210, 210), (140, 280)]),
]
LIFTED = ("#3FB58A", [(280, 0), (280, 140), (210, 70)])


def lift(points, dx=22, dy=-46, degrees=-20, center=(250, 70)):
    """掀起來的那片：繞自己附近轉一點、往右上挪。"""
    angle = math.radians(degrees)
    cx, cy = center
    out = []
    for x, y in points:
        x, y = x - cx, y - cy
        out.append((x * math.cos(angle) - y * math.sin(angle) + cx + dx, x * math.sin(angle) + y * math.cos(angle) + cy + dy))
    return out


def draw_mark(size, mark_fraction, background, seam):
    """size 見方的透明圖，標誌（含掀起來那片）佔 mark_fraction；seam＝片與片之間的縫的顏色。"""
    big = size * SUPER
    lifted = lift(LIFTED[1])
    xs = [p[0] for _, ps in PIECES for p in ps] + [p[0] for p in lifted]
    ys = [p[1] for _, ps in PIECES for p in ps] + [p[1] for p in lifted]
    width, height = max(xs) - min(xs), max(ys) - min(ys)
    scale = big * mark_fraction / max(width, height)
    ox = (big - width * scale) / 2 - min(xs) * scale
    oy = (big - height * scale) / 2 - min(ys) * scale
    to_canvas = lambda ps: [(ox + x * scale, oy + y * scale) for x, y in ps]

    layer = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    seam_width = max(2, round(7 * scale))
    for color, points in PIECES:
        draw.polygon(to_canvas(points), fill=color, outline=seam, width=seam_width)
    # 掀起來那片的影子
    shadow = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).polygon([(x + 0, y + 14 * scale) for x, y in to_canvas(lifted)], fill=(43, 42, 51, 90))
    shadow = shadow.filter(ImageFilter.GaussianBlur(10 * scale))
    layer = Image.alpha_composite(layer, shadow)
    draw = ImageDraw.Draw(layer)
    draw.polygon(to_canvas(lifted), fill=LIFTED[0], outline=seam, width=seam_width)
    return layer.resize((size, size), Image.LANCZOS)


def flat(size, color):
    return Image.new("RGBA", (size, size), color)


def rgb(hex_color):
    return tuple(int(hex_color[i:i + 2], 16) for i in (1, 3, 5))


def mix(a, b, u):
    return tuple(round(x + (y - x) * u) for x, y in zip(rgb(a) if isinstance(a, str) else a, rgb(b) if isinstance(b, str) else b))


def gradient(size):
    """青綠：左上淺、右下深，標誌後面一圈亮光。"""
    image = Image.new("RGBA", (size, size))
    pixels = image.load()
    light, deep = rgb("#6BE6D8"), rgb("#1FA9A8")
    for y in range(size):
        for x in range(size):
            u = (x + y) / (2 * size)
            d = math.hypot(x - size / 2, y - size / 2) / (size * .55)
            glow = max(0, 1 - d) ** 2 * .35
            pixels[x, y] = mix(mix(light, deep, u), (255, 255, 255), glow) + (255,)
    return image


def pastel_stripes(size):
    """海報的七色調淡，斜條紋。"""
    colors = ["#E8613C", "#E9A13B", "#A8C13F", "#3FB58A", "#3E9BD4", "#6B72C9", "#D4589B"]
    big = size * SUPER
    image = Image.new("RGBA", (big, big))
    draw = ImageDraw.Draw(image)
    band = big * 2 / 7
    for i in range(8):
        color = mix(colors[i % 7], "#FFFFFF", .62) + (255,)
        x0 = -big + i * band
        draw.polygon([(x0, big), (x0 + band, big), (x0 + band + big, 0), (x0 + big, 0)], fill=color)
    return image.resize((size, size), Image.LANCZOS)


def wavy_stripes(amplitude, wavelength):
    """粉彩七色斜條，邊界是波浪：每個點沿著條紋方向（v）讓它往垂直方向（u）偏一點，再看落在第幾條。
    amplitude、wavelength 都是以邊長為 1 的比例。"""
    import numpy as np
    colors = [mix(c, "#FFFFFF", .62) for c in ["#E8613C", "#E9A13B", "#A8C13F", "#3FB58A", "#3E9BD4", "#6B72C9", "#D4589B"]]

    def make(size):
        big = size * 3
        y, x = np.mgrid[0:big, 0:big] / big
        u = (x + y) / math.sqrt(2)          # 垂直於條紋：左上 0、右下最大（跟直條紋版同方向，左上從紅色開始）
        v = (x - y) / math.sqrt(2)          # 沿著條紋（從左下往右上）
        wave = amplitude * (np.sin(2 * np.pi * v / wavelength + .7) + .35 * np.sin(4 * np.pi * v / wavelength + 2.1))
        band = math.sqrt(2) / 7             # 跟直條紋版一樣寬
        index = np.floor((u + wave) / band).astype(int) % 7
        palette = np.array(colors, dtype=np.uint8)
        image = Image.fromarray(palette[index], "RGB").convert("RGBA")
        return image.resize((size, size), Image.LANCZOS)
    return make


def duo(size):
    """撞色：左上青綠、右下深藍紫，斜對切。"""
    big = size * SUPER
    image = Image.new("RGBA", (big, big), TEAL)
    ImageDraw.Draw(image).polygon([(big, big * .18), (big, big), (big * .18, big)], fill="#3D3F8F")
    return image.resize((size, size), Image.LANCZOS)


def confetti(size):
    """青綠底，散幾片半透明的白色小七巧板。"""
    big = size * SUPER
    image = Image.new("RGBA", (big, big), TEAL)
    overlay = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    shapes = [[(0, 0), (1, 0), (0, 1)], [(0, 0), (1, 0), (1, 1), (0, 1)], [(0, 0), (1, 0), (1.6, .6), (.6, .6)]]
    spots = [(.12, .14, .09, 20), (.86, .1, .07, -35), (.1, .84, .08, 55), (.88, .86, .1, 10), (.5, .07, .05, 70),
             (.06, .5, .05, -20), (.94, .48, .06, 40), (.52, .94, .06, -60)]
    for i, (cx, cy, s, degrees) in enumerate(spots):
        points = shapes[i % 3]
        angle = math.radians(degrees)
        mean_x = sum(p[0] for p in points) / len(points)
        mean_y = sum(p[1] for p in points) / len(points)
        moved = [(((x - mean_x) * math.cos(angle) - (y - mean_y) * math.sin(angle)) * s * big + cx * big,
                  ((x - mean_x) * math.sin(angle) + (y - mean_y) * math.cos(angle)) * s * big + cy * big) for x, y in points]
        draw.polygon(moved, fill=(255, 255, 255, 70))
    return Image.alpha_composite(image, overlay).resize((size, size), Image.LANCZOS)


def final():
    """使用者 11/4 選的：粉彩七色斜條、微扭。輸出 Unity 要的四張。"""
    background = wavy_stripes(.035, .75)
    icon = Image.alpha_composite(background(1024), draw_mark(1024, .74, None, "#FFFFFF"))
    icon.convert("RGB").save(OUT / "Tangrade_icon.png")                 # iOS 的 icon 不能有透明，存成 RGB
    mask = Image.new("L", (1024 * SUPER, 1024 * SUPER), 0)
    ImageDraw.Draw(mask).ellipse((0, 0, 1024 * SUPER - 1, 1024 * SUPER - 1), fill=255)
    round_icon = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
    round_icon.paste(icon, (0, 0), mask.resize((1024, 1024), Image.LANCZOS))
    round_icon.save(OUT / "Tangrade_icon_round.png")
    draw_mark(432, .44, None, "#FFFFFF").save(OUT / "Tangrade_adaptive_foreground.png")   # 系統會切外圈再切圓，.52 時綠色尖角太貼邊
    background(432).save(OUT / "Tangrade_adaptive_background.png")
    print("ok", OUT)


def main():
    if len(sys.argv) > 2 and sys.argv[2] == "final":
        return final()
    variants = {"stripes": (pastel_stripes, "#FFFFFF"),
                "wavy_soft": (wavy_stripes(.035, .75), "#FFFFFF"),
                "wavy_strong": (wavy_stripes(.07, .6), "#FFFFFF")}
    previews = []
    for name, (background, seam) in variants.items():
        icon = Image.alpha_composite(background(1024), draw_mark(1024, .74, None, seam))
        icon.save(OUT / f"icon_{name}_1024.png")
        # 自適應 icon：108dp 的畫布，系統會切掉外圈，只保證中間 72dp（66%）看得到 → 標誌放 52%
        draw_mark(432, .52, None, seam).save(OUT / f"adaptive_{name}_fg_432.png")
        background(432).save(OUT / f"adaptive_{name}_bg_432.png")
        previews.append(icon)

    # 預覽：大圖、圓形遮罩、手機桌面上的實際大小（48、72、96 px）
    sheet = Image.new("RGB", (1200, 370 * len(previews) + 20), "#3A3F52")
    for row, icon in enumerate(previews):
        y = 20 + row * 370
        sheet.paste(icon.resize((340, 340), Image.LANCZOS), (20, y))
        mask = Image.new("L", (340, 340), 0)
        ImageDraw.Draw(mask).ellipse((0, 0, 339, 339), fill=255)
        sheet.paste(icon.resize((340, 340), Image.LANCZOS), (390, y), mask)
        x = 770
        for s in (96, 72, 48):
            sheet.paste(icon.resize((s, s), Image.LANCZOS), (x, y + 170 - s // 2))
            x += s + 40
    sheet.save(OUT / "preview.png")
    print("ok", OUT)


main()

