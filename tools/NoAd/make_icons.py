#!/usr/bin/env python3
# 把 ImageGen 生成的源图标：去水印 -> 缩放多尺寸 -> 加圆角，输出到 extension/icons/
from PIL import Image, ImageDraw
import os

SRC = r"C:/Users/fuwen/WorkBuddy/noad/extension/assets/Flat_vector_app_icon_for_a_bro_2026-11-04T08-13-53.png"
ICON_DIR = r"C:/Users/fuwen/WorkBuddy/noad/extension/icons"
os.makedirs(ICON_DIR, exist_ok=True)

img = Image.open(SRC).convert("RGBA")
W, H = img.size

# 计算白色符号(盾牌/圆/斜杠)边界框，覆盖水印时避开它，避免误伤
gray = img.convert("L")
mask_white = gray.point(lambda p: 255 if p > 240 else 0)
bbox = mask_white.getbbox()
bx1, by1 = (bbox[2], bbox[3]) if bbox else (int(W * 0.7), int(H * 0.8))

# 采样最右下角背景蓝，用于覆盖水印
fill = img.getpixel((W - 3, H - 3))[:3]
draw = ImageDraw.Draw(img)
# 覆盖区域从「白色符号边界框之外 + 安全余量」延伸到右下角，
# 确保剔除角部水印，同时不切到盾牌底部
x0 = max(bx1 + 12, int(W * 0.55))
y0 = max(by1 + 12, int(H * 0.80))
draw.rectangle([x0, y0, W - 1, H - 1], fill=fill + (255,))

def rounded(src, sz, radius_ratio=0.18):
    base = src.resize((sz, sz), Image.LANCZOS)
    mask = Image.new("L", (sz, sz), 0)
    d = ImageDraw.Draw(mask)
    r = max(1, int(sz * radius_ratio))
    d.rounded_rectangle([0, 0, sz - 1, sz - 1], radius=r, fill=255)
    out = base.copy()
    out.putalpha(mask)
    return out

for sz in (16, 32, 48, 128):
    rounded(img, sz).save(os.path.join(ICON_DIR, f"icon{sz}.png"), "PNG")
    print("生成 icon%d.png" % sz)

print("完成，图标目录:", ICON_DIR)
