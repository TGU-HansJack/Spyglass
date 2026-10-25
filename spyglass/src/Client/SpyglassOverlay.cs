using Vintagestory.API.Client;
using Cairo;
using System;

namespace spyglass.src.Client
{
    internal class SpyglassOverlay : GuiElement // basically GuiElementInset, with a customizeable background color.
    {
        private readonly int edgeSize;
        private readonly float lensSize;
        private readonly float glassBrightness;
        private readonly float glassColor;
        private readonly VignetteStyle style;
        private readonly string material;
        private readonly float lightLevel;
        private readonly float edgeOpacity;

        public SpyglassOverlay(ICoreClientAPI capi, ElementBounds bounds, int edgeSize, float lensSize, float glassColor, float glassBrightness, float edgeOpacity, VignetteStyle style, string material, float lightLevel) : base(capi, bounds)
        {
            this.edgeSize = edgeSize;
            this.lensSize = float.IsFinite(lensSize) ? Math.Clamp(lensSize, 0.05f, 1f) : 0.5f;
            this.glassColor = glassColor;
            this.glassBrightness = glassBrightness;
            this.style = style;
            this.material = material;
            this.lightLevel = Math.Max(0.2f, Math.Min(1.15f, lightLevel));
            this.edgeOpacity = Math.Max(0f, Math.Min(1f, edgeOpacity));
        }

        public override void ComposeElements(Context ctx, ImageSurface surface)
        {
            Bounds.CalcWorldBounds();

            if (glassBrightness < 1)
            {
                ctx.SetSourceRGBA(glassColor, glassColor, glassColor, 1 - glassBrightness);
                Rectangle(ctx, Bounds);
                ctx.Fill();
            }

            switch (style)
            {
                case VignetteStyle.square:
                    {
                        var aperture = LensAperture.Square(Bounds.drawX, Bounds.drawY, Bounds.OuterWidth, Bounds.OuterHeight, edgeSize, lensSize);
                        double size = aperture.Width;
                        double x = aperture.X;
                        double y = aperture.Y;

                        // Even-odd fill cuts the square aperture out of the dark mask.
                        ctx.SetSourceRGBA(0.0, 0.0, 0.0, 1.0);
                        ctx.NewPath();
                        ctx.Rectangle(Bounds.drawX, Bounds.drawY, Bounds.OuterWidth, Bounds.OuterHeight);
                        ctx.Rectangle(x, y, size, size);
                        ctx.FillRule = FillRule.EvenOdd;
                        ctx.Fill();

                        DrawMaterialContour(ctx, x, y, size);
                        break;
                    }
            }
        }

        private void DrawMaterialContour(Context ctx, double x, double y, double size)
        {
            GetMaterialColor(material, out double red, out double green, out double blue);
            double light = lightLevel;
            // Twice the previous default metal width (20 * 0.18), independent
            // of the aperture margin so reducing that margin keeps the rim thick.
            const double width = 7.2;

            // Three nested strokes form a bevelled metal outline instead of a flat
            // gradient: dark underside, material body, then a narrow lit edge.
            ctx.LineWidth = width * 2.2;
            ctx.SetSourceRGBA(red * 0.28, green * 0.28, blue * 0.28, edgeOpacity);
            StrokeRectangle(ctx, x, y, size);

            using (var materialGradient = new LinearGradient(x, y, x + size, y + size))
            {
                materialGradient.AddColorStop(0.0, new Color(Math.Min(1, red * (0.75 + light * 0.25)), Math.Min(1, green * (0.75 + light * 0.25)), Math.Min(1, blue * (0.75 + light * 0.25)), edgeOpacity));
                materialGradient.AddColorStop(0.48, new Color(red, green, blue, edgeOpacity));
                materialGradient.AddColorStop(1.0, new Color(red * 0.35, green * 0.35, blue * 0.35, edgeOpacity));
                ctx.LineWidth = width;
                ctx.SetSource(materialGradient);
                StrokeRectangle(ctx, x, y, size);
            }

            ctx.LineWidth = Math.Max(2.0, width * 0.22);
            ctx.SetSourceRGBA(Math.Min(1, red + 0.34 * light), Math.Min(1, green + 0.34 * light), Math.Min(1, blue + 0.34 * light), edgeOpacity * 0.9);
            ctx.MoveTo(x + width * 0.5, y + width * 0.5);
            ctx.LineTo(x + size - width * 0.5, y + width * 0.5);
            ctx.MoveTo(x + width * 0.5, y + width * 0.5);
            ctx.LineTo(x + width * 0.5, y + size - width * 0.5);
            ctx.Stroke();
        }

        private static void StrokeRectangle(Context ctx, double x, double y, double size)
        {
            ctx.NewPath();
            ctx.Rectangle(x, y, size, size);
            ctx.Stroke();
        }

        private static void GetMaterialColor(string material, out double red, out double green, out double blue)
        {
            red = 0.62; green = 0.30; blue = 0.11;
            switch (material)
            {
                case "tinbronze": red = 0.60; green = 0.46; blue = 0.25; break;
                case "bismuth": red = 0.42; green = 0.48; blue = 0.54; break;
                case "bismuthbronze": red = 0.48; green = 0.31; blue = 0.17; break;
                case "blackbronze": red = 0.24; green = 0.19; blue = 0.15; break;
                case "gold": red = 0.76; green = 0.54; blue = 0.14; break;
                case "silver": red = 0.62; green = 0.67; blue = 0.72; break;
                case "iron": red = 0.30; green = 0.32; blue = 0.34; break;
                case "meteoriciron": red = 0.18; green = 0.21; blue = 0.25; break;
                case "brass": red = 0.68; green = 0.49; blue = 0.17; break;
                case "steel": red = 0.40; green = 0.47; blue = 0.54; break;
            }
        }
    }
}
