"""Compose the Workshop cover from real gameplay; requires Pillow, no generated art."""

import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "release/media/cover-assets"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "release/media/preview.png")
    parser.add_argument("--font", type=Path, default=Path("C:/Windows/Fonts/msyhbd.ttc"))
    args = parser.parse_args()
    ink = "#292431"
    cream = "#f2dfaf"
    orange = "#db7950"
    mint = "#9dccba"
    scene = Image.open(ASSETS / "shop.png").convert("RGB")
    cover = Image.new("RGB", (1024, 1024), ink)
    # Keep the screenshot's proportions and pixel edges, with no invented game art.
    scene = scene.resize((1110, 669), Image.Resampling.NEAREST)
    cover.paste(scene, (-25, 285))
    overlay = Image.new("RGBA", cover.size)
    od = ImageDraw.Draw(overlay)
    for y in range(285, 425):
        alpha = int(245 * (1 - (y - 285) / 140))
        od.line((0, y, 1023, y), fill=(41, 36, 49, alpha))
    cover = Image.alpha_composite(cover.convert("RGBA"), overlay)
    draw = ImageDraw.Draw(cover)

    def text(value, size, x, y, color, stroke=0, stroke_fill=ink):
        # Render at half size, then enlarge with nearest-neighbour for pixel lettering.
        font = ImageFont.truetype(str(args.font), size // 2)
        box = font.getbbox(value)
        layer = Image.new("RGBA", (box[2] - box[0] + 24, box[3] - box[1] + 24))
        ImageDraw.Draw(layer).text((12 - box[0], 12 - box[1]), value, font=font,
                                  fill=color, stroke_width=stroke // 2,
                                  stroke_fill=stroke_fill)
        layer = layer.resize((layer.width * 2, layer.height * 2), Image.Resampling.NEAREST)
        cover.alpha_composite(layer, (x - 24, y - 24))

    # A shop-sign hierarchy, with one title and four short verbs.
    draw.rectangle((42, 38, 982, 42), fill=orange)
    text("PROBABLY STOLEN  /  MOD", 25, 54, 65, mint)
    text("深空管家", 216, 77, 141, "#5e393d", 8)
    text("深空管家", 216, 69, 131, cream, 8)
    draw = ImageDraw.Draw(cover)
    draw.polygon([(42, 351), (260, 351), (280, 371), (280, 403), (42, 403)], fill=orange)
    text("让开店更顺手", 28, 60, 363, ink)
    draw.rectangle((0, 920, 1023, 1023), fill=ink)
    draw.rectangle((42, 920, 982, 925), fill=orange)
    text("上货  /  分类  /  整理  /  装瓶", 40, 105, 953, cream)
    # Stepped border picks up the game's pixel UI instead of rounded dashboard cards.
    draw = ImageDraw.Draw(cover)
    draw.line([(18, 53), (18, 18), (53, 18)], fill=cream, width=4)
    draw.line([(971, 18), (1006, 18), (1006, 53)], fill=cream, width=4)
    draw.line([(18, 971), (18, 1006), (53, 1006)], fill=cream, width=4)
    draw.line([(971, 1006), (1006, 1006), (1006, 971)], fill=cream, width=4)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    cover.convert("RGB").save(args.output, optimize=True)
    print(args.output)


if __name__ == "__main__":
    main()
