using HarmonyLib;
using Vintagestory.API.Client;

namespace spyglass.src.Client.Patches
{
    // Optional integration: no assembly reference or required dependency on Scribe.
    class OptionalHudPatches : BasePatch
    {
        public override void patch(Harmony harmony)
        {
            var type = AccessTools.TypeByName("Scribe.HudScribePins");
            if (type == null || !typeof(GuiDialog).IsAssignableFrom(type))
                return;

            var method = AccessTools.DeclaredMethod(type, nameof(GuiDialog.ShouldReceiveRenderEvents), System.Type.EmptyTypes);
            if (method != null && method.ReturnType == typeof(bool))
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(OptionalHudPatches), nameof(ShouldRenderPrefix)));
        }

        public static bool ShouldRenderPrefix(GuiDialog __instance, ref bool __result)
        {
            if (!GuiDialogHudPatch.HideGui(__instance))
                return true;

            __result = false;
            return false;
        }
    }
}
