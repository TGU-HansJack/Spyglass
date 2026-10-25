using spyglass.src;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace spyglass.src.Client
{
    class ZoomWheel : GameWheel, IRenderer
    {
		private bool disposed;
		public double RenderOrder => 1.1;
		public int RenderRange => 999;
		private double lastAppearanceCheck;
		private float lastLensLight = 1f;
		private string lastLensMaterial;
		private string lastLensGlass;

        public ZoomWheel(ICoreClientAPI capi) : base(capi)
		{
			// F4 skips the entire Ortho stage, so a HUD visibility override alone
			// cannot retain the physical lens. Done runs even with the HUD hidden.
			capi.Event.RegisterRenderer(this, EnumRenderStage.Done, SpyglassMod.ModId + "-lens");
		}

		public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
		{
			if (disposed || stage != EnumRenderStage.Done || !capi.HideGuis || !IsEnabled()) return;
			RenderWithoutHud(deltaTime);
		}

		private void RenderWithoutHud(float deltaTime)
		{
			var render = capi.Render;
			if (render.FrameWidth <= 0 || render.FrameHeight <= 0) return;
			using var state = new OpticsRenderState();
			var previousShader = render.CurrentActiveShader;
			if (capi.World is not ClientMain game) return;
			var previousGui = game.guiShaderProg;
			previousShader?.Stop();
			game.guiShaderProg = ShaderPrograms.Gui;
			render.OrthoMode(render.FrameWidth, render.FrameHeight);
			try
			{
				render.GlToggleBlend(true);
				OpenTK.Graphics.OpenGL.GL.Disable(OpenTK.Graphics.OpenGL.EnableCap.DepthTest);
				game.guiShaderProg.Use();
				OnRenderGUI(deltaTime);
			}
			finally
			{
				game.guiShaderProg.Stop();
				game.guiShaderProg = previousGui;
				render.PerspectiveMode();
				previousShader?.Use();
			}
		}

		public override void Dispose()
		{
			if (disposed) return;
			disposed = true;
			capi.Event.UnregisterRenderer(this, EnumRenderStage.Done);
			base.Dispose();
		}

		public override bool IsEnabled()
		{
			return ClientManipulation.EnableEffect() && ClientManipulation.getPercentZoomed() > 0.01;
		}

		public override void BeforeRenderGUI(float deltaTime)
		{
			if (SpyglassMod.gameRuntime - lastAppearanceCheck > 0.25)
			{
				lastAppearanceCheck = SpyglassMod.gameRuntime;
				float currentLight = ClientManipulation.GetLensLightLevel();
				string currentMaterial = ClientManipulation.GetHeldSpyglassMaterial();
				string currentGlass = ClientManipulation.GetHeldLensGlass();
				if (Math.Abs(currentLight - lastLensLight) > 0.025f || currentMaterial != lastLensMaterial || currentGlass != lastLensGlass)
				{
					lastLensLight = currentLight;
					lastLensMaterial = currentMaterial;
					lastLensGlass = currentGlass;
					RequestRecompose();
				}
			}

			float percent = ClientManipulation.getPercentZoomed();
			if (SpyglassMod.config.GetVinetteStyle() == VignetteStyle.none)
			{
				SingleComposer.Color.W = 0f;
				return;
			}

			// The outer eye-cup must be opaque. Applying edgeOpacity here would make
			// the whole element translucent, including the area outside the lens.
			// Zoom amount is capped at the selected ratio (normally 0.97),
			// so using it as opacity would leave metal transparent at rest.
			SingleComposer.Color.W = Math.Clamp(percent / SpyglassMod.getZoomRatio(), 0f, 1f);
		}

		public override GuiComposer adjustCompose(GuiComposer composer)
		{
			composer.Color = new Vec4f(1f, 1f, 1f, 0f);
			VignetteStyle style = SpyglassMod.config.GetVinetteStyle();
			if (style == VignetteStyle.none)
				return composer;

            return composer.AddInteractiveElement(new SpyglassOverlay(capi, ElementBounds.Fill, SpyglassMod.config.edgeSize, SpyglassMod.config.lensSize, SpyglassMod.config.glassColor, SpyglassMod.config.glassBrightness, SpyglassMod.config.edgeOpacity, style, ClientManipulation.GetHeldSpyglassMaterial(), ClientManipulation.GetLensLightLevel(), ClientManipulation.GetHeldLensGlass()));
		}

		public override void OnMouseWheel(MouseWheelEventArgs args)
		{
			if (ClientManipulation.EnableEffect() && SpyglassMod.zoomed && SpyglassMod.config.enableMouseWheelAdjustment)
			{
				args.SetHandled(true);
				float fDelta = args.delta;
                SpyglassMod.setZoomRatio( SpyglassMod.getZoomRatio() + fDelta / ClientManipulation.GetWheelAdjustment() );
			}
		}
	}
}
