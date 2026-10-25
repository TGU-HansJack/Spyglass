"""Create an explicitly labelled offscreen preview using the production GLSL.

Requires Pillow and the same game libraries as AtmosphericOpticsTests.
Example: python preview.py --image screenshot.png --game E:/MVL/Release/1.22.3
"""
import argparse
from pathlib import Path
import subprocess

from PIL import Image, ImageDraw, ImageFont


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", type=Path, required=True)
    parser.add_argument("--game", required=True)
    parser.add_argument("--crop", help="Optional left,top,right,bottom input crop")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    out = root / "artifacts" / "optics-preview"
    out.mkdir(parents=True, exist_ok=True)
    source = Image.open(str(args.image)).convert("RGB")
    if args.crop:
        source = source.crop(tuple(map(int, args.crop.split(","))))
    else:
        width, height = source.size
        side = min(width, height)
        source = source.crop(((width-side)//2, (height-side)//2, (width+side)//2, (height+side)//2))
    source = source.resize((512, 512), Image.LANCZOS)
    scene = Image.new("RGBA", (1044, 1044), (0, 0, 0, 255))
    scene.paste(source, (266, 266))
    (out / "scene.rgba").write_bytes(scene.transpose(Image.FLIP_TOP_BOTTOM).tobytes())
    subprocess.run([
        "dotnet", "run", "--project", str(Path(__file__).parent), "-c", "Release",
        "-p:VINTAGE_STORY=" + args.game, "--", "--preview", str(out)
    ], check=True, cwd=str(root))
    font_path = "C:/Windows/Fonts/msyh.ttc"
    def font(size):
        return ImageFont.truetype(font_path, size)
    canvas = Image.new("RGB", (1648, 1434), "#12191f")
    draw = ImageDraw.Draw(canvas)
    draw.text((32, 20), "望远镜环境光学 · 实际着色器离屏对比", fill="#f1f4f6", font=font(32))
    draw.text((32, 73), "旧游戏截图作为输入；统一假定距离 256 格。不是当前模组的实机效果截图。", fill="#edc682", font=font(23))
    draw.text((32, 113), "同一画面、同一曝光；只比较新增光学。方形开口内裁图，未绘制金属边框。", fill="#b0beca", font=font(21))
    cases = [
        ("off", "关闭新增光学", "基准画面，保留原截图已有的光照与雾"),
        ("rain", "默认 · 满强度降雨", "256 格处额外降低对比度约 15%"),
        ("snow", "默认 · 满强度降雪", "256 格处额外降低对比度约 25%"),
        ("glare", "默认 · 局部太阳眩光", "无遮挡、晴空的理论上限：中心约 10%"),
        ("rain2", "对比 · 降雨强度设置为 2", "256 格处额外降低对比度约 28%"),
        ("glare2", "对比 · 眩光强度设置为 2", "无遮挡、晴空的理论上限：中心约 20%"),
    ]
    for index, (name, title, detail) in enumerate(cases):
        x, y = 32 + (index % 3) * 536, 172 + (index // 3) * 604
        draw.text((x, y), title, fill="#f1f4f6", font=font(24))
        draw.text((x, y+37), detail, fill="#b0beca", font=font(17))
        canvas.paste(Image.open(str(out / (name + ".ppm"))), (x, y+72))
    draw.text((32, 1388), "天气、太阳位置与遮挡是控制输入；本图不能验证游戏内的天气采样和遮挡是否正确。", fill="#b0beca", font=font(20))
    canvas.save(str(out / "comparison.png"))
    # Blink makes a restrained default effect easier to inspect without amplifying it.
    frames = []
    for name, title in [("off", "关闭新增光学"), ("rain", "默认：满强度降雨 / 假定 256 格")]:
        frame = Image.new("RGB", (512, 570), "#12191f")
        ImageDraw.Draw(frame).text((12, 12), title, font=font(22), fill="white")
        frame.paste(Image.open(str(out / (name + ".ppm"))), (0, 58))
        frames.append(frame)
    frames[0].save(str(out / "rain-default.gif"), save_all=True, append_images=frames[1:], duration=1200, loop=0)
    (out / "preview-notes.txt").write_text(
        "Production GLSL offscreen preview, not an in-game capture.\n"
        "Input screenshot: " + args.image.name + "\n"
        "All pixels are a reference plane at view depth 256 blocks, not reconstructed terrain depth.\n"
        "Precipitation=1; weather strength=1 or 2; fog RGB=(.72,.77,.80).\n"
        "Sun examples use an unoccluded, cloudless theoretical maximum; no sun visibility is measured.\n"
        "No contrast amplification applied to the output.\n", encoding="utf-8")
    print(out / "comparison.png")


if __name__ == "__main__":
    main()
