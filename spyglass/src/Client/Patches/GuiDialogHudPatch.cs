using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace spyglass.src.Client.Patches
{
    class GuiDialogHudPatch : BasePatch
    {
        public static bool HideGui(GuiDialog target)
        {
            if (!SpyglassMod.config.hideHUDWhileSpying)
                return false;
            if (target is ZoomWheel)
                return false;
            if (target is Vintagestory.Client.NoObf.IGameSettingsHandler)
                return false;
            // With no lens selected there is no mask to compete with block info.
            if (target is Vintagestory.Client.NoObf.HudElementBlockAndEntityInfo
                && SpyglassMod.config.GetVinetteStyle() == VignetteStyle.none)
                return false;
            return ClientManipulation.HideGuis();
        }

        public static IEnumerable<CodeInstruction> AdjustGuiOpacity_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator g)
        {
            List<CodeInstruction> output = new List<CodeInstruction>();

            Label returnEqual = g.DefineLabel();
            output.Add(new CodeInstruction(OpCodes.Ldarg_0));
            output.Add(new CodeInstruction(OpCodes.Call, typeof(GuiDialogHudPatch).GetMethod("HideGui", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)));
            output.Add(new CodeInstruction(OpCodes.Brtrue, returnEqual));

            /* we are just adding this code to the beginning of the function
             *
             * if ( Patcher.HideGui(this) ) return false;
             *
             * After that its the same method.
             *
             */

            CodeInstruction jumpToLabel = new CodeInstruction(OpCodes.Nop);
            jumpToLabel.labels.Add(returnEqual);

            output.AddRange(instructions);

            output.Add(jumpToLabel);
            output.Add(new CodeInstruction(OpCodes.Ldc_I4_0));
            output.Add(new CodeInstruction(OpCodes.Ret));

            return output;
        }

        public override void patch(Harmony harmony)
        {
            // Server settings arrive after patch installation. Decide visibility at
            // render time so edge -> circle and circle -> edge both work.
            var transpiler = new HarmonyMethod(typeof(GuiDialogHudPatch), nameof(AdjustGuiOpacity_Transpiler));
            harmony.Patch(AccessTools.Method(typeof(GuiDialog), nameof(GuiDialog.ShouldReceiveRenderEvents)), transpiler: transpiler);
            harmony.Patch(AccessTools.DeclaredMethod(typeof(Vintagestory.Client.NoObf.HudElementBlockAndEntityInfo), nameof(GuiDialog.ShouldReceiveRenderEvents)), transpiler: transpiler);
        }

    }
}
