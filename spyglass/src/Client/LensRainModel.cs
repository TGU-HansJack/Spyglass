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
            internal float X, Y, Radius, Speed, StartY, Drift, LastTrailY, NextTrail;
        }
        internal sealed class Trail
        {
            internal float X, Y, Width, Life;
        }
        internal readonly List<Drop> Drops = new();
        internal readonly List<Trail> Trails = new();
        internal readonly float[] DropUniforms = new float[MaxDrops * 4];
        internal readonly float[] TrailUniforms = new float[MaxTrails * 4];
        internal float Pool { get; private set; }
        internal float Drained { get; private set; }
        private Random random = new(90417);
        private double remainder, spawn;

        internal bool HasWater => Drops.Count > 0 || Trails.Count > 0 || Pool > 0.0001f;

        internal void Clear()
        {
            Drops.Clear(); Trails.Clear(); Pool = 0; Drained = 0;
            Array.Clear(DropUniforms); Array.Clear(TrailUniforms);
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
                X = Math.Clamp(x, 0, 1), Y = startY, StartY = startY, LastTrailY = startY,
                Radius = radius, Drift = (float)(random.NextDouble() - .5) * .045f,
                NextTrail = .012f + (float)random.NextDouble() * .022f
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
                var t = Trails[i]; int j = i * 4;
                TrailUniforms[j] = t.X; TrailUniforms[j + 1] = t.Y;
                TrailUniforms[j + 2] = t.Width; TrailUniforms[j + 3] = t.Life / 2.5f;
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
                    a.Drift = (a.Drift * ma + b.Drift * mb) / (ma + mb);
                    a.NextTrail = Math.Min(a.NextTrail, b.NextTrail);
                    a.Radius = MathF.Sqrt(ma + mb);
                    a.Speed = Math.Max(a.Speed, b.Speed);
                    Drops.RemoveAt(j);
                }
            for (int i = Drops.Count - 1; i >= 0; i--)
            {
                var d = Drops[i];
                d.Radius = Math.Max(0, d.Radius - dt * (rain > 0 ? .00005f : .0006f));
                if (d.Radius < .002f) { Drops.RemoveAt(i); continue; }
                if (d.Radius > .014f || d.Speed > 0)
                {
                    d.Speed = Math.Min(.45f, d.Speed + dt * Math.Max(.015f, (d.Radius - .01f) * 8));
                    d.Y += d.Speed * dt;
                    d.Drift = Math.Clamp(d.Drift + ((float)random.NextDouble() - .5f) * dt * .012f, -.045f, .045f);
                    d.X = Math.Clamp(d.X + d.Drift * dt * (.35f + d.Speed * 2), .015f, .985f);
                    if (d.Y - d.LastTrailY >= d.NextTrail)
                    {
                        float width = d.Radius * (.18f + (float)random.NextDouble() * .48f);
                        AddTrail(d.X + d.Drift * .035f, d.Y - d.NextTrail * .35f, width,
                            .65f + (float)random.NextDouble() * 1.55f);
                        if (random.NextDouble() < .12 && width > .003f)
                            AddTrail(d.X - d.Drift * .055f + ((float)random.NextDouble() - .5f) * d.Radius,
                                d.Y - d.NextTrail * .7f, width * (.24f + (float)random.NextDouble() * .25f),
                                .35f + (float)random.NextDouble() * .8f);
                        d.LastTrailY = d.Y;
                        d.NextTrail = .010f + (float)random.NextDouble() * .027f;
                    }
                }
                if (d.Y + d.Radius >= 1 - Pool)
                {
                    Pool = Math.Min(.055f, Pool + d.Radius * d.Radius * 12);
                    Drops.RemoveAt(i);
                }
            }
            float drainage = Math.Min(Pool, dt * (.0005f + Pool * .08f));
            Pool -= drainage;
            Drained += drainage;
        }

        private void AddTrail(float x, float y, float width, float life)
        {
            if (Trails.Count == MaxTrails) Trails.RemoveAt(0);
            Trails.Add(new Trail { X = Math.Clamp(x, .005f, .995f), Y = Math.Clamp(y, 0, 1), Width = width, Life = life });
        }
    }
}
