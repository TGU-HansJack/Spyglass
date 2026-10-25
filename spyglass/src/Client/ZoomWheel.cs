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
    class ZoomWheel : GameWheel
    {
		private double lastAppearanceCheck;
		private float lastLensLight = 1f;
		private string lastLensMaterial;

        public ZoomWheel(ICoreClientAPI capi) : base(capi)
		{
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
				if (Math.Abs(currentLight - lastLensLight) > 0.025f || currentMaterial != lastLensMaterial)
				{
					lastLensLight = currentLight;
					lastLensMaterial = currentMaterial;
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
			SingleComposer.Color.W = Math.Max(0f, Math.Min(1f, percent));
		}

		public override GuiComposer adjustCompose(GuiComposer composer)
		{
			composer.Color = new Vec4f(1f, 1f, 1f, 0f);
			VignetteStyle style = SpyglassMod.config.GetVinetteStyle();
			if (style == VignetteStyle.none)
				return composer;

            return composer.AddInteractiveElement(new SpyglassOverlay(capi, ElementBounds.Fill, SpyglassMod.config.edgeSize, SpyglassMod.config.lensSize, SpyglassMod.config.glassColor, SpyglassMod.config.glassBrightness, SpyglassMod.config.edgeOpacity, style, ClientManipulation.GetHeldSpyglassMaterial(), ClientManipulation.GetLensLightLevel()));
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
