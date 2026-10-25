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
    draw.text((16, 7), "水珠 / 下淌 / 底部积水 · 离屏预览", font=font, fill="white")
    draw.text((16, 32), "持续降雨" if index < 45 else "进入遮蔽处：余水继续下淌", font=font, fill=(160, 200, 225))
    frames.append(frame)
frames[0].save(overlays / "lens-runoff.gif", save_all=True, append_images=frames[1:], duration=100, loop=0)
frames[30].save(overlays / "lens-runoff.png")
print(overlays / "glass-comparison.png")
print(overlays / "lens-runoff.gif")
