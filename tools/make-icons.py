#!/usr/bin/env python3
"""
پردازش آیکون pdd Scan:
  1) حذف پس‌زمینه سفید و تبدیل به ترنسپرنت (با flood-fill از لبه‌ها تا سفیدِ
     داخل سند دست نخورده بماند)
  2) برش به اندازه محتوا با حاشیه کم
  3) خروجی PNG برای وب + ICO چندسایزی برای WPF
"""
from PIL import Image, ImageFilter, ImageDraw
import os

import sys
SRC = sys.argv[1] if len(sys.argv) > 1 else "icon_raw.png"
WEB_IMG_DIR = "/home/user/scan/src/ScanSystem.Web/wwwroot"
AGENT_DIR = "/home/user/scan/src/ScanSystem.Agent"

os.makedirs(os.path.join(WEB_IMG_DIR, "img"), exist_ok=True)

img = Image.open(SRC).convert("RGBA")
w, h = img.size
px = img.load()

# ── 1) Flood-fill مرزها → تولید ماسک پس‌زمینه ──────────────────────────────
# با BFS از تمام پیکسل‌های لبه شروع می‌کنیم و هر پیکسل نزدیک به سفید را
# (تلرانس رنگی) به‌عنوان پس‌زمینه علامت می‌زنیم.
TOL = 42
visited = bytearray(w * h)
stack = []

def is_bg(x, y):
    r, g, b, a = px[x, y]
    if a == 0:
        return True
    # فاصله از سفید خالص
    d = abs(255 - r) + abs(255 - g) + abs(255 - b)
    return d <= TOL

for x in range(w):
    stack.append((x, 0)); stack.append((x, h - 1))
for y in range(h):
    stack.append((0, y)); stack.append((w - 1, y))

while stack:
    x, y = stack.pop()
    if x < 0 or y < 0 or x >= w or y >= h:
        continue
    idx = y * w + x
    if visited[idx]:
        continue
    if not is_bg(x, y):
        continue
    visited[idx] = 1
    stack.append((x + 1, y)); stack.append((x - 1, y))
    stack.append((x, y + 1)); stack.append((x, y - 1))

# ── 2) اعمال آلفا + نرم‌کردن لبه ───────────────────────────────────────────
mask = Image.frombytes("L", (w, h), bytes(bytearray(0 if visited[i] else 255 for i in range(w * h))))
# کمی feather برای لبه‌های تمیز
mask = mask.filter(ImageFilter.GaussianBlur(1.2))

img.putalpha(mask)

# ── 3) برش به محتوا + حاشیه ───────────────────────────────────────────────
bbox = mask.getbbox()
pad = 18
if bbox:
    l, t, r, b = bbox
    l = max(0, l - pad); t = max(0, t - pad)
    r = min(w, r + pad); b = min(h, b + pad)
    img = img.crop((l, t, r, b))

# مربع‌سازی (آیکون استاندارد)
cw, ch = img.size
side = max(cw, ch)
sq = Image.new("RGBA", (side, side), (0, 0, 0, 0))
sq.paste(img, ((side - cw) // 2, (side - ch) // 2), img)
img = sq

def export_png(path, size):
    out = img.resize((size, size), Image.LANCZOS)
    out.save(path, "PNG", optimize=True)
    print("saved", path, size)

# ── 4) خروجی‌ها ────────────────────────────────────────────────────────────
# لوگوی صفحه وب (هدر) و favicon
export_png(os.path.join(WEB_IMG_DIR, "img", "pddscan-logo.png"), 512)
export_png(os.path.join(WEB_IMG_DIR, "favicon.png"), 256)

# لوگوی PNG برای هدر پنجره WPF
export_png(os.path.join(AGENT_DIR, "logo.png"), 256)

# ICO چندسایزی برای WPF/ClickOnce (Tray + Titlebar + Explorer)
ico_path = os.path.join(AGENT_DIR, "appicon.ico")
img.save(ico_path, "ICO", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print("saved", ico_path)

print("done")
