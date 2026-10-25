using System.Reflection;
using System.Runtime.InteropServices;
using Cairo;
using Path = System.IO.Path;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

// Render the production Cairo element with the installed game's real item
// textures. The API proxy only supplies assets; the drawing is not recreated.
internal static class LensOverlayTests
{
    public static int Run(string[] args)
    {
        RuntimeEnv.GUIScale = 1;
        var mod = Assembly.LoadFrom(Path.GetFullPath(args[1]));
        var type = mod.GetType("spyglass.src.Client.SpyglassOverlay", true)!;
        var style = Enum.Parse(mod.GetType("spyglass.src.VignetteStyle", true)!, "square");
        string output = Path.GetFullPath("artifacts/lens-preview");
        Directory.CreateDirectory(output);
        var logger = Proxy.Make<ILogger>();
        var render = Proxy.Make<IRenderAPI>((method, values) => method.Name == "BitmapCreateFromPng"
            ? new BitmapExternal((byte[])values![0]!, ((byte[])values[0]!).Length, logger) : Proxy.Default(method.ReturnType));
        IAsset Asset(AssetLocation location)
        {
            string path = Path.Combine(args[0], "assets/survival", location.Path);
            if (!File.Exists(path)) path = Path.Combine(args[0], "assets/game", location.Path);
            byte[] data = File.ReadAllBytes(path);
            return Proxy.Make<IAsset>((method, _) => method.Name == "get_Data" ? data : Proxy.Default(method.ReturnType));
        }
        var assets = Proxy.Make<IAssetManager>((method, values) => method.Name is "Get" or "TryGet"
            ? Asset((AssetLocation)values![0]!) : Proxy.Default(method.ReturnType));
        var api = Proxy.Make<ICoreClientAPI>((method, _) => method.Name switch {
            "get_Assets" => assets, "get_Render" => render, _ => Proxy.Default(method.ReturnType)
        });
        byte[] Draw(string material, float light, int width, int height, string? filename = null, string glass = "plain", float edgeOpacity = .5f)
        {
            using var surface = new ImageSurface(Format.Argb32, width, height);
            using var context = new Context(surface);
            var bounds = ElementBounds.Fixed(0, 0, width, height).WithParent(ElementBounds.Empty);
            using var overlay = (GuiElement)Activator.CreateInstance(type, api, bounds, 10, .5f, .94f, 1f, edgeOpacity, style, material, light, glass)!;
            overlay.ComposeElements(context, surface);
            surface.Flush();
            if (filename != null) surface.WriteToPng(Path.Combine(output, filename));
            var pixels = new byte[surface.Stride * surface.Height];
            Marshal.Copy(surface.DataPtr, pixels, 0, pixels.Length);
            return pixels;
        }
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("FAIL " + message);
            checks++;
            Console.WriteLine("PASS " + message);
        }
        foreach (var material in new[] { "copper", "tinbronze", "bismuth", "bismuthbronze", "blackbronze", "gold", "silver", "iron", "meteoriciron", "brass", "steel" })
        {
            const int width = 1280, height = 720;
            var pixels = Draw(material, 1, width, height, material + ".png");
            Check(pixels[3] == 255, material + " has an opaque outer mask");
            int transparent = 0;
            for (int y = 210; y < 510; y++)
                for (int x = 490; x < 790; x++)
                    if (pixels[(y * width + x) * 4 + 3] < 100) transparent++;
            Check(transparent == 90000, material + " preserves vanilla plain glass transparency");
            Check(pixels[(360 * width + 475) * 4 + 3] == 255, material + " metal inside the lens is completely opaque");
            Check(pixels.SequenceEqual(Draw(material, 1, width, height)), material + " vanilla glass is stable after recomposition");
            Check(pixels.SequenceEqual(Draw(material, 1, width, height, edgeOpacity: 0)), material + " legacy edgeOpacity cannot make the metal transparent");
        }
        foreach (var (width, height) in new[] { (1920, 1080), (720, 1280), (80, 80), (1044, 1044) })
        {
            var pixels = Draw("copper", 1, width, height, $"size-{width}x{height}.png");
            Check(pixels[3] == 255, $"mask renders at {width}x{height}");
        }
        var lit = Draw("copper", 1, 1280, 720);
        var dark = Draw("copper", .2f, 1280, 720, "copper-dark.png");
        Check(lit.Where((_, i) => i % 4 != 3).Select(b => (long)b).Sum() > dark.Where((_, i) => i % 4 != 3).Select(b => (long)b).Sum(), "metal and glass respond to ambient lighting");
        var plain = Draw("copper", 1, 1280, 720);
        foreach (string glass in new[] { "quartz", "green", "blue", "violet", "red", "yellow", "brown", "vintage", "smoky", "pink" })
        {
            var colored = Draw("copper", 1, 1280, 720, "glass-" + glass + ".png", glass);
            Check(!colored.SequenceEqual(plain), glass + " lens uses distinct vanilla glass artwork and tint");
            Check(colored[(360 * 1280 + 475) * 4 + 3] == 255, glass + " glass does not change opaque metal");
            byte[] originalData = File.ReadAllBytes(Path.Combine(args[0], "assets/survival/textures/block/glass/" + glass + ".png"));
            using var original = new BitmapExternal(originalData, originalData.Length, logger);
            int[] raw = original.Pixels;
            // Aperture: x=465,y=185,size=350. Compare interior pixels to the
            // original PNG's exact straight-alpha color after premultiplication.
            bool matches = true;
            foreach (int px in new[] { 510, 580, 670, 750 })
                foreach (int py in new[] { 230, 300, 390, 470 })
                {
                    int tx = Math.Clamp((int)((px + .5 - 465) * original.Width / 350), 0, original.Width - 1);
                    int ty = Math.Clamp((int)((py + .5 - 185) * original.Height / 350), 0, original.Height - 1);
                    uint expected = (uint)raw[ty * original.Width + tx];
                    int alpha = (int)(expected >> 24), offset = (py * 1280 + px) * 4;
                    matches &= colored[offset + 3] == alpha;
                    for (int c = 0; c < 3; c++)
                        matches &= Math.Abs(colored[offset + c] - (((expected >> (c * 8)) & 255) * alpha + 127) / 255) <= 1;
                }
            Check(matches, glass + " lens preserves original glass hue and alpha without color overflow");
        }
        Console.WriteLine($"SUCCESS {checks} Cairo assertions; production overlays: {output}");
        return 0;
    }
}
