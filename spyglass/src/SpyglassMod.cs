using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using spyglass.src.Client;
using spyglass.src.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using Vintagestory.Server;

namespace spyglass.src
{
    class SpyglassMod : ModSystem
    {
        internal const string ModId = "spyglassfork";
        internal const string ChannelName = ModId + "Channel";
        internal const float MIN_ZOOM = 0.8f;
        internal const float MAX_ZOOM = 1.0f;

        public static bool zoomed = false; // enabled by item.
        private static float zoomRatio = 0.0f; // adjusted via mouse-wheel while zoomed.

        private ClientManipulation clientLogic;
        private ICoreClientAPI clientApi;
        private ZoomWheel zoomWheel;
        private AtmosphericOpticsRenderer atmosphericOptics;
        private long? tickListenerId;
        internal static double gameRuntime = 0; // used by Util/ClientTime

        private const string configFile = ModId + ".json";
        private static SpyglassConfig loadedConfig;

        internal static float getZoomRatio()
        {
            return zoomRatio;
        }

        internal static void setZoomRatio(float value)
        {
            zoomRatio = value;
            if (zoomRatio < SpyglassMod.MIN_ZOOM) zoomRatio = MIN_ZOOM;
            if (zoomRatio > SpyglassMod.MAX_ZOOM) zoomRatio = MAX_ZOOM;
        }

        public static SpyglassConfig config
        {
            get => loadedConfig;
        }

        internal static void setRatioToDefault()
        {
            setZoomRatio(MIN_ZOOM + (MAX_ZOOM - MIN_ZOOM) * config.defaultZoomPosition);
        }

        internal static void ResetZoomRatio()
        {
            if (!config.preserveZoomBetweenUses)
                setRatioToDefault();
        }

        internal static void FlickLensWater() => instance?.atmosphericOptics?.FlickWater();
        private static SpyglassMod instance;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            try
            {
                loadedConfig = api.LoadModConfig<SpyglassConfig>(configFile);
                if (loadedConfig == null) throw new FileNotFoundException();
            }
            catch (Exception)
            {
                loadedConfig = new SpyglassConfig();
                api.StoreModConfig<SpyglassConfig>(loadedConfig, configFile);
            }

            api.RegisterItemClass(ModId + ":ItemSpyglass", typeof(ItemSpyglass));
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            instance = this;
            clientApi = api;
            gameRuntime = 0;
            clientLogic = new ClientManipulation(api);
            api.Network.RegisterChannel(ChannelName)
                .RegisterMessageType(typeof(ServerConfigSyncPacket))
                .SetMessageHandler<ServerConfigSyncPacket>(serverConfig =>
                {
                    loadedConfig.vignetteStyle = serverConfig.vignetteStyle.ToString();
                    loadedConfig.edgeOpacity = serverConfig.edgeOpacity;
                    loadedConfig.edgeSize = serverConfig.edgeSize;
                    loadedConfig.lensSize = serverConfig.lensSize;
                    zoomWheel?.RequestRecompose();
                });
            zoomWheel = new ZoomWheel(api);
            api.Gui.RegisterDialog(zoomWheel);
            atmosphericOptics = new AtmosphericOpticsRenderer(api);
            tickListenerId = api.Event.RegisterGameTickListener(OnGameTick, 4);
            setRatioToDefault();
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            var serverNetworkChannel = api.Network.RegisterChannel(ChannelName)
                .RegisterMessageType(typeof(ServerConfigSyncPacket));
            api.Event.PlayerJoin += byPlayer =>
            {
                if (loadedConfig.overrideClientConfig)
                {
                    serverNetworkChannel.SendPacket(new ServerConfigSyncPacket()
                    {
                        edgeOpacity = loadedConfig.edgeOpacity,
                        edgeSize = loadedConfig.edgeSize,
                        lensSize = loadedConfig.lensSize,
                        vignetteStyle = loadedConfig.GetVinetteStyle()
                    }, byPlayer);
                }
            };
        }

        private void OnGameTick(float dt)
        {
            gameRuntime += (double)dt;
        }

        public override void Dispose()
        {
            atmosphericOptics?.Dispose();
            atmosphericOptics = null;
            if (instance == this) instance = null;
            if (tickListenerId.HasValue)
            {
                clientApi.Event.UnregisterGameTickListener(tickListenerId.Value);
                tickListenerId = null;
            }
            if ( clientLogic != null )
            {
                clientLogic.Dispose();
                clientLogic = null;
            }
            zoomWheel?.Dispose();
            zoomWheel = null;
            clientApi = null;
            base.Dispose();
        }

    }
}
