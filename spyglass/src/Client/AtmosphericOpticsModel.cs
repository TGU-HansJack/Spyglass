using System;

namespace spyglass.src.Client
{
    internal enum OpticsPrecipitation { None, Rain, Snow, Hail, Auto }

    // Shared by the Cairo mask and the GPU pass; all coordinates are physical pixels.
    internal readonly record struct LensAperture(double X, double Y, double Width, double Height)
    {
        internal static LensAperture Square(double x, double y, double width, double height, int edgeSize, float lensSize)
        {
            float scale = float.IsFinite(lensSize) ? Math.Clamp(lensSize, 0.05f, 1f) : 0.5f;
            double size = Math.Clamp(Math.Min(width, height) - edgeSize * 2.0, 0, Math.Min(width, height)) * scale;
            return new(x + (width - size) / 2, y + (height - size) / 2, size, size);
        }
    }

    internal static class AtmosphericOpticsModel
    {
        internal static float Strength(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 2) : 1;
        internal static float Unit(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;

        internal static float SmoothStep(float lo, float hi, float value)
        {
            float t = Unit((value - lo) / (hi - lo));
            return t * t * (3 - 2 * t);
        }

        internal static float DayWeight(float sunElevation) => SmoothStep(-0.08f, 0.12f, sunElevation);

        internal static OpticsPrecipitation ResolvePrecipitation(OpticsPrecipitation kind, float temperature, float snowThreshold)
            => kind == OpticsPrecipitation.Auto
                ? (temperature < snowThreshold ? OpticsPrecipitation.Snow : OpticsPrecipitation.Rain)
                : kind;

        internal static float Extinction(OpticsPrecipitation kind, float intensity, float strength)
        {
            if (kind == OpticsPrecipitation.None || kind == OpticsPrecipitation.Auto) return 0;
            float transmission = kind == OpticsPrecipitation.Snow ? 0.75f : 0.85f;
            return -MathF.Log(transmission) / 256 * Unit(intensity) * Strength(strength);
        }

        internal static float LensRainIntensity(OpticsPrecipitation kind, float rainfall, double eyeHeight, int rainHeight)
            => kind == OpticsPrecipitation.Rain && double.IsFinite(eyeHeight) && eyeHeight > rainHeight + 1.0
                ? Unit(rainfall) : 0;

        internal static float Approach(float current, float target, float dt)
            => current + (target - current) * (1 - MathF.Exp(-(float.IsFinite(dt) ? Math.Clamp(dt, 0, 1) : 0) / 0.35f));

        internal static float Glare(float sunElevation, float visibility, float cloudDensity, float fogDensity, float strength)
            => 0.1f * Strength(strength) * DayWeight(sunElevation) * Unit(visibility)
                * (1 - Unit(cloudDensity) * Unit(cloudDensity))
                * MathF.Exp(-Math.Max(0, float.IsFinite(fogDensity) ? fogDensity : 0) * 64);

        // Column-major inverse of the actual game projection, including the zoomed FOV.
        internal static bool InvertProjection(double[] projection, float[] inverse)
        {
            if (projection == null || projection.Length != 16 || inverse == null || inverse.Length != 16) return false;
            // Retain double precision until upload: a short near plane makes the
            // inverse sensitive to rounding, especially for distant zoomed targets.
            Span<double> augmented = stackalloc double[32];
            augmented.Clear();
            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 4; col++) augmented[row * 8 + col] = projection[col * 4 + row];
                augmented[row * 8 + 4 + row] = 1;
            }
            for (int col = 0; col < 4; col++)
            {
                int pivot = col;
                for (int row = col + 1; row < 4; row++)
                    if (Math.Abs(augmented[row * 8 + col]) > Math.Abs(augmented[pivot * 8 + col])) pivot = row;
                double divisor = augmented[pivot * 8 + col];
                if (!double.IsFinite(divisor) || Math.Abs(divisor) < 1e-15) return false;
                for (int i = 0; i < 8; i++)
                    (augmented[col * 8 + i], augmented[pivot * 8 + i]) = (augmented[pivot * 8 + i], augmented[col * 8 + i]);
                for (int i = 0; i < 8; i++) augmented[col * 8 + i] /= divisor;
                for (int row = 0; row < 4; row++)
                {
                    if (row == col) continue;
                    double factor = augmented[row * 8 + col];
                    for (int i = 0; i < 8; i++) augmented[row * 8 + i] -= factor * augmented[col * 8 + i];
                }
            }
            for (int row = 0; row < 4; row++)
            for (int col = 0; col < 4; col++)
            {
                float value = (float)augmented[row * 8 + col + 4];
                if (!float.IsFinite(value)) return false;
                inverse[col * 4 + row] = value;
            }
            return true;
        }
    }
}
