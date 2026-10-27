using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.Client.NoObf;

if (args.Length < 2)
    throw new ArgumentException("Usage: CompatibilityTests <game-directory> <fork-dll> [optional-mod-dll-directory]");
var search = new[] { args[0], Path.Combine(args[0], "Lib"), Path.Combine(args[0], "Mods"), args.Length > 2 ? args[2] : "" };
AssemblyLoadContext.Default.Resolving += (_, name) => {
    foreach (var directory in search.Where(Directory.Exists)) {
        var path = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
    }
    return null;
};
return args.Length > 2 && args[2] == "--lens-preview" ? LensOverlayTests.Run(args) : Regression.Run(args);

static class Regression
{
    static int assertions;
    static void Check(bool condition, string label) {
        if (!condition) throw new Exception("FAIL " + label);
        assertions++;
        Console.WriteLine("PASS " + label);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run(string[] args) {
        Console.WriteLine("GAME " + Path.GetFullPath(args[0]));
        var mod = Assembly.LoadFrom(Path.GetFullPath(args[1]));
        Type T(string name) => mod.GetType("spyglass.src." + name, true)!;
        object? Call(Type type, string name, params object?[] values) => AccessTools.Method(type, name).Invoke(null, values);
        void Set(Type type, string name, object? value) => AccessTools.Field(type, name).SetValue(null, value);
        object? Get(Type type, string name) => AccessTools.Field(type, name).GetValue(null);
        void Patch(string name, Harmony h) {
            var type = T("Client.Patches." + name);
            type.GetMethod("patch")!.Invoke(Activator.CreateInstance(type, true), new object[] {h});
        }
        var configType = T("SpyglassConfig");
        var config = Activator.CreateInstance(configType, true)!;
        void Config(string name, object value) => AccessTools.Field(configType, name).SetValue(config, value);
        var modType = T("SpyglassMod");
        var logic = T("ClientManipulation");
        Set(modType, "loadedConfig", config);
        Check(mod.GetName().Name == "spyglassfork", "fork assembly identity");
        Check((string)Get(modType, "ModId")! == "spyglassfork", "fork mod identity");
        Check(mod.GetName().Version == new Version(0, 6, 4, 0), "assembly release version is 0.6.4");
        Check(AccessTools.Method(modType, "FlickLensWater") == null
            && AccessTools.DeclaredMethod(modType, "OnMouseDown") == null
            && AccessTools.Method(logic, "CtrlHeld") == null, "manual wipe entry points and Ctrl mouse interception are removed");

        var surfaceModel = T("Client.LensRainModel");
        Check(surfaceModel.GetNestedType("Crystal", BindingFlags.NonPublic) == null
            && surfaceModel.GetNestedType("IceFacet", BindingFlags.NonPublic) == null, "released assembly has no frost geometry types");
        Check(surfaceModel.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .All(field => !field.Name.Contains("Frost", StringComparison.OrdinalIgnoreCase))
            && AccessTools.Field(T("Client.AtmosphericOpticsRenderer"), "frostConditions") == null,
            "released assembly has no frost simulation state or cold-weather trigger");

        var harmony = new Harmony("spyglassfork-regression");
        try {
            foreach (var name in new[] { "OnBeforeRenderFrame3D", "Set3DProjection", "ImmersiveFpSystemRenderEntities", "OnMouseMove", "GuiDialogHudPatch", "OptionalHudPatches" }) {
                Patch(name, harmony);
                Check(true, "patch installation " + name);
            }
            int Injected(MethodInfo method, string helper) => PatchProcessor.GetCurrentInstructions(method).Count(c => c.operand is MethodInfo m && m.DeclaringType?.Assembly == mod && m.Name == helper);
            Check(Injected(AccessTools.Method(typeof(PlayerCamera), "OnBeforeRenderFrame3D"), "AdjustCameraMode") == 1, "camera injection");
            Check(Injected(AccessTools.Method(typeof(ClientMain), "Set3DProjection", new[] {typeof(float), typeof(float)}), "AdjustFov") == 1, "projection injection");
            Check(Injected(AccessTools.Method(typeof(SystemRenderEntities), "OnRenderOpaque3D"), "LoadIsRender") == 2, "ordinary and batched entity injections");
            Check(Injected(AccessTools.Method(typeof(ClientMain), "OnMouseMove"), "AdjustSensitivity") == 2, "both mouse sensitivity injections");
            var blockMethod = AccessTools.DeclaredMethod(typeof(HudElementBlockAndEntityInfo), "ShouldReceiveRenderEvents");
            Check(Harmony.GetPatchInfo(blockMethod)?.Owners.Contains(harmony.Id) == true, "block HUD patched while initial style is square");

            Set(logic, "capi", null);
            var player = (EntityPlayer)RuntimeHelpers.GetUninitializedObject(typeof(EntityPlayer));
            Check(!(bool)Call(logic, "IsLocalPlayer", player)!, "missing client API is safe");
            Check(!(bool)Call(logic, "IsLocalPlayer", new object?[] {null})!, "null entity is not local");
            var api = Proxy.Make<ICoreClientAPI>();
            Set(logic, "capi", api);
            Check(!(bool)Call(logic, "IsLocalPlayer", player)!, "missing world is safe");
            Check(!(bool)Call(logic, "AttemptingToZoom")!, "missing inventory is safe");

            var inventory = new InventoryGeneric(1, "hotbar", "test", null);
            inventory[0].Itemstack = new ItemStack(new Item { Code = new AssetLocation("spyglassfork", "spyglass-copper") });
            var manager = Proxy.Make<IPlayerInventoryManager>((m, _) => m.Name switch {
                "get_ActiveHotbarSlotNumber" => 0,
                "GetHotbarInventory" => inventory,
                _ => Proxy.Default(m.ReturnType)
            });
            // Later 1.22 IClientPlayer versions have internal interface methods
            // that DispatchProxy cannot implement. Use the actual engine type.
            var clientPlayer = (ClientPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayer));
            var worldDataField = AccessTools.Field(typeof(ClientPlayer), "worlddata");
            var worldData = RuntimeHelpers.GetUninitializedObject(worldDataField.FieldType);
            AccessTools.Field(worldDataField.FieldType, "entityplayer").SetValue(worldData, player);
            worldDataField.SetValue(clientPlayer, worldData);
            ZoomState.Manager = manager;
            harmony.Patch(AccessTools.PropertyGetter(typeof(ClientPlayer), "InventoryManager"), prefix: new HarmonyMethod(typeof(ZoomState), nameof(ZoomState.Inventory)));
            var world = Proxy.Make<IClientWorldAccessor>((m, _) => m.Name == "get_Player" ? clientPlayer : Proxy.Default(m.ReturnType));
            api = Proxy.Make<ICoreClientAPI>((m, _) => m.Name == "get_World" ? world : Proxy.Default(m.ReturnType));
            Set(logic, "capi", api);
            Check((bool)Call(logic, "IsLocalPlayer", player)!, "ready local player recognized");
            Set(modType, "zoomed", true);
            Check((bool)Call(logic, "AttemptingToZoom")!, "fork item activates zoom");
            inventory[0].Itemstack!.Item.Code = new AssetLocation("spyglass", "spyglass-copper");
            Check(!(bool)Call(logic, "AttemptingToZoom")!, "upstream namespace does not activate fork");
            inventory[0].Itemstack!.Item.Code = new AssetLocation("spyglassfork", "spyglass-copper");
            Check((string)Call(logic, "GetHeldLensGlass")! == "plain", "existing spyglass defaults to plain glass");
            inventory[0].Itemstack!.Item.Code = new AssetLocation("spyglassfork", "spyglass-tinbronze-blue");
            Check((string)Call(logic, "GetHeldSpyglassMaterial")! == "tinbronze"
                && (string)Call(logic, "GetHeldLensGlass")! == "blue", "colored lens keeps metal and glass identities separate");
            Set(modType, "zoomed", true);
            Check((bool)Call(logic, "AttemptingToZoom")!, "colored spyglass activates the original zoom path");
            inventory[0].Itemstack!.Item.Code = new AssetLocation("spyglassfork", "spyglass-copper");

            var states = new Harmony("spyglassfork-regression-state");
            try {
                states.Patch(AccessTools.Method(logic, "getPercentZoomed"), prefix: new HarmonyMethod(typeof(ZoomState), nameof(ZoomState.Percent)));
                Set(logic, "isZoomed", true);
                Set(modType, "zoomed", true);
                Set(logic, "realCameraMode", EnumCameraMode.ThirdPerson);
                Config("enableZoomInThirdPerson", false);
                Set(logic, "realCameraMode", EnumCameraMode.FirstPerson);
                Check((EnumCameraMode)Call(logic, "HandleCameraMode", EnumCameraMode.ThirdPerson)! == EnumCameraMode.ThirdPerson, "first third-person frame stays in third person when zoom disabled");
                Check(!(bool)Call(logic, "EnableEffect")!, "first third-person frame records actual camera mode");
                var wheelType = T("Client.ZoomWheel");
                var wheel = (GuiDialog)RuntimeHelpers.GetUninitializedObject(wheelType);
                var entityPatch = T("Client.Patches.ImmersiveFpSystemRenderEntities");
                player.IsRendered = true;
                Check((float)Call(logic, "GetZoomAdjust")! == 1, "disabled third person preserves FOV");
                Check((double)Call(logic, "GetSensitivityMultiplier")! == 1, "disabled third person preserves sensitivity");
                Check(!(bool)Call(logic, "HideGuis")!, "disabled third person keeps HUD");
                Check(!(bool)wheelType.GetMethod("IsEnabled")!.Invoke(wheel, null)!, "disabled third person has no overlay");
                Check((bool)Call(entityPatch, "LoadIsRender", player)!, "disabled third person keeps local player visible");
                var scroll = new MouseWheelEventArgs {delta = 1};
                wheel.OnMouseWheel(scroll);
                Check(!scroll.IsHandled, "disabled third person does not consume wheel");
                Config("enableZoomInThirdPerson", true);
                Check((EnumCameraMode)Call(logic, "HandleCameraMode", EnumCameraMode.ThirdPerson)! == EnumCameraMode.FirstPerson, "enabled third-person zoom enters first person");
                Check((EnumCameraMode)Call(logic, "HandleCameraMode", EnumCameraMode.FirstPerson)! == EnumCameraMode.FirstPerson, "forced camera preserves original third-person mode");
                ZoomState.Value = 0;
                Check((EnumCameraMode)Call(logic, "HandleCameraMode", EnumCameraMode.FirstPerson)! == EnumCameraMode.ThirdPerson, "zoom ending restores original third-person mode");
                ZoomState.Value = 1;
                Check((bool)wheelType.GetMethod("IsEnabled")!.Invoke(wheel, null)!, "enabled zoom activates overlay");
                wheel.OnMouseWheel(scroll);
                Check(scroll.IsHandled, "enabled zoom consumes adjustment wheel");
                Check(!(bool)Call(entityPatch, "LoadIsRender", player)!, "enabled zoom hides local player");
                var hudPatch = T("Client.Patches.GuiDialogHudPatch");
                var blockHud = (GuiDialog)RuntimeHelpers.GetUninitializedObject(typeof(HudElementBlockAndEntityInfo));
                Check((bool)Call(hudPatch, "HideGui", blockHud)!, "square mask hides block info");
                Config("vignetteStyle", "none");
                Check(!(bool)Call(hudPatch, "HideGui", blockHud)!, "server square-to-none restores block info");
                Check(!blockHud.ShouldReceiveRenderEvents(), "actual block HUD method suppresses rendering after sync");
                Config("vignetteStyle", "square");
                Check((bool)Call(hudPatch, "HideGui", blockHud)!, "none-to-square restores mask policy");
                Config("hideHUDWhileSpying", false);
                var ordinaryHud = (GuiDialog)RuntimeHelpers.GetUninitializedObject(typeof(TestDialog));
                Check(!(bool)Call(hudPatch, "HideGui", ordinaryHud)!, "HUD config is evaluated dynamically");
                Config("hideHUDWhileSpying", true);
                Check((bool)Call(hudPatch, "HideGui", ordinaryHud)!, "ordinary HUD hides during zoom");
                Check(!(bool)Call(hudPatch, "HideGui", wheel)!, "overlay never hides itself");
                bool hudHidden = false;
                var wheelApi = Proxy.Make<ICoreClientAPI>((m, _) => m.Name == "get_HideGuis" ? hudHidden : Proxy.Default(m.ReturnType));
                AccessTools.Field(typeof(GuiDialog), "capi").SetValue(wheel, wheelApi);
                states.Patch(AccessTools.Method(wheelType, "RenderWithoutHud"), prefix: new HarmonyMethod(typeof(ZoomState), nameof(ZoomState.CaptureLensRender)));
                var lensRenderer = (IRenderer)wheel;
                ZoomState.LensRenders = 0;
                lensRenderer.OnRenderFrame(.016f, EnumRenderStage.Done);
                Check(ZoomState.LensRenders == 0, "normal HUD does not draw the lens twice");
                hudHidden = true;
                lensRenderer.OnRenderFrame(.016f, EnumRenderStage.Ortho);
                lensRenderer.OnRenderFrame(.016f, EnumRenderStage.Done);
                Check(ZoomState.LensRenders == 1, "F4 routes the zoomed lens through Done while Ortho is skipped");
                ZoomState.Value = 0;
                lensRenderer.OnRenderFrame(.016f, EnumRenderStage.Done);
                Check(ZoomState.LensRenders == 1, "F4 does not retain a mask after zoom ends");
                ZoomState.Value = 1;
                Config("enableZoomInThirdPerson", false);
                Set(logic, "realCameraMode", EnumCameraMode.ThirdPerson);
                lensRenderer.OnRenderFrame(.016f, EnumRenderStage.Done);
                Check(ZoomState.LensRenders == 1, "F4 respects disabled third-person zoom");
                Config("enableZoomInThirdPerson", true);
                wheelType.GetMethod("RequestRecompose")!.Invoke(wheel, null);
                Check((bool)AccessTools.Field(wheelType.BaseType, "recomposeRequested").GetValue(wheel)!, "synchronized overlay rebuild is requested");

                if (args.Length > 2) {
                    var scribe = Assembly.LoadFrom(Path.Combine(args[2], "Scribe.dll"));
                    var scribeType = scribe.GetType("Scribe.HudScribePins", true)!;
                    Patch("OptionalHudPatches", harmony);
                    var scribeHud = (GuiDialog)RuntimeHelpers.GetUninitializedObject(scribeType);
                    AccessTools.Field(typeof(GuiDialog), "opened").SetValue(scribeHud, true);
                    Check(!scribeHud.ShouldReceiveRenderEvents(), "actual Scribe HUD hides during zoom");
                    Config("hideHUDWhileSpying", false);
                    Check(scribeHud.ShouldReceiveRenderEvents(), "Scribe HUD retains original behavior when hide option disabled");
                    Config("hideHUDWhileSpying", true);
                    ZoomState.Value = 0;
                    Check(scribeHud.ShouldReceiveRenderEvents(), "Scribe HUD returns after zoom ends");
                    ZoomState.Value = 1;
                    var walk = Assembly.LoadFrom(Path.Combine(args[2], "WalkableEntities.dll"));
                    foreach (bool walkFirst in new[] {true, false}) {
                        harmony.Unpatch(AccessTools.Method(typeof(PlayerCamera), "OnBeforeRenderFrame3D"), HarmonyPatchType.All, harmony.Id);
                        Action installWalk = () => AccessTools.Method(walk.GetType("WalkableEntities.CameraAlignment", true), "Install").Invoke(null, new object[] {harmony});
                        if (walkFirst) installWalk();
                        Patch("OnBeforeRenderFrame3D", harmony);
                        if (!walkFirst) installWalk();
                        var codes = PatchProcessor.GetCurrentInstructions(AccessTools.Method(typeof(PlayerCamera), "OnBeforeRenderFrame3D"));
                        Check(codes.Any(c => c.operand is MethodInfo m && m.Name == "Align") && codes.Any(c => c.operand is MethodInfo m && m.Name == "AdjustCameraMode"), "Walkable joint camera patch, walkFirst=" + walkFirst);
                    }
                }
            } finally { states.UnpatchAll(states.Id); }

            Set(logic, "isZoomed", false);
            Set(modType, "gameRuntime", 0d);
            var tweenType = T("Client.UpdateableTween");
            var tween = Activator.CreateInstance(tweenType, new object[] {0f, 525f})!;
            var timeType = T("Client.ClientTime");
            AccessTools.Field(tweenType, "startTime").SetValue(tween, Call(timeType, "StartNew"));
            var tweenValue = (float)tweenType.GetMethod("getValue")!.Invoke(tween, new object[] {0f})!;
            Check(float.IsFinite(tweenValue) && tweenValue == 0, "same-tick zero-distance tween is finite");

            var errors = new List<string>();
            var logger = Proxy.Make<ILogger>((m, _) => { if (m.Name == "Error") errors.Add("patch error"); return Proxy.Default(m.ReturnType); });
            api = Proxy.Make<ICoreClientAPI>((m, _) => m.Name == "get_Logger" ? logger : Proxy.Default(m.ReturnType));
            Set(modType, "zoomed", true);
            Set(logic, "isZoomed", true);
            Set(logic, "resetCameraMode", true);
            var clientLogic = Activator.CreateInstance(logic, new object[] {api})!;
            Check(errors.Count == 0, "real client patcher starts without errors");
            Check(!(bool)Get(modType, "zoomed")! && !(bool)Get(logic, "isZoomed")! && !(bool)Get(logic, "resetCameraMode")!, "new session resets zoom and camera state");
            logic.GetMethod("Dispose")!.Invoke(clientLogic, null);
            Check(Get(logic, "capi") == null && Get(logic, "zoomAnimation") == null, "dispose clears client references");
            Check((float)Call(logic, "getPercentZoomed")! == 0, "late zoom query is safe after dispose");
            Check(!Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m)?.Owners.Contains("spyglassfork") == true), "dispose removes fork patches");
            var legacyConfig = Newtonsoft.Json.JsonConvert.DeserializeObject("{\"vignetteStyle\":\"square\",\"lensSize\":0.5}", configType)!;
            Check((bool)AccessTools.Field(configType, "enableAtmosphericOptics").GetValue(legacyConfig)!
                && (float)AccessTools.Field(configType, "weatherOpticsStrength").GetValue(legacyConfig)! == 1
                && (float)AccessTools.Field(configType, "sunGlareStrength").GetValue(legacyConfig)! == 1
                && (bool)AccessTools.Field(configType, "enableLensRain").GetValue(legacyConfig)!
                && (float)AccessTools.Field(configType, "lensRainStrength").GetValue(legacyConfig)! == 1,
                "legacy config retains atmospheric and lens rain defaults");
            Config("enableAtmosphericOptics", false);
            Config("weatherOpticsStrength", .4f);
            Config("sunGlareStrength", 1.6f);
            Config("enableLensRain", false);
            Config("lensRainStrength", 1.2f);
            var packetType = T("Network.ServerConfigSyncPacket");
            var packet = Activator.CreateInstance(packetType)!;
            AccessTools.Field(packetType, "vignetteStyle").SetValue(packet, Enum.Parse(T("VignetteStyle"), "none"));
            AccessTools.Field(packetType, "lensSize").SetValue(packet, .8f);
            var syncHandler = modType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).Single(m =>
                m.Name.StartsWith("<StartClientSide>") && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == packetType);
            syncHandler.Invoke(Activator.CreateInstance(modType, true), new[] { packet });
            Check((string)AccessTools.Field(configType, "vignetteStyle").GetValue(config)! == "none"
                && (float)AccessTools.Field(configType, "lensSize").GetValue(config)! == .8f, "real server handler still applies aperture settings");
            Check(!(bool)AccessTools.Field(configType, "enableAtmosphericOptics").GetValue(config)!
                && (float)AccessTools.Field(configType, "weatherOpticsStrength").GetValue(config)! == .4f
                && (float)AccessTools.Field(configType, "sunGlareStrength").GetValue(config)! == 1.6f
                && !(bool)AccessTools.Field(configType, "enableLensRain").GetValue(config)!
                && (float)AccessTools.Field(configType, "lensRainStrength").GetValue(config)! == 1.2f,
                "server aperture synchronization preserves client optics preferences");
            Check(packetType.GetFields().All(f => f.Name != "enableAtmosphericOptics" && f.Name != "weatherOpticsStrength" && f.Name != "sunGlareStrength"
                && f.Name != "enableLensRain" && f.Name != "lensRainStrength"),
                "atmospheric preferences excluded from network packet");

            var rendererType = T("Client.AtmosphericOpticsRenderer");
            var registered = new List<EnumRenderStage>();
            var unregistered = new List<EnumRenderStage>();
            Delegate? reload = null;
            int reloadAdds = 0, reloadRemoves = 0, compiles = 0, disposes = 0, warnings = 0;
            var events = Proxy.Make<IClientEventAPI>((m, values) => {
                if (m.Name == "RegisterRenderer") registered.Add((EnumRenderStage)values![1]!);
                if (m.Name == "UnregisterRenderer") unregistered.Add((EnumRenderStage)values![1]!);
                if (m.Name == "add_ReloadShader") { reload = (Delegate)values![0]!; reloadAdds++; }
                if (m.Name == "remove_ReloadShader") { if (Equals(reload, values![0])) reloadRemoves++; }
                return Proxy.Default(m.ReturnType);
            });
            var fragmentShader = Proxy.Make<IShader>((m, _) => m.Name == "get_Code" ? "/* VANILLA_RAIN_FUNCTIONS */" : Proxy.Default(m.ReturnType));
            var vanillaAsset = Proxy.Make<IAsset>((m, _) => m.Name == "ToText"
                ? File.ReadAllText(Path.Combine(args[0], "assets/game/shaders/chunkliquid.fsh")) : Proxy.Default(m.ReturnType));
            var assets = Proxy.Make<IAssetManager>((m, _) => m.Name == "TryGet" ? vanillaAsset : Proxy.Default(m.ReturnType));
            var failedShader = Proxy.Make<IShaderProgram>((m, _) => {
                if (m.Name == "get_FragmentShader") return fragmentShader;
                if (m.Name == "Compile") { compiles++; return false; }
                if (m.Name == "Dispose") disposes++;
                return Proxy.Default(m.ReturnType);
            });
            var shaders = Proxy.Make<IShaderAPI>((m, _) => m.Name == "NewShaderProgram" ? failedShader : Proxy.Default(m.ReturnType));
            logger = Proxy.Make<ILogger>((m, _) => { if (m.Name == "Warning") warnings++; return Proxy.Default(m.ReturnType); });
            api = Proxy.Make<ICoreClientAPI>((m, _) => m.Name switch {
                "get_Event" => events, "get_Shader" => shaders, "get_Logger" => logger, "get_Assets" => assets, _ => Proxy.Default(m.ReturnType)
            });
            var renderer = (IRenderer)Activator.CreateInstance(rendererType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] {api}, null)!;
            var surface = AccessTools.Field(rendererType, "rain").GetValue(renderer)!;
            var surfaceType = surface.GetType();
            AccessTools.Method(surfaceType, "AddDrop").Invoke(surface, new object[] {.5f, .5f, .015f});
            var usage = AccessTools.Method(rendererType, "UpdateUsage");
            usage.Invoke(renderer, new object[] {1000L, true});
            usage.Invoke(renderer, new object[] {30999L, false});
            Check((bool)AccessTools.PropertyGetter(surfaceType, "HasWater").Invoke(surface, null)!, "renderer keeps effects before the idle deadline");
            usage.Invoke(renderer, new object[] {31000L, false});
            Check(!(bool)AccessTools.PropertyGetter(surfaceType, "HasWater").Invoke(surface, null)!, "renderer expires idle surface without invoking any graphics API");
            Check(registered.SequenceEqual(new[] {EnumRenderStage.AfterOIT, EnumRenderStage.AfterBlit}) && reloadAdds == 1,
                "atmosphere registers depth validation, final rendering and shader reload");
            bool Resources() => (bool)AccessTools.Method(rendererType, "EnsureResources").Invoke(renderer, null)!;
            Check(!Resources() && !Resources() && compiles == 1 && warnings == 1, "shader failure logs once and suspends retries");
            float ratioBefore = (float)Call(modType, "getZoomRatio")!;
            renderer.OnRenderFrame(.016f, EnumRenderStage.AfterOIT);
            renderer.OnRenderFrame(.016f, EnumRenderStage.AfterBlit);
            Check((float)Call(modType, "getZoomRatio")! == ratioBefore, "shader failure preserves existing zoom state");
            Check((bool)reload!.DynamicInvoke()! && disposes == 1, "shader reload releases failed shader safely");
            Check(!Resources() && compiles == 2 && warnings == 2, "shader reload permits a fresh compilation attempt");
            renderer.Dispose(); renderer.Dispose();
            Check(unregistered.SequenceEqual(new[] {EnumRenderStage.AfterBlit, EnumRenderStage.AfterOIT})
                && reloadRemoves == 1 && disposes == 2, "atmosphere dispose unregisters all callbacks exactly once");
            renderer.OnRenderFrame(.016f, EnumRenderStage.AfterBlit);
            Check(compiles == 2 && warnings == 2, "late render after dispose is safe");
            assertions += LensRecipeTests.Run();
            Console.WriteLine("SUCCESS " + assertions + " assertions");
            return 0;
        } finally { harmony.UnpatchAll(harmony.Id); }
    }
}

public class Proxy : DispatchProxy
{
    System.Func<MethodInfo, object?[]?, object?> handler = (method, _) => Default(method.ReturnType);
    public static object? Default(Type type) => type == typeof(void) ? null : type.IsValueType ? Activator.CreateInstance(type) : null;
    public static T Make<T>(System.Func<MethodInfo, object?[]?, object?>? handler = null) where T : class {
        var result = Create<T, Proxy>();
        if (handler != null) ((Proxy)(object)result).handler = handler;
        return result;
    }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => handler(targetMethod!, args);
}

public static class ZoomState
{
    public static int LensRenders;
    public static bool CaptureLensRender() { LensRenders++; return false; }
    public static float Value = 1;
    public static IPlayerInventoryManager? Manager;
    public static bool Inventory(ref IPlayerInventoryManager __result) { __result = Manager!; return false; }
    public static bool Percent(ref float __result) { __result = Value; return false; }
}

class TestDialog : GuiDialog
{
    public TestDialog() : base(null) { }
    public override string ToggleKeyCombinationCode => "test";
}
