using System;
using System.Collections.Generic;

namespace spyglass.src.Client
{
    // Lens coordinates run from top-left to bottom-right. Surface beads never move.
    internal sealed class LensRainModel
    {
        internal const int MaxDrops = 80;
        internal const float MaxDropRadius = .024f;
        internal const long IdleClearMilliseconds = 30000;
        private long lastUseMilliseconds = -1;
        internal bool IsInUse { get; private set; }
        internal sealed class Drop
        {
            internal float X, Y, Radius, Shape, Fade, Seed;
        }
        internal readonly List<Drop> Drops = new();
        internal readonly float[] DropUniforms = new float[MaxDrops * 4];
        internal readonly float[] DropShapeUniforms = new float[MaxDrops * 4];
        private readonly Random waterRandom = new(90417);
        private double remainder, spawn;
        internal bool HasWater => Drops.Count > 0;

        internal void UpdateUsage(long nowMilliseconds, bool inUse)
        {
            if (nowMilliseconds < 0) return;
            // Elapsed time is session-local. Restart a clock on a new session
            // instead of expiring from a negative or wrapped subtraction.
            if (lastUseMilliseconds < 0 || nowMilliseconds < lastUseMilliseconds)
                lastUseMilliseconds = nowMilliseconds;
            IsInUse = inUse;
            if (inUse) lastUseMilliseconds = nowMilliseconds;
            else if (nowMilliseconds - lastUseMilliseconds >= IdleClearMilliseconds && HasWater)
                Clear();
        }

        internal void Clear()
        {
            Drops.Clear(); remainder = spawn = 0;
            UploadUniforms();
        }

        internal void AddDrop(float x, float y, float radius)
        {
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(radius)) return;
            if (Drops.Count >= MaxDrops) return;
            Drops.Add(new Drop
            {
                X = Math.Clamp(x, .03f, .97f), Y = Math.Clamp(y, .03f, .97f),
                Radius = Math.Clamp(radius, .0035f, MaxDropRadius),
                Shape = .93f + (float)waterRandom.NextDouble() * .22f,
                Seed = (float)waterRandom.NextDouble(), Fade = 1
            });
        }

        internal void Update(float dt, float rain)
        {
            if (!float.IsFinite(dt) || dt <= 0) return;
            remainder += Math.Min(dt, .25f);
            const double step = 1.0 / 60;
            while (remainder + 1e-8 >= step)
            {
                Tick((float)step, AtmosphericOpticsModel.Unit(rain));
                remainder -= step;
            }
            UploadUniforms();
        }

        private void Tick(float dt, float rain)
        {
            for (int i = Drops.Count - 1; i >= 0; i--)
                if ((Drops[i].Fade -= dt / (rain > 0 ? 90f : 45f)) <= 0) Drops.RemoveAt(i);
            spawn += dt * rain * 16;
            while (spawn >= 1)
            {
                spawn--;
                AddDrop(.04f + (float)waterRandom.NextDouble() * .92f,
                    .04f + (float)waterRandom.NextDouble() * .92f,
                    .004f + MathF.Pow((float)waterRandom.NextDouble(), 1.7f) * .012f);
            }
            for (int i = 0; i < Drops.Count; i++)
            for (int j = Drops.Count - 1; j > i; j--)
            {
                Drop a = Drops[i], b = Drops[j];
                float dx = a.X - b.X, dy = a.Y - b.Y, reach = a.Radius + b.Radius;
                if (dx * dx + dy * dy > reach * reach) continue;
                // A real contact can grow a bead; neither gravity nor screen Y can.
                a.Radius = Math.Min(MaxDropRadius, MathF.Sqrt(a.Radius * a.Radius + b.Radius * b.Radius));
                a.Fade = Math.Max(a.Fade, b.Fade);
                Drops.RemoveAt(j);
            }
        }

        private void UploadUniforms()
        {
            Array.Clear(DropUniforms); Array.Clear(DropShapeUniforms);
            for (int i = 0; i < Drops.Count; i++)
            {
                int j = i * 4; Drop d = Drops[i];
                DropUniforms[j] = d.X; DropUniforms[j + 1] = d.Y;
                DropUniforms[j + 2] = d.Radius; DropUniforms[j + 3] = d.Fade;
                DropShapeUniforms[j] = d.Shape; DropShapeUniforms[j + 1] = d.Seed;
            }
        }
    }
}
