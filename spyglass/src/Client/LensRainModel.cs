using System;
using System.Collections.Generic;

namespace spyglass.src.Client
{
    // Adapted from Anti-Productivity RainyGlass, Copyright (c) 2026 DoraNeko (MIT).
    // Top-left UV positions; radii and velocities use the lens's shorter side.
    internal sealed class LensRainModel
    {
        internal const int MaxDrops = 80;
        internal const float MaxDropRadius = .04f, MoveRadius = .014f;
        internal const long IdleClearMilliseconds = 30000;
        internal enum StampKind { Erase, Small, Trail }
        internal readonly record struct Stamp(float X0, float Y0, float X1, float Y1,
            float Radius, float Height, float Seed, StampKind Kind, double Time);
        internal sealed class Drop
        {
            internal float X, Y, Radius, Shape, Fade, Seed, Vx, Vy, Stretch, StallTimer, TrailDistance;
            internal bool Moving;
        }
        internal readonly List<Drop> Drops = new();
        internal readonly List<Stamp> Stamps = new();
        internal readonly float[] DropUniforms = new float[MaxDrops * 4];
        internal readonly float[] DropShapeUniforms = new float[MaxDrops * 4];
        private readonly Random waterRandom = new(90417);
        private double remainder, spawn, smallSpawn;
        private float surfaceLife, aspectX = 1, aspectY = 1;
        private long lastUseMilliseconds = -1;
        internal bool IsInUse { get; private set; }
        internal bool HasWater => Drops.Count > 0 || surfaceLife > 0;
        internal int Generation { get; private set; }
        internal float PendingSeconds { get; private set; }
        internal double SurfaceTime { get; private set; }
        private float Rand() => (float)waterRandom.NextDouble();
        private float Rand(float a, float b) => a + (b - a) * Rand();

        internal void SetAspect(float width, float height)
        {
            if (!float.IsFinite(width) || !float.IsFinite(height) || width <= 0 || height <= 0) return;
            float shorter = Math.Min(width, height);
            aspectX = width / shorter; aspectY = height / shorter;
        }

        internal void UpdateUsage(long nowMilliseconds, bool inUse)
        {
            if (nowMilliseconds < 0) return;
            if (lastUseMilliseconds < 0 || nowMilliseconds < lastUseMilliseconds)
                lastUseMilliseconds = nowMilliseconds;
            IsInUse = inUse;
            if (inUse) lastUseMilliseconds = nowMilliseconds;
            else if (nowMilliseconds - lastUseMilliseconds >= IdleClearMilliseconds && HasWater) Clear();
        }

        internal void Clear()
        {
            Drops.Clear(); Stamps.Clear(); remainder = spawn = smallSpawn = 0;
            surfaceLife = PendingSeconds = 0; SurfaceTime = 0; Generation++;
            UploadUniforms();
        }

        internal void AddDrop(float x, float y, float radius)
        {
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(radius)) return;
            if (Drops.Count >= MaxDrops)
            {
                // Adhering beads must not permanently occupy every physics slot.
                // Preserve the least visible small bead in the persistent map,
                // allowing fresh impacts to keep feeding flowing drops in rain.
                int settled = -1;
                float smallest = float.MaxValue;
                for (int i = 0; i < Drops.Count; i++)
                {
                    var d = Drops[i];
                    float visibility = d.Radius * d.Radius * d.Fade;
                    if (d.Moving || d.Radius > MoveRadius || visibility >= smallest) continue;
                    settled = i; smallest = visibility;
                }
                if (settled < 0) return;
                var bead = Drops[settled];
                SmallDrop(bead.X, bead.Y, bead.Radius, bead.Fade);
                Drops.RemoveAt(settled);
            }
            float r = Math.Clamp(radius, .0035f, MaxDropRadius);
            x = Math.Clamp(x, .02f, .98f); y = Math.Clamp(y, .02f, .98f);
            Drops.Add(new Drop {
                X = Math.Clamp(x, .02f, .98f), Y = Math.Clamp(y, .02f, .98f), Radius = r,
                Shape = Rand(.93f, 1.10f), Seed = Rand(), Fade = 1, StallTimer = Rand(.1f, .5f)
            });
            StampSurface(x, y, x, y, r * 1.05f, 0, 0, StampKind.Erase);
        }

        private void StampSurface(float x0, float y0, float x1, float y1, float radius, float height, float seed, StampKind kind)
        {
            // Bound deferred uploads when no usable scene buffer is available.
            if (Stamps.Count >= 4096) Stamps.RemoveRange(0, 1024);
            Stamps.Add(new Stamp(x0, y0, x1, y1, radius, height, seed, kind, SurfaceTime));
            if (kind != StampKind.Erase) surfaceLife = Math.Max(surfaceLife, kind == StampKind.Small ? 45 : 15);
        }
        private void SmallDrop(float x, float y, float radius, float fade = 1)
            => StampSurface(x, y, x, y, radius, .5f * fade, Rand(), StampKind.Small);
        internal void AcknowledgeSurface() { Stamps.Clear(); PendingSeconds = 0; }

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
            bool hadWater = HasWater;
            SurfaceTime += dt; PendingSeconds += dt; surfaceLife = Math.Max(0, surfaceLife - dt);
            spawn += dt * rain * 16; smallSpawn += dt * rain * 45;
            while (spawn >= 1)
            {
                spawn--;
                AddDrop(Rand(.025f, .975f), Rand(.025f, .975f), .004f + MathF.Pow(Rand(), 1.7f) * .016f);
            }
            while (smallSpawn >= 1)
            {
                smallSpawn--;
                SmallDrop(Rand(.01f, .99f), Rand(.01f, .99f), .0015f + MathF.Pow(Rand(), 2) * .003f);
            }
            for (int i = Drops.Count - 1; i >= 0; i--)
            {
                Drop d = Drops[i]; d.Fade -= dt / (rain > 0 ? 90f : 45f);
                Step(d, dt);
                if (d.Fade <= 0 || d.Y - d.Radius / aspectY > 1 || d.Radius < .0025f) Drops.RemoveAt(i);
            }
            for (int i = 0; i < Drops.Count; i++)
            for (int j = Drops.Count - 1; j > i; j--)
            {
                Drop a = Drops[i], b = Drops[j];
                float dx = (a.X - b.X) * aspectX, dy = (a.Y - b.Y) * aspectY;
                float reach = (a.Radius + b.Radius) * .85f;
                if (dx * dx + dy * dy > reach * reach) continue;
                if (!a.Moving && (b.Moving || b.Radius > a.Radius))
                { Drops[i] = b; Drops[j] = a; (a, b) = (b, a); }
                float aa = a.Radius * a.Radius, bb = b.Radius * b.Radius, weight = bb / (aa + bb);
                a.X += (b.X - a.X) * weight * (a.Moving ? .5f : 1);
                if (!a.Moving) a.Y += (b.Y - a.Y) * weight;
                else a.Vy = Math.Min(.75f, a.Vy + .035f);
                a.Radius = Math.Min(MaxDropRadius, MathF.Sqrt(aa + bb)); a.Fade = Math.Max(a.Fade, b.Fade);
                StampSurface(a.X, a.Y, a.X, a.Y, a.Radius * 1.05f, 0, 0, StampKind.Erase);
                Drops.RemoveAt(j);
            }
            if (hadWater && !HasWater)
            {
                // Fully dry glass must not revive old texture residue on the next rain.
                Stamps.Clear(); PendingSeconds = 0; Generation++;
            }
        }

        private void Step(Drop d, float dt)
        {
            if (!d.Moving)
            {
                if (d.Radius > MoveRadius && Rand() < ((d.Radius / MoveRadius - 1) * 5 + .4f) * dt)
                { d.Moving = true; d.Vy = .02f; }
                d.Stretch *= MathF.Exp(-6 * dt);
                if (!d.Moving) return;
            }
            float mass = d.Radius / MoveRadius;
            d.Vy += .85f * (mass - .6f) * dt; d.Vy *= MathF.Exp(-2.2f * dt);
            d.StallTimer -= dt;
            if (d.StallTimer <= 0)
            {
                d.StallTimer = Rand(.08f, .6f); float chance = Rand();
                if (chance < .25f) d.Vy *= .15f;
                else if (chance < .45f) d.Vy += .11f * mass;
                d.Vx += Rand(-.028f, .028f);
            }
            d.Vy = Math.Clamp(d.Vy, 0, .75f); d.Vx *= MathF.Exp(-5 * dt);
            float x0 = d.X, y0 = d.Y;
            d.X = Math.Clamp(d.X + d.Vx * dt / aspectX, d.Radius / aspectX, 1 - d.Radius / aspectX);
            d.Y += d.Vy * dt / aspectY;
            float dx = (d.X - x0) * aspectX, dy = (d.Y - y0) * aspectY;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            d.Radius -= distance * .0035f;
            float targetStretch = Math.Clamp(d.Vy / (d.Radius * 14 + .0001f), 0, .6f);
            d.Stretch += (targetStretch - d.Stretch) * Math.Min(1, dt * 10);
            if (distance > .00001f)
            {
                StampSurface(x0, y0, d.X, d.Y, d.Radius * .95f, 0, 0, StampKind.Erase);
                float tail = d.Radius * .4f / aspectY;
                StampSurface(x0, y0 - tail, d.X, d.Y - tail, d.Radius * .32f, .45f * d.Fade, d.Seed, StampKind.Trail);
            }
            d.TrailDistance += distance;
            if (d.TrailDistance > d.Radius * 1.1f)
            {
                d.TrailDistance = 0;
                if (Rand() < .7f) SmallDrop(d.X + Rand(-.3f, .3f) * d.Radius / aspectX,
                    d.Y - d.Radius * Rand(1.2f, 2.2f) / aspectY, d.Radius * Rand(.12f, .32f));
            }
            if (d.Radius < MoveRadius * .65f && d.Vy < .055f) d.Moving = false;
        }

        private void UploadUniforms()
        {
            Array.Clear(DropUniforms); Array.Clear(DropShapeUniforms);
            for (int i = 0; i < Drops.Count; i++)
            {
                int j = i * 4; Drop d = Drops[i];
                DropUniforms[j] = d.X; DropUniforms[j + 1] = d.Y;
                DropUniforms[j + 2] = d.Radius; DropUniforms[j + 3] = d.Fade;
                DropShapeUniforms[j] = d.Shape; DropShapeUniforms[j + 1] = d.Seed; DropShapeUniforms[j + 2] = d.Stretch;
            }
        }
    }
}
