using System;
using System.Collections.Generic;

namespace spyglass.src.Client
{
    // Lens coordinates run from top-left to bottom-right. Surface beads never move.
    internal sealed class LensRainModel
    {
        internal const int MaxDrops = 80, MaxFrost = 2, FacetsPerCrystal = 72, MaxFrostFacets = MaxFrost * FacetsPerCrystal;
        internal const float MaxDropRadius = .024f;
        internal const long IdleClearMilliseconds = 30000;
        private long lastUseMilliseconds = -1;
        internal bool IsInUse { get; private set; }
        internal sealed class Drop
        {
            internal float X, Y, Radius, Shape, Fade, Seed;
        }
        internal sealed class Crystal
        {
            internal float X, Y, Radius, Fade, Growth, Angle, Seed;
            internal readonly List<IceFacet> Facets = new();
        }
        internal readonly record struct IceFacet(float AX, float AY, float BX, float BY, float Width, float Thickness);

        internal readonly List<Drop> Drops = new();
        internal readonly List<Crystal> Crystals = new();
        internal readonly float[] DropUniforms = new float[MaxDrops * 4];
        internal readonly float[] DropShapeUniforms = new float[MaxDrops * 4];
        internal readonly float[] FrostUniforms = new float[MaxFrost * 4];
        internal readonly float[] FrostDetailUniforms = new float[MaxFrost * 4];
        internal readonly float[] FrostFacetUniforms = new float[MaxFrostFacets * 4];
        internal readonly float[] FrostFacetShapeUniforms = new float[MaxFrostFacets * 4];
        private readonly Random waterRandom = new(90417), frostRandom = new(7811);
        private double remainder, spawn;
        internal bool HasWater => Drops.Count > 0 || Crystals.Count > 0;

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
                    var crystal = new Crystal
                    {
                        X = edge == 0 ? .012f : edge == 1 ? .988f : along,
                        Y = edge == 2 ? .012f : edge == 3 ? .988f : along,
                        Angle = edge == 0 ? 0 : edge == 1 ? MathF.PI : edge == 2 ? MathF.PI / 2 : -MathF.PI / 2,
                        // Each patch extends over a sixth of the full lens even
                        // after the metal rim's inset is taken into account.
                        Radius = .34f + (float)frostRandom.NextDouble() * .07f,
                        Seed = (float)frostRandom.NextDouble(), Fade = .01f, Growth = 0
                    };
                    BuildCrystal(crystal);
                    Crystals.Add(crystal);
                }
            }
        }

        // Finite seeded geometry, built once on formation. The reference's
        // feathered hierarchy is recreated from tapered ice prisms, not pixels.
        private static void BuildCrystal(Crystal crystal)
        {
            var random = new Random((int)(crystal.Seed * 1000000) + 319);
            float Sample(float lo, float hi) => lo + (float)random.NextDouble() * (hi - lo);
            for (int stem = 0; stem < 2; stem++)
            {
                float lean = stem == 0 ? Sample(-.28f, -.12f) : Sample(.12f, .30f);
                float reach = stem == 0 ? .98f : Sample(.78f, .91f);
                float originY = Sample(-.035f, .035f);
                float Curve(float t) => originY + lean * t * t + MathF.Sin(t * 3.1f) * .028f;
                float previousX = 0, previousY = originY;
                for (int segment = 1; segment <= 4; segment++)
                {
                    float t = segment / 4f, x = t * reach, y = Curve(t);
                    crystal.Facets.Add(new IceFacet(previousX, previousY, x, y, .009f - .004f * t, Sample(.60f, .90f)));
                    previousX = x; previousY = y;
                }
                for (int branch = 0; branch < 8; branch++)
                {
                    float t = .10f + branch * .105f + Sample(-.012f, .012f);
                    float x = t * reach, y = Curve(t);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float length = (.30f - t * .16f) * Sample(.80f, 1.12f);
                        float vx = Sample(.52f, .69f), vy = side * Sample(.75f, .90f) + lean * .30f;
                        float norm = MathF.Sqrt(vx * vx + vy * vy); vx /= norm; vy /= norm;
                        float bx = x + vx * length, by = y + vy * length;
                        float width = (.0085f - t * .003f) * Sample(.72f, 1.20f);
                        crystal.Facets.Add(new IceFacet(x, y, bx, by, width, Sample(.45f, .90f)));
                        // Secondary needles vary in length and taper; alternate
                        // attachment points avoid a complete six-arm snowflake.
                        float joint = branch % 2 == 0 ? .43f : .64f;
                        float tx = x + vx * length * joint, ty = y + vy * length * joint;
                        float twig = length * Sample(.25f, .40f);
                        crystal.Facets.Add(new IceFacet(tx, ty, tx + (vx * .38f + Math.Abs(vy) * .60f) * twig,
                            ty + (vy * .38f + vx * side * .76f) * twig, width * .52f, Sample(.28f, .55f)));
                    }
                }
            }
            if (crystal.Facets.Count > FacetsPerCrystal) crystal.Facets.RemoveRange(FacetsPerCrystal, crystal.Facets.Count - FacetsPerCrystal);
        }

        private void UploadUniforms()
        {
            Array.Clear(DropUniforms); Array.Clear(DropShapeUniforms);
            Array.Clear(FrostUniforms); Array.Clear(FrostDetailUniforms);
            Array.Clear(FrostFacetUniforms); Array.Clear(FrostFacetShapeUniforms);
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
                FrostDetailUniforms[j + 3] = c.Facets.Count;
                for (int f = 0; f < c.Facets.Count; f++)
                {
                    int k = (i * FacetsPerCrystal + f) * 4; IceFacet facet = c.Facets[f];
                    FrostFacetUniforms[k] = facet.AX; FrostFacetUniforms[k + 1] = facet.AY;
                    FrostFacetUniforms[k + 2] = facet.BX; FrostFacetUniforms[k + 3] = facet.BY;
                    FrostFacetShapeUniforms[k] = facet.Width; FrostFacetShapeUniforms[k + 1] = facet.Thickness;
                }
            }
        }
    }
}
