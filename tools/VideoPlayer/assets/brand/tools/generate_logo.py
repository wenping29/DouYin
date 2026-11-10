#!/usr/bin/env python3
"""Generate the VideoPlayer brand assets (SVG / PNG / ICO / preview sheet).

Design: "Neon Minimal" dark plate with a cyan -> violet gradient play mark.
All geometry is defined once in a 1024x1024 design space, so the vector
sources and every raster size stay pixel-consistent.

Usage:  python assets/brand/tools/generate_logo.py
"""

from __future__ import annotations

import struct
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

# --------------------------------------------------------------------------
# Design space (1024 x 1024)
# --------------------------------------------------------------------------
VB = 1024.0

PLATE_XY = 40.0
PLATE_SIDE = 944.0
PLATE_R = 214.0

# Play mark: bounding box centred on the plate (x 331..693, y 306..718)
TRI = [(331.0, 306.0), (331.0, 718.0), (693.0, 512.0)]
CORNER_D = 100.0
CORNER_SEG = 26
COMPACT_SCALE = 1.09  # optical size boost for 16-32px icons

BG_STOPS = [
    (0.00, (27, 32, 46)),
    (0.52, (13, 16, 26)),
    (1.00, (5, 7, 13)),
]
BG_P0, BG_P1 = (40.0, 40.0), (984.0, 984.0)

GLOW_C = ((320.0, 352.0), 500.0, (34, 211, 238), 0.20)
GLOW_V = ((748.0, 792.0), 560.0, (139, 92, 246), 0.24)

SHEEN_P0, SHEEN_P1 = (40.0, 40.0), (984.0, 984.0)
SHEEN_STOPS = [(0.00, 0.0), (0.28, 0.0), (0.40, 0.062), (0.50, 0.028), (0.62, 0.0), (1.00, 0.0)]

PLAY_STOPS = [
    (0.00, (43, 231, 245)),
    (0.55, (110, 94, 255)),
    (1.00, (186, 92, 255)),
]
PLAY_P0, PLAY_P1 = (345.0, 306.0), (700.0, 710.0)

GLOW_BLUR = 34.0
GLOW_STRENGTH = 0.55

BORDER_ALPHA = 0.10
BORDER_W = 2.0

MONO_PAD = 0.08  # transparent-edge padding for the standalone mark

# Palette (used by the preview sheet and the XAML snippet)
C_CYAN = "#2BE7F5"
C_INDIGO = "#6E5EFF"
C_VIOLET = "#BA5CFF"
C_INK = "#0C0F18"
C_SLATE = "#191E2B"
C_TEXT = "#E9EDF6"

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT


# --------------------------------------------------------------------------
# Small math helpers
# --------------------------------------------------------------------------
def rounded_polygon(poly, d, seg=CORNER_SEG):
    """Return a densely sampled rounded polygon (quadratic-corner fillets)."""
    n = len(poly)
    corners = []
    for i, v in enumerate(poly):
        vi = np.array(v, dtype=np.float64)
        prev = np.array(poly[i - 1], dtype=np.float64) - vi
        nxt = np.array(poly[(i + 1) % n], dtype=np.float64) - vi
        prev /= np.linalg.norm(prev)
        nxt /= np.linalg.norm(nxt)
        corners.append((vi + prev * d, vi, vi + nxt * d))

    pts = [corners[0][2]]
    for i in range(1, n + 1):
        t_in, v, t_out = corners[i % n]
        pts.append(t_in)
        for s in range(1, seg + 1):
            t = s / seg
            pts.append((1 - t) ** 2 * t_in + 2 * (1 - t) * t * v + t * t * t_out)
    return [(float(p[0]), float(p[1])) for p in pts]


TRI_PTS = rounded_polygon(TRI, CORNER_D)


def _proj(W, H, p0, p1):
    ys, xs = np.mgrid[0:H, 0:W]
    dx, dy = p1[0] - p0[0], p1[1] - p0[1]
    denom = dx * dx + dy * dy
    t = ((xs - p0[0]) * dx + (ys - p0[1]) * dy) / denom
    return np.clip(t, 0.0, 1.0)


def gradient_rgba(W, H, p0, p1, stops):
    """Linear gradient with multiple stops -> float RGBA array (0..255)."""
    t = _proj(W, H, p0, p1)
    pos = np.array([s[0] for s in stops], dtype=np.float64)
    out = np.empty((H, W, 4), dtype=np.float32)
    for ch in range(3):
        vals = np.array([s[1][ch] for s in stops], dtype=np.float64)
        out[:, :, ch] = np.interp(t, pos, vals)
    out[:, :, 3] = 255.0
    return out


def radial_alpha(W, H, center, radius):
    ys, xs = np.mgrid[0:H, 0:W]
    d = np.sqrt((xs - center[0]) ** 2 + (ys - center[1]) ** 2) / radius
    return np.clip(1.0 - d, 0.0, 1.0) ** 2


# --------------------------------------------------------------------------
# Raster renderer
# --------------------------------------------------------------------------
def render(px: int, ss: int = 4, compact: bool | None = None) -> Image.Image:
    if compact is None:
        compact = px <= 48
    W = px * ss
    k = W / VB

    def s(x):
        return x * k

    # optional optical boost for tiny icons
    tri_pts = TRI_PTS
    tri_glow = GLOW_STRENGTH
    if compact:
        f = COMPACT_SCALE
        tri_pts = [(512.0 + (x - 512.0) * f, 512.0 + (y - 512.0) * f) for x, y in TRI_PTS]
        tri_glow = 0.25

    arr = np.zeros((W, W, 4), dtype=np.float32)

    # --- plate mask -------------------------------------------------------
    plate = Image.new("L", (W, W), 0)
    ImageDraw.Draw(plate).rounded_rectangle(
        [s(PLATE_XY), s(PLATE_XY), s(PLATE_XY + PLATE_SIDE) - 1, s(PLATE_XY + PLATE_SIDE) - 1],
        radius=s(PLATE_R),
        fill=255,
    )
    plate_a = np.asarray(plate, dtype=np.float32) / 255.0

    # --- base gradient ----------------------------------------------------
    bg = gradient_rgba(W, W, (s(BG_P0[0]), s(BG_P0[1])), (s(BG_P1[0]), s(BG_P1[1])), BG_STOPS)
    arr[:, :, :3] = bg[:, :, :3]

    # --- ambient neon -----------------------------------------------------
    if not compact:
        for center, radius, color, strength in (GLOW_C, GLOW_V):
            a = radial_alpha(W, W, (s(center[0]), s(center[1])), s(radius)) * strength
            for ch in range(3):
                arr[:, :, ch] += a * color[ch]

    # --- glass sheen (soft diagonal band) ---------------------------------
    if not compact:
        st = _proj(W, W, (s(SHEEN_P0[0]), s(SHEEN_P0[1])), (s(SHEEN_P1[0]), s(SHEEN_P1[1])))
        pos = np.array([p for p, _ in SHEEN_STOPS], dtype=np.float64)
        alpha = np.interp(st, pos, np.array([a for _, a in SHEEN_STOPS], dtype=np.float64))
        for ch in range(3):
            arr[:, :, ch] += alpha * (255.0 - arr[:, :, ch])

    # --- clip to plate ----------------------------------------------------
    arr[:, :, 3] = np.clip(arr[:, :, 3], 0, 255)
    arr[:, :, :3] *= plate_a[:, :, None]
    arr[:, :, 3] = plate_a * 255.0

    # --- hairline border --------------------------------------------------
    border = Image.new("L", (W, W), 0)
    ImageDraw.Draw(border).rounded_rectangle(
        [s(PLATE_XY) + s(BORDER_W) / 2, s(PLATE_XY) + s(BORDER_W) / 2,
         s(PLATE_XY + PLATE_SIDE) - 1 - s(BORDER_W) / 2,
         s(PLATE_XY + PLATE_SIDE) - 1 - s(BORDER_W) / 2],
        radius=s(PLATE_R) - s(BORDER_W) / 2,
        outline=255,
        width=max(1, round(s(BORDER_W))),
    )
    b_a = np.asarray(border, dtype=np.float32) / 255.0 * BORDER_ALPHA

    # --- play mark --------------------------------------------------------
    tri = Image.new("L", (W, W), 0)
    ImageDraw.Draw(tri).polygon([(s(x), s(y)) for x, y in tri_pts], fill=255)

    tri_grad = gradient_rgba(
        W, W, (s(PLAY_P0[0]), s(PLAY_P0[1])), (s(PLAY_P1[0]), s(PLAY_P1[1])), PLAY_STOPS
    )

    # neon emission: blurred copy of the gradient, added on top
    glow_rgb = tri_grad.copy()
    glow_rgb[:, :, 3] = np.asarray(tri, dtype=np.float32)
    glow_img = Image.fromarray(np.clip(glow_rgb, 0, 255).astype(np.uint8), "RGBA")
    glow_img = glow_img.filter(ImageFilter.GaussianBlur(s(GLOW_BLUR)))
    glow = np.asarray(glow_img, dtype=np.float32)
    ga = glow[:, :, 3] / 255.0 * tri_glow
    for ch in range(3):
        arr[:, :, ch] += glow[:, :, ch] * ga

    # solid mark with a light-from-above highlight
    m_a = np.asarray(tri, dtype=np.float32) / 255.0
    top = min(y for _, y in tri_pts)
    bottom = max(y for _, y in tri_pts)
    hi_t = _proj(W, W, (0.0, s(top)), (0.0, s(bottom)))
    hi = (1.0 - hi_t) * 0.20
    for ch in range(3):
        col = tri_grad[:, :, ch] * (1.0 - hi) + 255.0 * hi
        arr[:, :, ch] = arr[:, :, ch] * (1.0 - m_a) + col * m_a

    # --- border on top ----------------------------------------------------
    for ch in range(3):
        arr[:, :, ch] = arr[:, :, ch] * (1.0 - b_a) + 255.0 * b_a * plate_a

    img = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGBA")
    if ss > 1:
        img = img.resize((px, px), Image.Resampling.LANCZOS)
    return img


# --------------------------------------------------------------------------
# SVG writers (same constants)
# --------------------------------------------------------------------------
def _f(v: float) -> str:
    return f"{v:.2f}".rstrip("0").rstrip(".")


def _hex(rgb) -> str:
    return "#{:02X}{:02X}{:02X}".format(*rgb)


def _svg_stops(stops, attr="stop-color"):
    out = []
    for pos, rgb in stops:
        out.append(f'      <stop offset="{_f(pos)}" {attr}="{_hex(rgb)}"/>')
    return "\n".join(out)


def svg_icon() -> str:
    tri_path = _svg_tri_path()

    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024" width="1024" height="1024" role="img" aria-label="VideoPlayer">
  <title>VideoPlayer</title>
  <defs>
    <linearGradient id="vp-bg" x1="{_f(BG_P0[0])}" y1="{_f(BG_P0[1])}" x2="{_f(BG_P1[0])}" y2="{_f(BG_P1[1])}" gradientUnits="userSpaceOnUse">
{_svg_stops(BG_STOPS)}
    </linearGradient>
    <radialGradient id="vp-glow-cyan" cx="{_f(GLOW_C[0][0])}" cy="{_f(GLOW_C[0][1])}" r="{_f(GLOW_C[1])}" gradientUnits="userSpaceOnUse">
      <stop offset="0" stop-color="{_hex(GLOW_C[2])}" stop-opacity="{_f(GLOW_C[3])}"/>
      <stop offset="1" stop-color="{_hex(GLOW_C[2])}" stop-opacity="0"/>
    </radialGradient>
    <radialGradient id="vp-glow-violet" cx="{_f(GLOW_V[0][0])}" cy="{_f(GLOW_V[0][1])}" r="{_f(GLOW_V[1])}" gradientUnits="userSpaceOnUse">
      <stop offset="0" stop-color="{_hex(GLOW_V[2])}" stop-opacity="{_f(GLOW_V[3])}"/>
      <stop offset="1" stop-color="{_hex(GLOW_V[2])}" stop-opacity="0"/>
    </radialGradient>
    <linearGradient id="vp-sheen" x1="{_f(SHEEN_P0[0])}" y1="{_f(SHEEN_P0[1])}" x2="{_f(SHEEN_P1[0])}" y2="{_f(SHEEN_P1[1])}" gradientUnits="userSpaceOnUse">
{chr(10).join(f'      <stop offset="{_f(p)}" stop-color="#FFFFFF" stop-opacity="{_f(a)}"/>' for p, a in SHEEN_STOPS)}
    </linearGradient>
    <linearGradient id="vp-play" x1="{_f(PLAY_P0[0])}" y1="{_f(PLAY_P0[1])}" x2="{_f(PLAY_P1[0])}" y2="{_f(PLAY_P1[1])}" gradientUnits="userSpaceOnUse">
{_svg_stops(PLAY_STOPS)}
    </linearGradient>
    <linearGradient id="vp-hi" x1="0" y1="{_f(TRI[0][1])}" x2="0" y2="{_f(TRI[1][1])}" gradientUnits="userSpaceOnUse">
      <stop offset="0" stop-color="#FFFFFF" stop-opacity="0.20"/>
      <stop offset="1" stop-color="#FFFFFF" stop-opacity="0"/>
    </linearGradient>
    <filter id="vp-neon" x="-25%" y="-25%" width="150%" height="150%" color-interpolation-filters="sRGB">
      <feGaussianBlur stdDeviation="{_f(GLOW_BLUR)}"/>
    </filter>
    <clipPath id="vp-plate">
      <rect x="{_f(PLATE_XY)}" y="{_f(PLATE_XY)}" width="{_f(PLATE_SIDE)}" height="{_f(PLATE_SIDE)}" rx="{_f(PLATE_R)}"/>
    </clipPath>
  </defs>

  <g clip-path="url(#vp-plate)">
    <rect x="{_f(PLATE_XY)}" y="{_f(PLATE_XY)}" width="{_f(PLATE_SIDE)}" height="{_f(PLATE_SIDE)}" fill="url(#vp-bg)"/>
    <rect x="{_f(PLATE_XY)}" y="{_f(PLATE_XY)}" width="{_f(PLATE_SIDE)}" height="{_f(PLATE_SIDE)}" fill="url(#vp-glow-cyan)" style="mix-blend-mode:screen"/>
    <rect x="{_f(PLATE_XY)}" y="{_f(PLATE_XY)}" width="{_f(PLATE_SIDE)}" height="{_f(PLATE_SIDE)}" fill="url(#vp-glow-violet)" style="mix-blend-mode:screen"/>
    <rect x="{_f(PLATE_XY)}" y="{_f(PLATE_XY)}" width="{_f(PLATE_SIDE)}" height="{_f(PLATE_SIDE)}" fill="url(#vp-sheen)"/>
  </g>

  <path d="{tri_path}" fill="url(#vp-play)" filter="url(#vp-neon)" opacity="{_f(GLOW_STRENGTH)}"/>
  <path d="{tri_path}" fill="url(#vp-play)"/>
  <path d="{tri_path}" fill="url(#vp-hi)"/>

  <rect x="{_f(PLATE_XY + BORDER_W / 2)}" y="{_f(PLATE_XY + BORDER_W / 2)}"
        width="{_f(PLATE_SIDE - BORDER_W)}" height="{_f(PLATE_SIDE - BORDER_W)}"
        rx="{_f(PLATE_R - BORDER_W / 2)}" fill="none" stroke="#FFFFFF" stroke-opacity="{_f(BORDER_ALPHA)}" stroke-width="{_f(BORDER_W)}"/>
</svg>
"""


def svg_lockup() -> str:
    """Horizontal lockup: mark + wordmark (text uses system UI font stack)."""
    mark = 256.0
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1180 320" width="1180" height="320" role="img" aria-label="VideoPlayer">
  <title>VideoPlayer — logo lockup</title>
  <defs>
    <linearGradient id="lk-bg" x1="0" y1="0" x2="1" y2="1" gradientUnits="objectBoundingBox">
{_svg_stops(BG_STOPS)}
    </linearGradient>
    <radialGradient id="lk-glow-cyan" cx="0.3" cy="0.32" r="0.55">
      <stop offset="0" stop-color="{_hex(GLOW_C[2])}" stop-opacity="{_f(GLOW_C[3])}"/>
      <stop offset="1" stop-color="{_hex(GLOW_C[2])}" stop-opacity="0"/>
    </radialGradient>
    <radialGradient id="lk-glow-violet" cx="0.76" cy="0.8" r="0.6">
      <stop offset="0" stop-color="{_hex(GLOW_V[2])}" stop-opacity="{_f(GLOW_V[3])}"/>
      <stop offset="1" stop-color="{_hex(GLOW_V[2])}" stop-opacity="0"/>
    </radialGradient>
    <linearGradient id="lk-play" x1="0" y1="0" x2="1" y2="1" gradientUnits="objectBoundingBox">
{_svg_stops(PLAY_STOPS)}
    </linearGradient>
    <filter id="lk-neon" x="-25%" y="-25%" width="150%" height="150%">
      <feGaussianBlur stdDeviation="8"/>
    </filter>
    <clipPath id="lk-plate">
      <rect width="{_f(mark)}" height="{_f(mark)}" rx="{_f(PLATE_R / VB * mark)}"/>
    </clipPath>
    <linearGradient id="lk-text" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#FFFFFF"/>
      <stop offset="1" stop-color="#C9D2E4"/>
    </linearGradient>
  </defs>

  <g transform="translate(0, 32)">
    <g clip-path="url(#lk-plate)">
      <rect width="{_f(mark)}" height="{_f(mark)}" fill="url(#lk-bg)"/>
      <rect width="{_f(mark)}" height="{_f(mark)}" fill="url(#lk-glow-cyan)" style="mix-blend-mode:screen"/>
      <rect width="{_f(mark)}" height="{_f(mark)}" fill="url(#lk-glow-violet)" style="mix-blend-mode:screen"/>
    </g>
    <g transform="scale({_f(mark / VB)})">
      <path d="{_svg_tri_path()}" fill="url(#lk-play)" filter="url(#lk-neon)" opacity="0.5"/>
      <path d="{_svg_tri_path()}" fill="url(#lk-play)"/>
    </g>
    <rect x="1" y="1" width="{_f(mark - 2)}" height="{_f(mark - 2)}" rx="{_f(PLATE_R / VB * mark - 1)}"
          fill="none" stroke="#FFFFFF" stroke-opacity="0.10" stroke-width="1.5"/>
  </g>

  <text x="316" y="172" fill="url(#lk-text)" font-family="Segoe UI Variable Display, Segoe UI, Inter, Helvetica Neue, Arial, sans-serif"
        font-size="86" font-weight="600" letter-spacing="-1.5">VideoPlayer</text>
  <text x="320" y="220" fill="{C_CYAN}" font-family="Segoe UI Variable Text, Segoe UI, Inter, Helvetica Neue, Arial, sans-serif"
        font-size="24" font-weight="500" letter-spacing="6.5" opacity="0.9">PLAY ANYTHING</text>
</svg>
"""


def _svg_tri_path() -> str:
    corners = []
    n = len(TRI)
    for i, v in enumerate(TRI):
        vi = np.array(v, dtype=np.float64)
        prev = np.array(TRI[i - 1], dtype=np.float64) - vi
        nxt = np.array(TRI[(i + 1) % n], dtype=np.float64) - vi
        prev /= np.linalg.norm(prev)
        nxt /= np.linalg.norm(nxt)
        corners.append((vi + prev * CORNER_D, vi, vi + nxt * CORNER_D))

    def p(pt):
        return f"{_f(pt[0])} {_f(pt[1])}"

    d = [f"M {p(corners[0][2])}"]
    for i in range(1, n + 1):
        t_in, v, t_out = corners[i % n]
        d.append(f"L {p(t_in)}")
        d.append(f"Q {p(v)} {p(t_out)}")
    d.append("Z")
    return " ".join(d)


def mono_transform(pad: float = MONO_PAD):
    """Scale + offset that fits the mark (bbox) into the 1024 canvas."""
    xs = [p[0] for p in TRI_PTS]
    ys = [p[1] for p in TRI_PTS]
    w = max(xs) - min(xs)
    h = max(ys) - min(ys)
    s = min(1024.0 * (1 - 2 * pad) / w, 1024.0 * (1 - 2 * pad) / h)
    cx = (max(xs) + min(xs)) / 2
    cy = (max(ys) + min(ys)) / 2
    return s, 512.0 - cx * s, 512.0 - cy * s


def render_mono(px: int, rgb, pad: float = MONO_PAD, ss: int = 8) -> Image.Image:
    """Transparent, single-colour play mark (no plate, no glow)."""
    W = px * ss
    k = W / VB
    s, tx, ty = mono_transform(pad)
    pts = [((x * s + tx) * k, (y * s + ty) * k) for x, y in TRI_PTS]
    mask = Image.new("L", (W, W), 0)
    ImageDraw.Draw(mask).polygon(pts, fill=255)
    mask = mask.resize((px, px), Image.Resampling.LANCZOS)
    out = Image.new("RGBA", (px, px), (rgb[0], rgb[1], rgb[2], 0))
    out.putalpha(mask)
    return out


def svg_mono(rgb, title: str) -> str:
    s, tx, ty = mono_transform()
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024" width="1024" height="1024" role="img" aria-label="{title}">
  <title>{title}</title>
  <path d="{_svg_tri_path()}" fill="{_hex(rgb)}" transform="translate({_f(tx)} {_f(ty)}) scale({_f(s)})"/>
</svg>
"""


# --------------------------------------------------------------------------
# ICO writer (BMP entries up to 64px, PNG entry for 256px)
# --------------------------------------------------------------------------
def _bmp_entry(img: Image.Image) -> bytes:
    w, h = img.size
    data = np.asarray(img.convert("RGBA"), dtype=np.uint8)
    bgra = data[:, :, [2, 1, 0, 3]][::-1]
    xor = np.ascontiguousarray(bgra).tobytes()

    alpha = data[:, :, 3]
    stride = max(4, ((w + 31) // 32) * 4)
    mask = bytearray()
    for y in range(h - 1, -1, -1):
        row = bytearray(stride)
        for x in range(w):
            if alpha[y, x] == 0:
                row[x // 8] |= 0x80 >> (x % 8)
        mask.extend(row)

    header = struct.pack(
        "<IiiHHIIiiII",
        40, w, h * 2, 1, 32, 0, len(xor) + len(mask), 0, 0, 0, 0,
    )
    return header + xor + bytes(mask)


def write_ico(path: Path, images: dict[int, Image.Image]) -> None:
    entries = []
    for size in sorted(images):
        img = images[size]
        if size >= 256:
            import io

            buf = io.BytesIO()
            img.save(buf, format="PNG", optimize=True)
            payload = buf.getvalue()
        else:
            payload = _bmp_entry(img)
        entries.append((size, payload))

    offset = 6 + 16 * len(entries)
    header = struct.pack("<HHH", 0, 1, len(entries))
    directory = b""
    blob = b""
    for size, payload in entries:
        directory += struct.pack(
            "<BBBBHHII",
            0 if size >= 256 else size,
            0 if size >= 256 else size,
            0, 0, 1, 32, len(payload), offset,
        )
        offset += len(payload)
        blob += payload
    path.write_bytes(header + directory + blob)


# --------------------------------------------------------------------------
# Preview sheet
# --------------------------------------------------------------------------
def _font(size: int, bold: bool = False, cjk: bool = False) -> ImageFont.FreeTypeFont:
    candidates = []
    if cjk:
        candidates += ["msyhbd.ttc" if bold else "msyh.ttc", "msyhl.ttc"]
    candidates += ["segoeuib.ttf" if bold else "segoeui.ttf", "arialbd.ttf" if bold else "arial.ttf"]
    for name in candidates:
        p = Path("C:/Windows/Fonts") / name
        if p.exists():
            try:
                return ImageFont.truetype(str(p), size)
            except OSError:
                continue
    return ImageFont.load_default()


def _tracked(d: ImageDraw.ImageDraw, xy, text, font, fill, tracking):
    x, y = xy
    for ch in text:
        d.text((x, y), ch, font=font, fill=fill)
        x += d.textlength(ch, font=font) + tracking


def draw_preview(icons: dict[int, Image.Image], path: Path) -> None:
    W, H = 1600, 1240
    sheet = Image.new("RGB", (W, H), "#0A0C12")

    # soft background glow
    for cx, cy, r, col in ((240, 230, 620, "#12283a"), (1420, 940, 700, "#1d1333")):
        g = Image.new("L", (W, H), 0)
        ImageDraw.Draw(g).ellipse([cx - r, cy - r, cx + r, cy + r], fill=70)
        g = g.filter(ImageFilter.GaussianBlur(170))
        sheet = Image.composite(Image.new("RGB", (W, H), col), sheet, g)

    d = ImageDraw.Draw(sheet)
    f_title = _font(78, True)
    f_sub = _font(27, cjk=True)
    f_small = _font(21, cjk=True)
    f_label = _font(24, bold=True, cjk=True)
    f_mono = _font(19)
    f_size = _font(19)
    f_mark = _font(84, True)

    # --- hero -------------------------------------------------------------
    hero = icons[512].resize((420, 420), Image.LANCZOS)
    sheet.paste(hero, (80, 88), hero)

    d.text((548, 116), "VideoPlayer", font=f_title, fill="#F2F5FC")
    d.text((552, 214), "Neon Minimal  ·  深色霓虹极简", font=f_sub, fill="#93A1BD")
    d.text((552, 256), "WPF + LibVLC 视频播放器品牌标识", font=f_small, fill="#5F6E8C")

    # --- palette ----------------------------------------------------------
    swatches = [(C_CYAN, "Cyan"), (C_INDIGO, "Indigo"), (C_VIOLET, "Violet"),
                (C_INK, "Ink"), (C_SLATE, "Slate")]
    x = 552
    for hexv, name in swatches:
        d.rounded_rectangle([x, 316, x + 84, 400], radius=16, fill=hexv,
                            outline="#2A3247", width=1)
        d.text((x, 412), hexv, font=f_mono, fill="#7C8AA6")
        d.text((x, 438), name, font=f_small, fill="#4E5A73")
        x += 100

    # --- sizes on dark ----------------------------------------------------
    d.line([(80, 520), (1520, 520)], fill="#1E2537", width=1)
    d.text((80, 544), "尺寸与可读性", font=f_label, fill="#C6D0E4")
    d.text((232, 550), "/ icon sizes", font=f_small, fill="#4E5A73")

    x = 84
    for sz in (128, 64, 48, 32, 24, 16):
        img = icons[sz]
        sheet.paste(img, (x, 712 - sz), img)
        d.text((x, 730), f"{sz}px", font=f_size, fill="#5C6982")
        x += max(sz, 74) + 46

    # --- sizes on light ---------------------------------------------------
    card = Image.new("RGB", (820, 250), "#F3F5F9")
    sheet.paste(card, (84, 786))
    d.rounded_rectangle([84, 786, 904, 1036], radius=22, outline="#DCE1EA", width=1)
    d.text((112, 810), "浅色背景", font=f_small, fill="#6B7689")
    x = 112
    base_y = 986
    for sz in (96, 64, 48, 32, 16):
        img = icons.get(sz) or render(sz)
        sheet.paste(img, (x, base_y - sz), img)
        x += sz + 44

    # --- wordmark ---------------------------------------------------------
    d.text((924, 830), "VideoPlayer", font=f_mark, fill="#EEF2FA")
    _tracked(d, (928, 950), "PLAY ANYTHING", _font(20, True), C_CYAN, 6)
    d.text((928, 985), "wordmark  ·  Segoe UI Semibold", font=_font(18), fill="#525E76")

    d.text((84, 1155), "assets/brand  ·  logo.svg  ·  logo-lockup.svg  ·  logo-{16..1024}.png  ·  app.ico (16-256)",
           font=_font(19), fill="#3E4A62")

    sheet.save(path, "PNG")


# --------------------------------------------------------------------------
# Main
# --------------------------------------------------------------------------
def draw_lockup(icon: Image.Image, path: Path) -> None:
    """Transparent PNG lockup, matching logo-lockup.svg (1180 x 320)."""
    W, H = 1180, 320
    sheet = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    mark = icon.resize((256, 256), Image.LANCZOS)
    sheet.alpha_composite(mark, (0, 32))

    d = ImageDraw.Draw(sheet)
    d.text((316, 82), "VideoPlayer", font=_font(88, True), fill="#F4F7FD")
    _tracked(d, (320, 212), "PLAY ANYTHING", _font(21, True), "#2BE7F5", 6)
    sheet.save(path, "PNG")


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)

    (OUT / "logo.svg").write_text(svg_icon(), encoding="utf-8")
    (OUT / "logo-lockup.svg").write_text(svg_lockup(), encoding="utf-8")

    png_sizes = [1024, 512, 256, 128, 64, 48, 32, 24, 16]
    rendered: dict[int, Image.Image] = {}
    for size in png_sizes:
        ss = 8 if size <= 64 else (4 if size <= 256 else 2)
        img = render(size, ss=ss)
        rendered[size] = img
        img.save(OUT / f"logo-{size}.png", "PNG", optimize=True)

    # transparent, text-free dark logo for overlays (same art, no dither)
    rendered[1024].save(OUT / "logo.png", "PNG", optimize=True)

    draw_preview(rendered, OUT / "preview.png")
    draw_lockup(rendered[256], OUT / "logo-lockup.png")

    ico_sizes = [16, 24, 32, 48, 64, 128, 256]
    ico_images = {s: (rendered[s] if s in rendered else render(s, ss=8)) for s in ico_sizes}
    write_ico(OUT / "app.ico", ico_images)

    print("wrote:")
    for p in sorted(OUT.rglob("*")):
        if p.is_file() and "tools" not in p.parts:
            print(f"  {p.relative_to(ROOT.parent.parent)}  ({p.stat().st_size:,} B)")


if __name__ == "__main__":
    main()
