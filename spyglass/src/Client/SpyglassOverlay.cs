using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Cairo;
using System;
using System.Runtime.InteropServices;

namespace spyglass.src.Client
{
    internal class SpyglassOverlay : GuiElement
    {
        private readonly int edgeSize;
        private readonly float lensSize, glassBrightness, glassColor, lightLevel;
        private readonly VignetteStyle style;
        private readonly string material;
        private readonly string lensGlass;

        internal const double MetalWidth = 7.2 * 3;

        public SpyglassOverlay(ICoreClientAPI capi, ElementBounds bounds, int edgeSize, float lensSize, float glassColor, float glassBrightness, float edgeOpacity, VignetteStyle style, string material, float lightLevel, string lensGlass = "plain") : base(capi, bounds)
        {
            this.edgeSize = edgeSize;
            this.lensSize = float.IsFinite(lensSize) ? Math.Clamp(lensSize, 0.05f, 1f) : 0.5f;
            this.glassColor = glassColor;
            this.glassBrightness = glassBrightness;
            this.style = style;
            this.material = material;
            this.lightLevel = Math.Clamp(lightLevel, 0.2f, 1f);
            this.lensGlass = lensGlass;
        }

        public override void ComposeElements(Context ctx, ImageSurface surface)
        {
            Bounds.CalcWorldBounds();
            if (style != VignetteStyle.square) return;
            var aperture = LensAperture.Square(Bounds.drawX, Bounds.drawY, Bounds.OuterWidth, Bounds.OuterHeight, edgeSize, lensSize);
            double x = aperture.X, y = aperture.Y, size = aperture.Width;
            if (size <= 0) return;

            ctx.Save();
            ctx.SetSourceRGBA(0, 0, 0, 1);
            ctx.NewPath();
            ctx.Rectangle(Bounds.drawX, Bounds.drawY, Bounds.OuterWidth, Bounds.OuterHeight);
            ctx.Rectangle(x, y, size, size);
            ctx.FillRule = FillRule.EvenOdd;
            ctx.Fill();
            ctx.Restore();

            ctx.Save();
            ctx.Rectangle(x, y, size, size);
            ctx.Clip();
            if (glassBrightness < 1)
            {
                ctx.SetSourceRGBA(glassColor, glassColor, glassColor, 1 - glassBrightness);
                ctx.Paint();
            }
            DrawVanillaGlass(ctx, x, y, size);
            ctx.Restore();
            DrawMaterialContour(ctx, x, y, size);
        }

        private void DrawVanillaGlass(Context ctx, double x, double y, double size)
        {
            // Preserve the original glass artwork, tint and alpha. A single tile
            // spans the lens, just as one texture spans an original glass block.
            using var glass = getImageSurfaceFromAsset(api, LensGlass.Texture(lensGlass));
            // The engine decoder supplies straight-alpha BGRA, while Cairo
            // expects premultiplied channels. Without this, colored glass shifts
            // hue and bright quartz pixels overflow into mosaic-like artifacts.
            glass.Flush();
            var pixels = new byte[glass.Stride * glass.Height];
            Marshal.Copy(glass.DataPtr, pixels, 0, pixels.Length);
            for (int i = 0; i < pixels.Length; i += 4)
                for (int channel = 0; channel < 3; channel++)
                    pixels[i + channel] = (byte)((pixels[i + channel] * pixels[i + 3] + 127) / 255);
            Marshal.Copy(pixels, 0, glass.DataPtr, pixels.Length);
            glass.MarkDirty();
            using var pattern = new SurfacePattern(glass) { Filter = Filter.Nearest, Extend = Extend.Pad };
            pattern.Matrix = new Matrix(glass.Width / size, 0, 0, glass.Height / size, -x * glass.Width / size, -y * glass.Height / size);
            ctx.SetSource(pattern);
            ctx.Paint();
        }

        private void DrawMaterialContour(Context ctx, double x, double y, double size)
        {
            ctx.Save();
            // Physical metal is always opaque, even with an old edgeOpacity
            // setting. Pixel edges avoid a translucent antialiased fringe.
            ctx.Antialias = Antialias.None;
            var texture = new AssetLocation("game", "textures/block/metal/ingot/" + material + ".png");
            if (api.Assets.TryGet(texture) == null)
                texture = new AssetLocation("game", "textures/block/metal/ingot/copper.png");
            using var metalSurface = getImageSurfaceFromAsset(api, texture);
            using var metalPattern = new SurfacePattern(metalSurface) { Extend = Extend.Repeat, Filter = Filter.Nearest };
            metalPattern.Matrix = new Matrix(1.0 / 3, 0, 0, 1.0 / 3, -x / 3, -y / 3);
            ctx.LineWidth = MetalWidth;
            ctx.SetSource(metalPattern);
            StrokeRectangle(ctx, x, y, size);

            ctx.SetSourceRGBA(0, 0, 0, 1 - lightLevel * 0.82);
            StrokeRectangle(ctx, x, y, size);
            ctx.Restore();
        }

        private static void StrokeRectangle(Context ctx, double x, double y, double size)
        {
            ctx.NewPath();
            ctx.Rectangle(x, y, size, size);
            ctx.Stroke();
        }
    }
}
