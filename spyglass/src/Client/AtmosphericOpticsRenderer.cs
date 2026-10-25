using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using Vintagestory.API.Common;

namespace spyglass.src.Client
{
    internal sealed class AtmosphericOpticsRenderer : IRenderer
    {
        private const string ShaderName = "spyglassforkatmosphere";
        private readonly ICoreClientAPI capi;
        private readonly HashSet<string> warnings = new();
        private readonly float[] inverseProjection = new float[16];
        private readonly int[] viewport = new int[4];
        private WeatherSystemClient weather;
        private IShaderProgram shader;
        private MeshRef quad;
        private bool disposed, failed;
        private bool sceneRenderedToPrimary;
        private long lastWeatherSample = long.MinValue;
        private float targetExtinction, extinction;
        private float targetLensRain;
        private readonly LensRainModel rain = new();
        private string rainLens;
        private bool frostConditions;

        public double RenderOrder => 0.5;
        public int RenderRange => 999;

        internal AtmosphericOpticsRenderer(ICoreClientAPI capi)
        {
            this.capi = capi;
            capi.Event.ReloadShader += ReloadShader;
            capi.Event.RegisterRenderer(this, EnumRenderStage.AfterOIT, ShaderName + "-depth");
            capi.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, ShaderName);
        }

        private void WarnOnce(string reason)
        {
            if (warnings.Add(reason)) capi.Logger.Warning("[spyglassfork] Atmospheric optics skipped: {0}", reason);
        }

        private bool ReloadShader()
        {
            if (disposed) return true;
            shader?.Dispose();
            shader = null;
            failed = false;
            warnings.Clear();
            // Lazy compilation keeps a shader failure from failing the game's reload.
            return true;
        }

        private bool EnsureResources()
        {
            if (failed) return false;
            if (shader == null || shader.Disposed)
            {
                shader = capi.Shader.NewShaderProgram();
                shader.AssetDomain = SpyglassMod.ModId;
                capi.Shader.RegisterFileShaderProgram(ShaderName, shader);
                if (!shader.Compile())
                {
                    failed = true;
                    WarnOnce("shader compilation failed; retry with a shader reload");
                    return false;
                }
            }
            quad ??= capi.Render.UploadMesh(QuadMeshUtil.GetQuad());
            return true;
        }

        private void SampleWeather()
        {
            long now = capi.ElapsedMilliseconds;
            if (lastWeatherSample != long.MinValue && now >= lastWeatherSample && now - lastWeatherSample < 250) return;
            lastWeatherSample = now;
            targetExtinction = 0;
            targetLensRain = 0;
            frostConditions = false;
            weather ??= capi.ModLoader.GetModSystem<WeatherSystemClient>();
            if (weather == null || !weather.haveLevelFinalize || !weather.playerChunkLoaded) return;
            var data = weather.BlendedWeatherData;
            var climate = weather.clientClimateCond;
            if (data == null || climate == null) return;
            OpticsPrecipitation kind = data.BlendedPrecType switch
            {
                EnumPrecipitationType.Snow => OpticsPrecipitation.Snow,
                EnumPrecipitationType.Hail => OpticsPrecipitation.Hail,
                EnumPrecipitationType.Auto => OpticsPrecipitation.Auto,
                _ => OpticsPrecipitation.Rain
            };
            kind = AtmosphericOpticsModel.ResolvePrecipitation(kind, climate.Temperature, data.snowThresholdTemp);
            frostConditions = kind == OpticsPrecipitation.Snow || climate.Temperature < -5f;
            // This is the same physical rainfall used by the game's weather particles,
            // before particle-quality settings or shelter suppress particle spawning.
            float rainfall = Math.Max(0, climate.Rainfall - (float)Math.Max(0,
                (capi.World.Player.Entity.Pos.Y - capi.World.SeaLevel - 5000) / 10000));
            targetExtinction = AtmosphericOpticsModel.Extinction(kind, rainfall, 1);
            var entity = capi.World.Player.Entity;
            var pos = entity.Pos;
            int rainHeight = capi.World.BlockAccessor.GetRainMapHeightAt((int)Math.Floor(pos.X), (int)Math.Floor(pos.Z));
            targetLensRain = AtmosphericOpticsModel.LensRainIntensity(kind, rainfall, pos.Y + entity.LocalEyePos.Y, rainHeight);
        }

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            // Also keep disabled/late callbacks independent of the graphics context.
            if (disposed || failed || (!SpyglassMod.config.enableAtmosphericOptics && !SpyglassMod.config.enableLensRain) || !ClientManipulation.EnableEffect())
            {
                sceneRenderedToPrimary = false;
                return;
            }
            try
            {
                RenderFrame(deltaTime, stage);
            }
            catch (Exception e)
            {
                sceneRenderedToPrimary = false;
                failed = true;
                WarnOnce(e.GetType().Name + ": " + e.Message + "; retry with a shader reload");
            }
        }

        // Called by the client tick even if graphics are disabled or the shader
        // has failed. No graphics context is needed to expire an unused lens.
        internal void UpdateUsage(long nowMilliseconds, bool inUse)
            => rain.UpdateUsage(nowMilliseconds, inUse);

        private void RenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (stage == EnumRenderStage.AfterOIT)
            {
                sceneRenderedToPrimary = false;
                if (ClientManipulation.getPercentZoomed() > 0.001f)
                {
                    var frames = capi.Render.FrameBuffers;
                    var frame = frames != null && frames.Count > 0 ? frames[0] : null;
                    sceneRenderedToPrimary = frame != null && !frame.Disposed && frame.FboId > 0
                        && GL.GetInteger(GetPName.DrawFramebufferBinding) == frame.FboId;
                }
                return;
            }
            if (stage != EnumRenderStage.AfterBlit) return;
            bool sceneReady = sceneRenderedToPrimary;
            sceneRenderedToPrimary = false;
            float fade = AtmosphericOpticsModel.Unit(ClientManipulation.getPercentZoomed());
            if (capi.World?.Player?.Entity == null) return;
            var render = capi.Render;
            if (render.ShaderUniforms.CameraUnderwater > 0) { rain.Clear(); frostConditions = false; return; }
            SampleWeather();
            string lens = ClientManipulation.GetHeldSpyglassMaterial() + ":" + ClientManipulation.GetHeldLensGlass();
            // A missing held spyglass resolves to fallback material names. Do
            // not treat putting the item away as switching the actual lens.
            if (rain.IsInUse && lens != rainLens) { rain.Clear(); rainLens = lens; }
            float rainStrength = SpyglassMod.config.enableLensRain
                ? AtmosphericOpticsModel.Strength(SpyglassMod.config.lensRainStrength) : 0;
            frostConditions &= SpyglassMod.config.enableLensRain;
            if (rainStrength <= 0 && !frostConditions) rain.Clear();
            else if (rain.IsInUse) rain.Update(deltaTime, rainStrength > 0 ? targetLensRain : 0, frostConditions);
            if (fade <= 0.001f) return;
            extinction = AtmosphericOpticsModel.Approach(extinction, targetExtinction, deltaTime);
            float coefficient = SpyglassMod.config.enableAtmosphericOptics
                ? extinction * AtmosphericOpticsModel.Strength(SpyglassMod.config.weatherOpticsStrength) : 0;
            var ambient = capi.Ambient;
            var sun = capi.World.Calendar.SunPositionNormalized;
            var uniforms = render.ShaderUniforms;
            var view = render.PerspectiveViewMat;
            if (view == null || view.Length != 16) return;
            float sunX = (float)(view[0] * sun.X + view[4] * sun.Y + view[8] * sun.Z);
            float sunY = (float)(view[1] * sun.X + view[5] * sun.Y + view[9] * sun.Z);
            float sunZ = (float)(view[2] * sun.X + view[6] * sun.Y + view[10] * sun.Z);
            float glare = SpyglassMod.config.enableAtmosphericOptics && sunZ < 0 ? AtmosphericOpticsModel.Glare(sun.Y, uniforms.SunSpecularIntensity,
                ambient.BlendedCloudDensity, ambient.BlendedFogDensity, SpyglassMod.config.sunGlareStrength) : 0;
            if (coefficient <= 0.000001f && glare <= 0.0001f && !rain.HasWater) return;
            var buffers = render.FrameBuffers;
            var primary = buffers != null && buffers.Count > (int)EnumFrameBuffer.Primary ? buffers[(int)EnumFrameBuffer.Primary] : null;
            if (!sceneReady || primary == null || primary.Disposed || primary.DepthTextureId <= 0
                || primary.Width <= 0 || primary.Height <= 0 || primary.ColorTextureIds == null || primary.ColorTextureIds.Length == 0
                || primary.ColorTextureIds[0] <= 0)
            {
                WarnOnce("no usable offscreen scene/depth buffer");
                return;
            }
            // AfterBlit normally targets framebuffer 0. Do not sample an attachment
            // if another renderer has left the primary framebuffer bound.
            if (GL.GetInteger(GetPName.DrawFramebufferBinding) != 0)
            {
                WarnOnce("AfterBlit is not targeting the default framebuffer");
                return;
            }
            if (!AtmosphericOpticsModel.InvertProjection(render.PerspectiveProjectionMat, inverseProjection))
            {
                WarnOnce("invalid perspective projection");
                return;
            }
            GL.GetInteger(GetPName.Viewport, viewport);
            if (viewport[2] <= 0 || viewport[3] <= 0) return;
            // Resource uploads can also change bindings on the first frame/reload.
            using var state = new OpticsRenderState();
            if (!EnsureResources()) return;
            Draw(primary, fade, coefficient, glare, sunX, sunY, sunZ, sun.Y, rainStrength);
        }

        private void Draw(FrameBufferRef primary, float fade, float coefficient, float glare, float sunX, float sunY, float sunZ, float sunElevation, float rainStrength)
        {
            var render = capi.Render;
            var config = SpyglassMod.config;
            var aperture = config.GetVinetteStyle() == VignetteStyle.none
                ? new LensAperture(0, 0, viewport[2], viewport[3])
                : LensAperture.Square(0, 0, viewport[2], viewport[3], config.edgeSize, config.lensSize);
            if (aperture.Width <= 0 || aperture.Height <= 0) return;
            var previousShader = render.CurrentActiveShader;
            previousShader?.Stop();
            try
            {
                GL.Disable(EnableCap.DepthTest);
                GL.DepthMask(false);
                GL.Disable(EnableCap.Blend);
                GL.Disable(EnableCap.CullFace);
                // Keep any pre-existing scissor restriction from other renderers.
                shader.Use();
                shader.BindTexture2D("sceneTexture", primary.ColorTextureIds[0], 0);
                shader.BindTexture2D("depthTexture", primary.DepthTextureId, 1);
                // Explicit samplers are not needed; don't inherit a previous shadow sampler.
                GL.BindSampler(0, 0);
                GL.BindSampler(1, 0);
                shader.UniformMatrix("inverseProjection", inverseProjection);
                shader.Uniform("aperture", (float)(aperture.X / viewport[2]), (float)(1 - (aperture.Y + aperture.Height) / viewport[3]),
                    (float)(aperture.Width / viewport[2]), (float)(aperture.Height / viewport[3]));
                shader.Uniform("frameSize", (float)viewport[2], (float)viewport[3]);
                shader.Uniform("fade", fade);
                shader.Uniform("lensRainStrength", rainStrength);
                shader.Uniform("lensLight", ClientManipulation.GetLensLightLevel());
                shader.Uniform("rainDropCount", rain.Drops.Count);
                shader.Uniform("frostCount", rain.Crystals.Count);
                shader.Uniforms4("rainDrops", LensRainModel.MaxDrops, rain.DropUniforms);
                shader.Uniforms4("rainDropShapes", LensRainModel.MaxDrops, rain.DropShapeUniforms);
                shader.Uniforms4("frostCrystals", LensRainModel.MaxFrost, rain.FrostUniforms);
                shader.Uniforms4("frostDetails", LensRainModel.MaxFrost, rain.FrostDetailUniforms);
                shader.Uniforms4("frostFacets", LensRainModel.MaxFrostFacets, rain.FrostFacetUniforms);
                shader.Uniforms4("frostFacetShapes", LensRainModel.MaxFrostFacets, rain.FrostFacetShapeUniforms);
                float rimInset = config.GetVinetteStyle() == VignetteStyle.none ? 0 : (float)(SpyglassOverlay.MetalWidth * 1.1 / aperture.Width);
                shader.Uniform("rainInset", Math.Min(.45f, rimInset), Math.Min(.45f, rimInset));
                shader.Uniform("extinction", coefficient);
                shader.Uniform("dayWeight", AtmosphericOpticsModel.DayWeight(sunElevation));
                shader.Uniform("glareStrength", glare);
                shader.Uniform("sunViewDirection", sunX, sunY, sunZ);
                var screen = render.ShaderUniforms.SunPositionScreen;
                shader.Uniform("sunScreen", screen.X * 0.5f + 0.5f, screen.Y * 0.5f + 0.5f);
                shader.Uniform("fogColor", capi.Ambient.BlendedFogColor.X, capi.Ambient.BlendedFogColor.Y, capi.Ambient.BlendedFogColor.Z);
                float brightness = ClientSettings.BrightnessLevel + Math.Max(0, render.ShaderUniforms.DropShadowIntensity * 2 - 1.66f) / 3;
                shader.Uniform("colorGrade", ClientSettings.GammaLevel, ClientSettings.ExtraGammaLevel, brightness, render.ShaderUniforms.ExtraContrastLevel);
                shader.Uniform("sepia", render.ShaderUniforms.SepiaLevel + render.ShaderUniforms.ExtraSepia);
                render.RenderMesh(quad);
            }
            finally
            {
                shader.Stop();
                previousShader?.Use();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            capi.Event.UnregisterRenderer(this, EnumRenderStage.AfterBlit);
            capi.Event.UnregisterRenderer(this, EnumRenderStage.AfterOIT);
            capi.Event.ReloadShader -= ReloadShader;
            shader?.Dispose();
            shader = null;
            quad?.Dispose();
            quad = null;
            weather = null;
        }
    }
}
