using System;
using Vintagestory.API.Common;

namespace spyglass.src.Client
{
    internal static class LensGlass
    {
        internal static readonly string[] Types = { "plain", "quartz", "green", "blue", "violet", "red", "yellow", "brown", "vintage", "smoky", "pink" };

        internal static string FromCode(string path)
        {
            if (string.IsNullOrEmpty(path)) return "plain";
            string suffix = path.Substring(path.LastIndexOf('-') + 1);
            return Array.IndexOf(Types, suffix) >= 0 ? suffix : "plain";
        }

        internal static string MetalFromCode(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("spyglass-", StringComparison.Ordinal)) return "copper";
            string material = path.Substring("spyglass-".Length);
            int separator = material.IndexOf('-');
            return separator < 0 ? material : material.Substring(0, separator);
        }

        internal static AssetLocation Texture(string glass)
            => new AssetLocation("game", "textures/block/glass/" + (Array.IndexOf(Types, glass) >= 0 ? glass : "plain") + ".png");
    }
}
