using System;
using System.Collections.Generic;

namespace spyglass.src.Client
{
    // Persistent water in lens-local coordinates (Y points down). Fixed steps
    // keep merging, gravity and drainage independent of render frame rate.
    internal sealed class LensRainModel
    {
        internal const int MaxDrops = 32, MaxTrails = 48;
        internal sealed class Drop
        {
            internal float X, Y, Radius, Speed, StartY, Drift, LastTrailX, LastTrailY, NextTrail, Pause, Phase;
        }
        internal sealed class Trail
        {
            internal float XTop, XBottom, Top, Bottom, WidthTop, WidthBottom, Life, Gap;
        }
        internal sealed class Bead
        {
            internal float X, Y, Radius;
        }
        internal readonly List<Drop> Drops = new();
        internal readonly List<Trail> Trails = new();
        internal readonly List<Bead> Beads = new();
        internal readonly float[] DropUniforms = new float[MaxDrops * 4];
        internal readonly float[] TrailUniforms = new float[MaxTrails * 8];
        internal readonly float[] BeadUniforms = new float[MaxDrops * 4];
        internal float Pool { get; private set; }
        internal float Drained { get; private set; }
        private Random random = new(90417);
        private double remainder, spawn;

        internal bool HasWater => Drops.Count > 0 || Trails.Count > 0 || Beads.Count > 0 || Pool > 0.0001f;

        internal void Clear()
        {
            Drops.Clear(); Trails.Clear(); Beads.Clear(); Pool = 0; Drained = 0;
            Array.Clear(DropUniforms); Array.Clear(TrailUniforms); Array.Clear(BeadUniforms);
            remainder = spawn = 0; random = new Random(90417);
        }

        internal void AddDrop(float x, float y, float radius)
        {
            if (Drops.Count >= MaxDrops)
            {
                // At the render budget, incoming water feeds the nearest bead
                // rather than being discarded or creating unlimited particles.
                Drop nearest = Drops[0]; float distance = float.MaxValue;
                foreach (var drop in Drops)
                {
                    float dx = drop.X - x, dy = drop.Y - y, d = dx * dx + dy * dy;
                    if (d < distance) { nearest = drop; distance = d; }
                }
                nearest.Radius = MathF.Sqrt(nearest.Radius * nearest.Radius + radius * radius);
                return;
            }
            float startY = Math.Clamp(y, 0, 1);
            Drops.Add(new Drop
            {
                X = Math.Clamp(x, 0, 1), Y = startY, StartY = startY, LastTrailX = Math.Clamp(x, 0, 1), LastTrailY = startY,
                Radius = radius, Drift = 0,
                NextTrail = .012f + (float)random.NextDouble() * .022f,
                Pause = 0,
                Phase = (float)random.NextDouble() * 6.2831855f
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
            Array.Clear(DropUniforms); Array.Clear(TrailUniforms);
            for (int i = 0; i < Drops.Count; i++)
            {
                var d = Drops[i]; int j = i * 4;
                DropUniforms[j] = d.X; DropUniforms[j + 1] = d.Y;
                DropUniforms[j + 2] = d.Radius; DropUniforms[j + 3] = d.Speed;
            }
            for (int i = 0; i < Trails.Count; i++)
            {
                var t = Trails[i]; int j = i * 8;
                TrailUniforms[j] = t.XTop; TrailUniforms[j + 1] = t.XBottom;
                TrailUniforms[j + 2] = t.Top; TrailUniforms[j + 3] = t.Bottom;
                TrailUniforms[j + 4] = t.WidthTop; TrailUniforms[j + 5] = t.WidthBottom;
                TrailUniforms[j + 6] = t.Life / 30f; TrailUniforms[j + 7] = t.Gap;
            }
            for (int i = 0; i < Beads.Count; i++)
            {
                var b = Beads[i]; int j = i * 4;
                BeadUniforms[j] = b.X; BeadUniforms[j + 1] = b.Y;
                BeadUniforms[j + 2] = b.Radius; BeadUniforms[j + 3] = 1;
            }
        }

        private void Tick(float dt, float rain)
        {
            spawn += dt * rain * 14;
            while (spawn >= 1)
            {
                spawn--;
                AddDrop(.025f + (float)random.NextDouble() * .95f, .025f + (float)random.NextDouble() * .90f,
                    .007f + (float)random.NextDouble() * .012f);
            }
            for (int i = Trails.Count - 1; i >= 0; i--)
                if ((Trails[i].Life -= dt) <= 0) Trails.RemoveAt(i);

            // Small beads remain lodged in the runoff track until a moving drop
            // reaches them; they then merge into the moving water.
            for (int i = Beads.Count - 1; i >= 0; i--)
                Beads[i].Radius = Math.Max(.0025f, Beads[i].Radius - dt * (rain > 0 ? .00001f : .00002f));

            // Two touching beads conserve their lens-surface area when merging.
            for (int i = 0; i < Drops.Count; i++)
                for (int j = Drops.Count - 1; j > i; j--)
                {
                    var a = Drops[i]; var b = Drops[j];
                    float dx = a.X - b.X, dy = a.Y - b.Y, reach = a.Radius + b.Radius;
                    if (dx * dx + dy * dy >= reach * reach) continue;
                    float ma = a.Radius * a.Radius, mb = b.Radius * b.Radius;
                    a.X = (a.X * ma + b.X * mb) / (ma + mb);
                    a.Y = (a.Y * ma + b.Y * mb) / (ma + mb);
                    a.StartY = Math.Min(a.StartY, b.StartY);
                    a.LastTrailY = Math.Min(a.LastTrailY, b.LastTrailY);
                    a.LastTrailX = (a.LastTrailX * ma + b.LastTrailX * mb) / (ma + mb);
                    a.Drift = 0;
                    a.NextTrail = Math.Min(a.NextTrail, b.NextTrail);
                    a.Pause = Math.Max(a.Pause, b.Pause);
                    a.Radius = MathF.Sqrt(ma + mb);
                    a.Speed = Math.Max(a.Speed, b.Speed);
                    Drops.RemoveAt(j);
                }
            for (int i = Drops.Count - 1; i >= 0; i--)
            {
                var d = Drops[i];
                d.Radius = Math.Max(0, d.Radius - dt * (rain > 0 ? .00005f : .0006f));
                if (d.Radius < .002f) { Drops.RemoveAt(i); continue; }
                for (int j = Beads.Count - 1; j >= 0; j--)
                {
                    var bead = Beads[j];
                    if (Math.Abs(bead.Y - d.Y) > bead.Radius + d.Radius * .8f) continue;
                    d.Radius = MathF.Sqrt(d.Radius * d.Radius + bead.Radius * bead.Radius);
                    Beads.RemoveAt(j);
                }
                // Even small falling drops move vertically; only the tiny beads
                // deposited on an old track remain parked until another drop
                // reaches them.
                if (d.Radius > .0065f || d.Speed > 0)
                {
                    float pulse = .45f + .55f * (0.5f + 0.5f * MathF.Sin(d.Phase + d.Y * 27));
                    d.Speed = Math.Min(.45f, d.Speed + dt * Math.Max(.015f, (d.Radius - .01f) * (5 + 4 * pulse)));
                    d.Y += d.Speed * dt;
                    d.Drift = 0;
                    if (d.Y - d.LastTrailY >= d.NextTrail && d.Y > d.LastTrailY)
                    {
                        float width = d.Radius * (.18f + (float)random.NextDouble() * .48f);
                        AddTrail(d.LastTrailX, d.LastTrailY, d.X, d.Y, width,
                            width * (.65f + (float)random.NextDouble() * .8f),
                            12f + (float)random.NextDouble() * 18f);
                        if (random.NextDouble() < .55 && Beads.Count < MaxDrops)
                            Beads.Add(new Bead
                            {
                                X = d.X,
                                Y = d.Y,
                                Radius = Math.Clamp(width * (.45f + (float)random.NextDouble() * .9f), .003f, .012f)
                            });
                        d.LastTrailY = d.Y;
                        d.LastTrailX = d.X;
                        d.NextTrail = .010f + (float)random.NextDouble() * .027f;
                    }
                }
                if (d.Y + d.Radius >= 1 - Pool)
                {
                    Pool = Math.Min(.055f, Pool + d.Radius * d.Radius * 12);
                    Drops.RemoveAt(i);
                }
            }
            for (int i = 0; i < Beads.Count; i++)
                for (int j = Beads.Count - 1; j > i; j--)
                {
                    var a = Beads[i]; var b = Beads[j];
                    if (Math.Abs(a.Y - b.Y) >= a.Radius + b.Radius) continue;
                    float aa = a.Radius * a.Radius, bb = b.Radius * b.Radius;
                    a.X = (a.X * aa + b.X * bb) / (aa + bb);
                    a.Radius = MathF.Sqrt(aa + bb);
                    Beads.RemoveAt(j);
                }
            float drainage = Math.Min(Pool, dt * (.0005f + Pool * .08f));
            Pool -= drainage;
            Drained += drainage;
        }

        private void AddTrail(float xTop, float top, float xBottom, float bottom, float widthTop, float widthBottom, float life)
        {
            if (Trails.Count == MaxTrails) Trails.RemoveAt(0);
            Trails.Add(new Trail
            {
                XTop = Math.Clamp(xTop, .005f, .995f), XBottom = Math.Clamp(xBottom, .005f, .995f),
                Top = Math.Clamp(top, 0, 1), Bottom = Math.Clamp(bottom, 0, 1),
                WidthTop = widthTop, WidthBottom = widthBottom, Life = life,
                Gap = (float)random.NextDouble()
            });
        }

        internal void SettleAllDrops()
        {
            for (int i = 0; i < Drops.Count && Beads.Count < MaxDrops; i++)
            {
                var drop = Drops[i];
                Beads.Add(new Bead { X = drop.X, Y = drop.Y, Radius = Math.Clamp(drop.Radius, .003f, .015f) });
            }
            Drops.Clear();
            Array.Clear(DropUniforms);
            Array.Clear(BeadUniforms);
            for (int i = 0; i < Beads.Count; i++)
            {
                int j = i * 4; var bead = Beads[i];
                BeadUniforms[j] = bead.X; BeadUniforms[j + 1] = bead.Y;
                BeadUniforms[j + 2] = bead.Radius; BeadUniforms[j + 3] = 1;
            }
        }
    }
}
