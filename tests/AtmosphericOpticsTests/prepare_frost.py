"""Prepare the transparent feathered-frost mask from the supplied reference.

Usage: python prepare_frost.py <reference-image>
Requires Pillow. The blue background is removed; only ice opacity is retained.
The RGBA fixture is the exact decoded production PNG for the GPU regression.
"""
import argparse
from pathlib import Path
from PIL import Image

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("reference", type=Path)
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
source = Image.open(args.reference).convert("RGB").resize((2048, 1365))
size = 512
axis, side = (.726, -.688), (.688, .726)
length, width = 840, 580
transform = (axis[0] * length / size, side[0] * width / size, 20 - side[0] * width / 2,
             axis[1] * length / size, side[1] * width / size, 1210 - side[1] * width / 2)
patch = source.transform((size, size), Image.AFFINE, transform, Image.BICUBIC)
pixels = []
for y in range(size):
    for x in range(size):
        red, green, _ = patch.getpixel((x, y))
        opacity = max(0, min(1, (min(red, green) / 255 - .055) / .82)) ** 1.1
        u, v = x / (size - 1), y / (size - 1)
        opacity *= min(1, (1 - u) / .10) * min(1, v / .07) * min(1, (1 - v) / .07)
        pixels.append((220, 238, 247, round(opacity * 255)))
mask = Image.new("RGBA", (size, size))
mask.putdata(pixels)
destination = root / "spyglass/assets/spyglassfork/textures/lens/frost.png"
destination.parent.mkdir(parents=True, exist_ok=True)
mask.save(destination)
fixture = Path(__file__).parent / "fixtures/frost.rgba"
fixture.parent.mkdir(parents=True, exist_ok=True)
fixture.write_bytes(Image.open(destination).convert("RGBA").tobytes())
print(destination)
