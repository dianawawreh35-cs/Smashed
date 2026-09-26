"""Makes the Agent App's and the supervisor app's icons from the Smashed logo.

    python -m pip install --target .pylib pillow      # once, anywhere
    PYTHONPATH=.pylib python tools/icons/make_icons.py

Reads tools/icons/smashed-logo.png, the client's logo (black circle, the word
SMASHED in white across a yellow burger outline), and writes:

    src/CallCenter.AgentApp/Assets/app.ico        the Agent App (exe, windows, taskbar)
    src/CallCenter.Web/public/favicon.ico         the supervisor app's browser tab
    src/CallCenter.Web/public/apple-touch-icon.png  bookmarks and home screens
    tools/icons/preview.png                       every size side by side, to look at

Three pictures, chosen by size. At 64 px and up it is the logo as it is. Below
that the word is a grey smudge, so 32 to 48 px is the logo's own burger: its
top and bottom halves closed up where the word ran through them, on the black
circle with its yellow rim. At 16 to 24 px even those outlines blur, so it is
the same burger drawn solid. Windows picks whichever size it needs, so a
shortcut shows the logo and the taskbar shows the burger.

Rerun it whenever the logo changes; the outputs are committed, so nothing runs
at build time.
"""

import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

REPO = Path(__file__).resolve().parents[2]
LOGO = REPO / "tools/icons/smashed-logo.png"

# Measured on the 659 x 659 logo: the circle, and the rows the burger's yellow
# lines occupy above and below the word.
CIRCLE_CENTRE = (332.5, 328.0)
CIRCLE_RADIUS = 306
BURGER_TOP_ROWS = (178, 296)      # the dome and the line under it
BURGER_BOTTOM_ROWS = (379, 427)   # the patty with its cheese and the bottom bun
BURGER_X = (215, 465)

YELLOW = (248, 224, 24, 255)      # the logo's burger yellow
BLACK = (0, 0, 0, 255)

ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
FAVICON_SIZES = [16, 32, 48]
MARK_BELOW = 64                   # sizes under this get the burger mark
SOLID_BELOW = 32                  # and under this, the burger drawn solid
WORK = 512                        # the mark is drawn at this size, then scaled down


def is_burger_yellow(p):
    r, g, b, a = p
    return a > 150 and r > 180 and g > 150 and b < 140


def burger_mask(logo):
    """The burger's yellow as a white-on-black mask, halves closed up."""
    px = logo.load()
    cx, cy = CIRCLE_CENTRE
    x0, x1 = BURGER_X
    (t0, t1), (b0, b1) = BURGER_TOP_ROWS, BURGER_BOTTOM_ROWS
    gap = 26                      # what separates the halves once the word is gone
    height = (t1 - t0) + gap + (b1 - b0)
    mask = Image.new("L", (x1 - x0, height), 0)
    out = mask.load()

    def copy(rows, dest_top):
        for y in range(*rows):
            for x in range(x0, x1):
                inside = math.hypot(x - cx, y - cy) < CIRCLE_RADIUS * 0.9
                if inside and is_burger_yellow(px[x, y]):
                    out[x - x0, dest_top + y - rows[0]] = 255

    copy(BURGER_TOP_ROWS, 0)
    copy(BURGER_BOTTOM_ROWS, (t1 - t0) + gap)
    return mask.crop(mask.getbbox())


def circle(size):
    """The black circle with its yellow rim, which keeps the edge visible on a
    dark taskbar."""
    img = Image.new("RGBA", (WORK, WORK), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    rim = max(1.0, size * 0.045) * WORK / size
    draw.ellipse((0, 0, WORK - 1, WORK - 1), fill=YELLOW)
    draw.ellipse((rim, rim, WORK - 1 - rim, WORK - 1 - rim), fill=BLACK)
    return img


def mark(burger, size):
    """32 to 48 px: the logo's own burger outlines on the circle."""
    img = circle(size)
    width = int(WORK * 0.64)
    scale = width / burger.width
    shape = burger.resize((width, round(burger.height * scale)), Image.LANCZOS)

    # The logo's lines are about 10 px of 659, which is under a pixel at 32.
    # Thicken them just to a pixel and a bit; any more and the burger's
    # layers run together into a block.
    line_now = 10 * scale
    line_wanted = max(1.3, size * 0.03) * WORK / size
    grow = int((line_wanted - line_now) / 2)
    if grow > 0:
        shape = shape.filter(ImageFilter.MaxFilter(2 * grow + 1))

    fill = Image.new("RGBA", shape.size, YELLOW)
    left = (WORK - shape.width) // 2
    top = (WORK - shape.height) // 2 + int(WORK * 0.01)
    img.paste(fill, (left, top), shape)
    return img.resize((size, size), Image.LANCZOS)


def solid_mark(size):
    """16 to 24 px: outlines cannot survive, so the same burger drawn solid:
    bun, patty, bottom bun, with a clean pixel of black between them."""
    img = circle(size)
    draw = ImageDraw.Draw(img)
    unit = WORK / size                        # one final pixel, in WORK units
    bun_w = WORK * 0.60
    gap = max(1.0, size * 0.06) * unit
    dome_h = bun_w * 0.40
    layer_h = max(2.0, size * 0.12) * unit
    total = dome_h + gap + layer_h + gap + layer_h
    left = (WORK - bun_w) / 2
    top = (WORK - total) / 2

    draw.pieslice((left, top, left + bun_w, top + 2 * dome_h), 180, 360, fill=YELLOW)
    y = top + dome_h + gap
    patty = bun_w * 1.06
    draw.rounded_rectangle(((WORK - patty) / 2, y, (WORK + patty) / 2, y + layer_h),
                           radius=layer_h / 2, fill=YELLOW)
    y += layer_h + gap
    draw.rounded_rectangle((left, y, left + bun_w, y + layer_h),
                           radius=layer_h / 2, fill=YELLOW)
    return img.resize((size, size), Image.LANCZOS)


def full_logo(logo, size):
    """The logo itself, trimmed to the circle and squared."""
    trimmed = logo.crop(logo.getchannel("A").getbbox())
    side = max(trimmed.size)
    square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    square.paste(trimmed, ((side - trimmed.width) // 2, (side - trimmed.height) // 2))
    return square.resize((size, size), Image.LANCZOS)


def icon(logo, burger, size):
    if size < SOLID_BELOW:
        return solid_mark(size)
    return mark(burger, size) if size < MARK_BELOW else full_logo(logo, size)


def save_ico(path, images):
    """One picture per size. Pillow would otherwise shrink the largest for all."""
    path.parent.mkdir(parents=True, exist_ok=True)
    largest = images[-1]
    largest.save(path, format="ICO", sizes=[i.size for i in images],
                 append_images=images[:-1])


def preview(images, path):
    pad = 12
    width = sum(i.width for i in images) + pad * (len(images) + 1)
    height = max(i.height for i in images) + 2 * pad
    sheet = Image.new("RGBA", (width, height * 2), (0, 0, 0, 0))
    ImageDraw.Draw(sheet).rectangle((0, 0, width, height), fill=(243, 243, 243, 255))
    ImageDraw.Draw(sheet).rectangle((0, height, width, 2 * height), fill=(32, 32, 32, 255))
    x = pad
    for i in images:
        for row in (0, height):
            sheet.alpha_composite(i, (x, row + height - pad - i.height))
        x += i.width + pad
    sheet.save(path)


def main():
    logo = Image.open(LOGO).convert("RGBA")
    burger = burger_mask(logo)

    app = [icon(logo, burger, s) for s in ICO_SIZES]
    save_ico(REPO / "src/CallCenter.AgentApp/Assets/app.ico", app)

    tab = [icon(logo, burger, s) for s in FAVICON_SIZES]
    save_ico(REPO / "src/CallCenter.Web/public/favicon.ico", tab)
    full_logo(logo, 180).save(REPO / "src/CallCenter.Web/public/apple-touch-icon.png")

    preview(app, REPO / "tools/icons/preview.png")
    print("wrote app.ico, favicon.ico, apple-touch-icon.png, preview.png")


if __name__ == "__main__":
    main()
