using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;

namespace spyglass.src.Client
{
	// base class that allows using OnMouseWheel without breaking things.
	abstract class GameWheel : HudElement
	{
		private bool wasEnabled = false;
		private bool recomposeRequested;
		private const string ComposerName = SpyglassMod.ModId + "-zoomoverlay";

		public override bool Focusable
		{
			get => false;
		}

		public override double InputOrder
		{
			get => 1.1;
		}

		public GameWheel(ICoreClientAPI capi) : base(capi)
		{
			SingleComposer = capi.Gui.CreateCompo(ComposerName, ElementBounds.Empty).Compose(true);
		}

		public override bool TryClose()
		{
			return false;
		}

		public override void OnOwnPlayerDataReceived()
		{
			TryOpen();
		}
		public override void OnRenderGUI(float deltaTime)
		{
			bool enabled = IsEnabled();
			if (wasEnabled != enabled || recomposeRequested)
			{
				wasEnabled = enabled;
				SingleComposer = adjustCompose(capi.Gui.CreateCompo(ComposerName, wasEnabled ? ElementBounds.Fill : ElementBounds.Empty)).Compose(true);
				recomposeRequested = false;
			}

			if (wasEnabled)
			{
				BeforeRenderGUI(deltaTime);
				base.OnRenderGUI(deltaTime);
			}
		}

		public void RequestRecompose()
		{
			recomposeRequested = true;
		}

		// when enabled some mouse actions are blocked, so best only enable when in use.
		public abstract bool IsEnabled();

		public virtual void BeforeRenderGUI(float deltaTime)
		{
		}

		public virtual GuiComposer adjustCompose(GuiComposer composer)
		{
			return composer;
		}

		public override void OnMouseDown(MouseEvent args)
		{
		}

		public override void OnMouseUp(MouseEvent args)
		{
		}

		public override void OnMouseMove(MouseEvent args)
		{
		}

	}
}
