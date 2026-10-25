using System;
using System.Collections.Generic;

namespace spyglass.src.Client
{
    // Sparse lens-local water state. All movement is vertical; the shader receives
    // explicit circles and path segments instead of a procedural noise field.
    internal sealed class LensRainModel
    {
        internal const int MaxDrops = 12, MaxBeads = 24, MaxTrails = 16, MaxFrost = 2;
        private const float MaxDropRadius = .024f;
        internal enum DropKind { Small, Medium, Large }

        internal sealed class Drop { internal float X, Y, Radius, Speed, LastTrailY, NextTrail, Shape, Fade, TrailChance; internal DropKind Kind; internal bool MayRoll => TrailChance > 0; }
        internal sealed class Bead { internal float X, Y, Radius, Fade; }
        internal sealed class Trail { internal float X, Top, Bottom, WidthTop, WidthBottom, Fade; }
        internal sealed class Crystal { internal float X, Y, Radius, Fade; }

        internal readonly List<Drop> Drops = new();
        internal readonly List<Bead> Beads = new();
        internal readonly List<Trail> Trails = new();
        internal readonly List<Crystal> Crystals = new();
        internal readonly float[] DropUniforms = new float[MaxDrops * 4];
        internal readonly float[] BeadUniforms = new float[MaxBeads * 4];
        internal readonly float[] TrailUniforms = new float[MaxTrails * 8];
        internal readonly float[] FrostUniforms = new float[MaxFrost * 4];
        internal float Pool { get; private set; }
        internal float Drained { get; private set; }
        private readonly Random random = new(90417);
        private double remainder, spawn;

        internal bool HasWater => Drops.Count > 0 || Beads.Count > 0 || Trails.Count > 0 || Crystals.Count > 0 || Pool > .0001f;

        internal void Clear()
        {
            Drops.Clear(); Beads.Clear(); Trails.Clear(); Crystals.Clear(); Pool = 0; Drained = 0;
            remainder = spawn = 0;
            Array.Clear(DropUniforms); Array.Clear(BeadUniforms); Array.Clear(TrailUniforms); Array.Clear(FrostUniforms);
        }

        internal void AddDrop(float x, float y, float radius)
        {
            if (Drops.Count >= MaxDrops) return;
            float clamped = Math.Clamp(radius, .006f, MaxDropRadius);
            DropKind kind = clamped >= .018f ? DropKind.Large : clamped >= .011f ? DropKind.Medium : DropKind.Small;
            float roll = (float)random.NextDouble();
            float trailChance = kind == DropKind.Large ? 1f : kind == DropKind.Medium && roll < .28f ? 1f : 0f;
            Drops.Add(new Drop { X = Math.Clamp(x, .02f, .98f), Y = Math.Clamp(y, .02f, .94f),
                Radius = clamped, Kind = kind, Shape = kind == DropKind.Large ? 1.20f : kind == DropKind.Medium ? (roll < .5f ? 1.10f : 1.0f) : .98f,
                TrailChance = trailChance, Fade = 1, LastTrailY = Math.Clamp(y, .02f, .94f),
                NextTrail = .018f + (float)random.NextDouble() * .018f });
        }

        internal void Update(float dt, float rain, bool frostConditions = false)
        {
            if (!float.IsFinite(dt) || dt <= 0) return;
            remainder += Math.Min(dt, .25f);
            const double step = 1.0 / 60;
            while (remainder + 1e-8 >= step) { Tick((float)step, AtmosphericOpticsModel.Unit(rain), frostConditions); remainder -= step; }
            UploadUniforms();
        }

        private void Tick(float dt, float rain, bool frostConditions)
        {
            spawn += dt * rain * 5.0;
            while (spawn >= 1)
            {
                spawn--;
                float kindRoll = (float)random.NextDouble();
                float radius = kindRoll < .50f ? .0065f + (float)random.NextDouble() * .004f
                    : kindRoll < .80f ? .011f + (float)random.NextDouble() * .0065f
                    : .018f + (float)random.NextDouble() * .005f;
                AddDrop(.04f + (float)random.NextDouble() * .92f, .03f + (float)random.NextDouble() * .42f, radius);
            }
            for (int i = Trails.Count - 1; i >= 0; i--)
                if ((Trails[i].Fade -= dt / 60f) <= 0) Trails.RemoveAt(i);
            for (int i = Beads.Count - 1; i >= 0; i--)
                if ((Beads[i].Fade -= dt / 60f) <= 0) Beads.RemoveAt(i);
            for (int i = Crystals.Count - 1; i >= 0; i--)
            {
                Crystal crystal = Crystals[i];
                if (frostConditions) crystal.Fade = Math.Min(1, crystal.Fade + dt / 4f);
                else crystal.Fade -= dt / 12f;
                if (crystal.Fade <= 0) Crystals.RemoveAt(i);
            }
            if (frostConditions && Crystals.Count < MaxFrost && random.NextDouble() < dt * .12)
            {
                int count = Math.Min(MaxFrost - Crystals.Count, random.Next(1, 3));
                for (int i = 0; i < count; i++)
                    Crystals.Add(new Crystal { X = .08f + (float)random.NextDouble() * .84f,
                        Y = .08f + (float)random.NextDouble() * .78f, Radius = .012f + (float)random.NextDouble() * .012f, Fade = .01f });
            }

            MergeMovingDrops();
            MergeMovingDropsIntoBeads();
            for (int i = Drops.Count - 1; i >= 0; i--)
            {
                Drop drop = Drops[i];
                if (!drop.MayRoll)
                {
                    drop.Fade = Math.Max(0, drop.Fade - dt / (rain > 0 ? 300f : 45f));
                    if (drop.Fade <= 0) Drops.RemoveAt(i);
                    continue;
                }
                drop.Radius = Math.Max(.003f, drop.Radius - dt * (rain > 0 ? .00001f : .0003f));
                if (drop.Kind != DropKind.Small)
                {
                    drop.Speed = Math.Min(.34f, drop.Speed + dt * (.018f + drop.Radius * 1.8f));
                    drop.Y += drop.Speed * dt;
                    if (drop.TrailChance > 0 && drop.Y - drop.LastTrailY >= drop.NextTrail)
                    {
                        float width = Math.Clamp(drop.Radius * (.42f + (float)random.NextDouble() * .32f), .003f, .014f);
                        AddTrail(drop.X, drop.LastTrailY, drop.Y, width, width * (.65f + (float)random.NextDouble() * .35f));
                        if (Beads.Count < MaxBeads && random.NextDouble() < .12f * drop.TrailChance)
                            Beads.Add(new Bead { X = drop.X, Y = drop.Y, Radius = Math.Clamp(width * .7f, .003f, .009f), Fade = 1 });
                        drop.LastTrailY = drop.Y;
                        drop.NextTrail = drop.Kind == DropKind.Large ? .026f + (float)random.NextDouble() * .018f
                            : .016f + (float)random.NextDouble() * .022f;
                    }
                }
                if (drop.Y + drop.Radius >= 1f - Pool)
                {
                    // Keep the bottom meniscus visually attached to the final
                    // tracked runout segment instead of ending as a separate pool.
                    if (drop.MayRoll && drop.Y > drop.LastTrailY + .001f)
                    {
                        float width = Math.Clamp(drop.Radius * .7f, .004f, .016f);
                        AddTrail(drop.X, drop.LastTrailY, 1f - Pool, width, width * 1.1f);
                    }
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
                if (b.Kind > a.Kind) { a.Kind = b.Kind; a.Shape = b.Shape; a.TrailChance = b.TrailChance; }
                else a.TrailChance = Math.Max(a.TrailChance, b.TrailChance);
                if (a.Kind != DropKind.Small) a.Fade = 1;
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
            Array.Clear(DropUniforms); Array.Clear(BeadUniforms); Array.Clear(TrailUniforms); Array.Clear(FrostUniforms);
            for (int i = 0; i < Drops.Count; i++) { int j = i * 4; Drop d = Drops[i]; DropUniforms[j] = d.X; DropUniforms[j + 1] = d.Y; DropUniforms[j + 2] = d.Radius; DropUniforms[j + 3] = d.Shape * d.Fade; }
            for (int i = 0; i < Beads.Count; i++) { int j = i * 4; Bead b = Beads[i]; BeadUniforms[j] = b.X; BeadUniforms[j + 1] = b.Y; BeadUniforms[j + 2] = b.Radius; BeadUniforms[j + 3] = b.Fade; }
            for (int i = 0; i < Trails.Count; i++) { int j = i * 8; Trail t = Trails[i]; TrailUniforms[j] = t.X; TrailUniforms[j + 1] = t.X; TrailUniforms[j + 2] = t.Top; TrailUniforms[j + 3] = t.Bottom; TrailUniforms[j + 4] = t.WidthTop; TrailUniforms[j + 5] = t.WidthBottom; TrailUniforms[j + 6] = t.Fade; TrailUniforms[j + 7] = 1; }
            for (int i = 0; i < Crystals.Count; i++) { int j = i * 4; Crystal c = Crystals[i]; FrostUniforms[j] = c.X; FrostUniforms[j + 1] = c.Y; FrostUniforms[j + 2] = c.Radius; FrostUniforms[j + 3] = c.Fade; }
        }
    }
}
