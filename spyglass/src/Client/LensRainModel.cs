using System;
using System.Collections.Generic;

namespace spyglass.src.Client
{
    // Sparse lens-local water state. All movement is vertical; the shader receives
    // explicit circles and path segments instead of a procedural noise field.
    internal sealed class LensRainModel
    {
        internal const int MaxDrops = 12, MaxBeads = 24, MaxTrails = 16;
        private const float MaxDropRadius = .024f;

        internal sealed class Drop { internal float X, Y, Radius, Speed, LastTrailY, NextTrail; }
        internal sealed class Bead { internal float X, Y, Radius, Fade; }
        internal sealed class Trail { internal float X, Top, Bottom, WidthTop, WidthBottom, Fade; }

        internal readonly List<Drop> Drops = new();
        internal readonly List<Bead> Beads = new();
        internal readonly List<Trail> Trails = new();
        internal readonly float[] DropUniforms = new float[MaxDrops * 4];
        internal readonly float[] BeadUniforms = new float[MaxBeads * 4];
        internal readonly float[] TrailUniforms = new float[MaxTrails * 8];
        internal float Pool { get; private set; }
        internal float Drained { get; private set; }
        private readonly Random random = new(90417);
        private double remainder, spawn;

        internal bool HasWater => Drops.Count > 0 || Beads.Count > 0 || Trails.Count > 0 || Pool > .0001f;

        internal void Clear()
        {
            Drops.Clear(); Beads.Clear(); Trails.Clear(); Pool = 0; Drained = 0;
            remainder = spawn = 0;
            Array.Clear(DropUniforms); Array.Clear(BeadUniforms); Array.Clear(TrailUniforms);
        }

        internal void AddDrop(float x, float y, float radius)
        {
            if (Drops.Count >= MaxDrops) return;
            Drops.Add(new Drop { X = Math.Clamp(x, .02f, .98f), Y = Math.Clamp(y, .02f, .94f),
                Radius = Math.Clamp(radius, .006f, MaxDropRadius), LastTrailY = Math.Clamp(y, .02f, .94f),
                NextTrail = .018f + (float)random.NextDouble() * .018f });
        }

        internal void Update(float dt, float rain)
        {
            if (!float.IsFinite(dt) || dt <= 0) return;
            remainder += Math.Min(dt, .25f);
            const double step = 1.0 / 60;
            while (remainder + 1e-8 >= step) { Tick((float)step, AtmosphericOpticsModel.Unit(rain)); remainder -= step; }
            UploadUniforms();
        }

        private void Tick(float dt, float rain)
        {
            spawn += dt * rain * 5.0;
            while (spawn >= 1)
            {
                spawn--;
                AddDrop(.04f + (float)random.NextDouble() * .92f, .03f + (float)random.NextDouble() * .42f,
                    .007f + (float)random.NextDouble() * .010f);
            }
            for (int i = Trails.Count - 1; i >= 0; i--)
                if ((Trails[i].Fade -= dt / 60f) <= 0) Trails.RemoveAt(i);
            for (int i = Beads.Count - 1; i >= 0; i--)
                if ((Beads[i].Fade -= dt / 60f) <= 0) Beads.RemoveAt(i);

            MergeMovingDrops();
            MergeMovingDropsIntoBeads();
            for (int i = Drops.Count - 1; i >= 0; i--)
            {
                Drop drop = Drops[i];
                drop.Radius = Math.Max(.003f, drop.Radius - dt * (rain > 0 ? .00001f : .0003f));
                drop.Speed = Math.Min(.34f, drop.Speed + dt * (.018f + drop.Radius * 1.8f));
                drop.Y += drop.Speed * dt;
                if (drop.Y - drop.LastTrailY >= drop.NextTrail)
                {
                    float width = Math.Clamp(drop.Radius * (.42f + (float)random.NextDouble() * .32f), .003f, .014f);
                    AddTrail(drop.X, drop.LastTrailY, drop.Y, width, width * (.65f + (float)random.NextDouble() * .35f));
                    if (Beads.Count < MaxBeads && random.NextDouble() < .35)
                        Beads.Add(new Bead { X = drop.X, Y = drop.Y, Radius = Math.Clamp(width * .7f, .003f, .009f), Fade = 1 });
                    drop.LastTrailY = drop.Y; drop.NextTrail = .016f + (float)random.NextDouble() * .022f;
                }
                if (drop.Y + drop.Radius >= 1f - Pool)
                {
                    Pool = Math.Min(.055f, Pool + drop.Radius * drop.Radius * 15f);
                    Drops.RemoveAt(i);
                }
            }
            float drainage = Math.Min(Pool, dt * (.00025f + Pool * .035f));
            Pool -= drainage; Drained += drainage;
        }

        private void MergeMovingDrops()
        {
            for (int i = 0; i < Drops.Count; i++)
            for (int j = Drops.Count - 1; j > i; j--)
            {
                Drop a = Drops[i], b = Drops[j];
                if (Math.Abs(a.X - b.X) > Math.Max(a.Radius, b.Radius) * 1.15f || Math.Abs(a.Y - b.Y) > a.Radius + b.Radius) continue;
                a.Radius = Math.Min(MaxDropRadius, MathF.Sqrt(a.Radius * a.Radius + b.Radius * b.Radius));
                a.Speed = Math.Max(a.Speed, b.Speed); a.LastTrailY = Math.Min(a.LastTrailY, b.LastTrailY);
                Drops.RemoveAt(j);
            }
        }

        private void MergeMovingDropsIntoBeads()
        {
            for (int i = Drops.Count - 1; i >= 0; i--)
            for (int j = Beads.Count - 1; j >= 0; j--)
            {
                Drop drop = Drops[i]; Bead bead = Beads[j];
                if (Math.Abs(drop.X - bead.X) > drop.Radius + bead.Radius || Math.Abs(drop.Y - bead.Y) > drop.Radius + bead.Radius) continue;
                drop.Radius = Math.Min(MaxDropRadius, MathF.Sqrt(drop.Radius * drop.Radius + bead.Radius * bead.Radius));
                Beads.RemoveAt(j);
            }
        }

        private void AddTrail(float x, float top, float bottom, float widthTop, float widthBottom)
        {
            Trail existing = Trails.Find(t => Math.Abs(t.X - x) < .004f && Math.Abs(t.Bottom - top) < .035f);
            if (existing != null) { existing.Bottom = Math.Max(existing.Bottom, bottom); existing.WidthBottom = Math.Max(existing.WidthBottom, widthBottom); existing.Fade = 1; return; }
            if (Trails.Count >= MaxTrails) return;
            Trails.Add(new Trail { X = x, Top = top, Bottom = bottom, WidthTop = widthTop, WidthBottom = widthBottom, Fade = 1 });
        }

        private void UploadUniforms()
        {
            Array.Clear(DropUniforms); Array.Clear(BeadUniforms); Array.Clear(TrailUniforms);
            for (int i = 0; i < Drops.Count; i++) { int j = i * 4; Drop d = Drops[i]; DropUniforms[j] = d.X; DropUniforms[j + 1] = d.Y; DropUniforms[j + 2] = d.Radius; DropUniforms[j + 3] = d.Speed; }
            for (int i = 0; i < Beads.Count; i++) { int j = i * 4; Bead b = Beads[i]; BeadUniforms[j] = b.X; BeadUniforms[j + 1] = b.Y; BeadUniforms[j + 2] = b.Radius; BeadUniforms[j + 3] = b.Fade; }
            for (int i = 0; i < Trails.Count; i++) { int j = i * 8; Trail t = Trails[i]; TrailUniforms[j] = t.X; TrailUniforms[j + 1] = t.X; TrailUniforms[j + 2] = t.Top; TrailUniforms[j + 3] = t.Bottom; TrailUniforms[j + 4] = t.WidthTop; TrailUniforms[j + 5] = t.WidthBottom; TrailUniforms[j + 6] = t.Fade; TrailUniforms[j + 7] = 1; }
        }
    }
}
