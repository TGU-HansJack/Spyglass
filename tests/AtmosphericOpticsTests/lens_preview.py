"""Assemble production Cairo/GLSL outputs; these are not in-game captures."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[2]
overlays = root / "artifacts/lens-preview"
optics = root / "artifacts/optics-preview"
font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 18)
source = Image.open(optics / "source-thumbnail.png").convert("RGBA")
card = Image.new("RGB", (1110, 800), (28, 28, 28))
draw = ImageDraw.Draw(card)
draw.text((12, 8), "实际绘制代码离屏预览（非游戏内截图）", font=font, fill="white")
for index, (glass, name) in enumerate([
    ("plain", "普通"), ("blue", "蓝色"), ("green", "绿色"),
    ("red", "红色"), ("quartz", "石英"), ("smoky", "烟色"),
]):
    overlay = Image.open(overlays / ("copper.png" if glass == "plain" else f"glass-{glass}.png")).convert("RGBA")
    composite = Image.alpha_composite(source.resize(overlay.size), overlay)
    crop = composite.crop((430, 150, 850, 570)).resize((350, 350))
    x, y = index % 3 * 370 + 10, index // 3 * 380 + 65
    card.paste(crop, (x, y))
    draw.text((x, y - 25), name + "玻璃", font=font, fill="white")
card.save(overlays / "glass-comparison.png")

overlay = Image.open(overlays / "size-1044x1044.png").convert("RGBA").crop((266, 266, 778, 778))
frames = []
for index in range(90):
    raw = Image.open(optics / f"water-{index:03}.ppm").convert("RGBA")
    image = Image.alpha_composite(raw, overlay)
    frame = Image.new("RGB", (544, 590), (25, 25, 25))
    frame.paste(image, (16, 65))
    draw = ImageDraw.Draw(frame)
    draw.text((16, 7), "镜片静止水珠 · 离屏预览", font=font, fill="white")
    draw.text((16, 32), "持续降雨" if index < 45 else "进入遮蔽处：水珠原地蒸发", font=font, fill=(160, 200, 225))
    frames.append(frame)
frames[0].save(overlays / "lens-beads.gif", save_all=True, append_images=frames[1:], duration=100, loop=0)
frames[30].save(overlays / "lens-beads.png")
frost_frames = []
for index in range(90):
    frost = Image.open(optics / f"frost-{index:03}.ppm").convert("RGBA")
    frost = Image.alpha_composite(frost, overlay)
    frame = Image.new("RGB", (544, 590), (25, 25, 25))
    frame.paste(frost, (16, 65))
    draw = ImageDraw.Draw(frame)
    draw.text((16, 7), "霜纹从镜片边缘生长 · 离屏预览", font=font, fill="white")
    draw.text((16, 32), f"生长时间：{index / 10:.1f} 秒", font=font, fill=(160, 200, 225))
    frost_frames.append(frame)
frost_frames[-1].save(overlays / "transmitted-frost-v079.png")
frost_frames[0].save(overlays / "transmitted-frost-growth-v079.gif", save_all=True, append_images=frost_frames[1:], duration=100, loop=0)
print(overlays / "glass-comparison.png")
print(overlays / "lens-beads.gif")
print(overlays / "transmitted-frost-v079.png")
print(overlays / "transmitted-frost-growth-v079.gif")

visibility = Image.new("RGB", (1100, 1883), (25, 25, 25))
draw = ImageDraw.Draw(visibility)
draw.text((16, 10), "亮背景可见度 · 实际 GLSL 离屏输出（非实机截图）", font=font, fill="white")
for row, (background, title) in enumerate([("foliage", "树叶"), ("fog", "亮雾"), ("sky", "天空"), ("sun", "太阳眩光 / 接近纯白"), ("chart", "线条背景 / 折射对照")]):
    for col, (effect, label) in enumerate([("clear", "无表面效果"), ("beads", "密集静止水珠"), ("ice", "透光冰层")]):
        raw = Image.open(optics / f"visibility-{background}-{effect}.ppm").convert("RGBA")
        image = Image.alpha_composite(raw, overlay).resize((350, 350))
        x, y = col * 365 + 10, row * 365 + 55
        visibility.paste(image, (x, y))
        draw.text((x, y - 24), title + " / " + label, font=font, fill="white")
visibility.save(overlays / "surface-transmission-v079.png")
print(overlays / "surface-transmission-v079.png")
