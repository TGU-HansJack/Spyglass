using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Util;

namespace spyglass.src
{
    class ItemSpyglass : Item
    {
        public override string GetHeldTpUseAnimation(ItemSlot activeHotbarSlot, Entity byEntity)
        {
            return SpyglassMod.ModId + "-spyglassing";
        }

        public override WorldInteraction[] GetHeldInteractionHelp( ItemSlot inSlot )
        {
            var interactions = new List<WorldInteraction>
            {
                new WorldInteraction
                {
                    ActionLangCode = SpyglassMod.ModId + ":item-spyglass-use",
                    MouseButton = EnumMouseButton.Right
                },
                new WorldInteraction
                {
                    ActionLangCode = SpyglassMod.ModId + ":item-spyglass-flick-water",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "ctrl"
                }
            };
            interactions.AddRange(base.GetHeldInteractionHelp(inSlot));
            return interactions.ToArray();
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (byEntity.Controls.CtrlKey)
            {
                if (byEntity.World.Side == EnumAppSide.Client && ClientManipulation.IsLocalPlayer(byEntity))
                    SpyglassMod.FlickLensWater();
                handling = EnumHandHandling.Handled;
                return;
            }
            if (!byEntity.Controls.Sneak)
            {
                handling = EnumHandHandling.Handled;
                updateState(true, byEntity);
            }
            else
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent,ref handling);
        }

        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            if (byEntity.Controls.CtrlKey)
            {
                updateState(false, byEntity);
                return false;
            }
            if (!byEntity.Controls.Sneak)
            {
                updateState(true, byEntity);
                return true;
            }
            else
               return base.OnHeldInteractStep(secondsUsed, slot, byEntity, blockSel, entitySel);
        }

        public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            updateState(false, byEntity);
        }

        public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, EnumItemUseCancelReason cancelReason)
        {
            updateState(false, byEntity);
            return true;
        }

        private void updateState(bool newState, EntityAgent byEntity)
        {
            if (byEntity.World.Side == EnumAppSide.Client)
            {
                if (ClientManipulation.IsLocalPlayer(byEntity))
                {
                    SpyglassMod.zoomed = newState;
                }
            }
        }
    }

}
