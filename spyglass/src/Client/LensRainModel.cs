using System;
using System.Collections.Generic;

namespace spyglass.src.Client
{
    // Lens coordinates run from top-left to bottom-right. Surface beads never move.
    internal sealed class LensRainModel
    {
        internal const int MaxDrops = 80, MaxFrost = 2;
        internal const float MaxDropRadius = .024f;
        internal sealed class Drop
        {
            internal float X, Y, Radius, Shape, Fade, Seed;
        }
        internal sealed class Crystal
        {
            internal float X, Y, Radius, Fade, Growth, Angle, Seed;
        }

        internal readonly List<Drop> Drops = new();
        internal readonly List<Crystal> Crystals = new();
        internal readonly float[] DropUniforms = new float[MaxDrops * 4];
        internal readonly float[] DropShapeUniforms = new float[MaxDrops * 4];
        internal readonly float[] FrostUniforms = new float[MaxFrost * 4];
        internal readonly float[] FrostDetailUniforms = new float[MaxFrost * 4];
        private readonly Random waterRandom = new(90417), frostRandom = new(7811);
        private double remainder, spawn;
        internal bool HasWater => Drops.Count > 0 || Crystals.Count > 0;

        internal void Clear()
        {
            Drops.Clear(); Crystals.Clear(); remainder = spawn = 0;
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

        internal void Update(float dt, float rain, bool frostConditions = false)
        {
            if (!float.IsFinite(dt) || dt <= 0) return;
            remainder += Math.Min(dt, .25f);
            const double step = 1.0 / 60;
            while (remainder + 1e-8 >= step)
            {
                Tick((float)step, AtmosphericOpticsModel.Unit(rain), frostConditions);
                remainder -= step;
            }
            UploadUniforms();
        }

        private void Tick(float dt, float rain, bool frostConditions)
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
            for (int i = Crystals.Count - 1; i >= 0; i--)
            {
                Crystal c = Crystals[i];
                if (frostConditions)
                {
                    c.Fade = Math.Min(1, c.Fade + dt / 2);
                    c.Growth = Math.Min(1, c.Growth + dt / 8);
                }
                else c.Fade -= dt / 12;
                if (c.Fade <= 0) Crystals.RemoveAt(i);
            }
            if (frostConditions && Crystals.Count < MaxFrost && frostRandom.NextDouble() < dt * .12)
            {
                int count = Math.Min(MaxFrost - Crystals.Count, frostRandom.Next(1, 3));
                for (int i = 0; i < count; i++)
                {
                    int edge = frostRandom.Next(4);
                    float along = .20f + (float)frostRandom.NextDouble() * .60f;
                    Crystals.Add(new Crystal
                    {
                        X = edge == 0 ? .012f : edge == 1 ? .988f : along,
                        Y = edge == 2 ? .012f : edge == 3 ? .988f : along,
                        Angle = edge == 0 ? 0 : edge == 1 ? MathF.PI : edge == 2 ? MathF.PI / 2 : -MathF.PI / 2,
                        // Each patch extends over a sixth of the full lens even
                        // after the metal rim's inset is taken into account.
                        Radius = .34f + (float)frostRandom.NextDouble() * .07f,
                        Seed = (float)frostRandom.NextDouble(), Fade = .01f, Growth = 0
                    });
                }
            }
        }

        private void UploadUniforms()
        {
            Array.Clear(DropUniforms); Array.Clear(DropShapeUniforms);
            Array.Clear(FrostUniforms); Array.Clear(FrostDetailUniforms);
            for (int i = 0; i < Drops.Count; i++)
            {
                int j = i * 4; Drop d = Drops[i];
                DropUniforms[j] = d.X; DropUniforms[j + 1] = d.Y;
                DropUniforms[j + 2] = d.Radius; DropUniforms[j + 3] = d.Fade;
                DropShapeUniforms[j] = d.Shape; DropShapeUniforms[j + 1] = d.Seed;
            }
            for (int i = 0; i < Crystals.Count; i++)
            {
                int j = i * 4; Crystal c = Crystals[i];
                FrostUniforms[j] = c.X; FrostUniforms[j + 1] = c.Y;
                FrostUniforms[j + 2] = c.Radius; FrostUniforms[j + 3] = c.Fade;
                FrostDetailUniforms[j] = c.Angle; FrostDetailUniforms[j + 1] = c.Growth;
                FrostDetailUniforms[j + 2] = c.Seed;
            }
        }
    }
}
