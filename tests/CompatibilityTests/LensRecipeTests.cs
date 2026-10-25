using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

internal static class LensRecipeTests
{
    internal static int Run()
    {
        string root = Path.GetFullPath("spyglass/assets/spyglassfork");
        var baseItem = JObject.Parse(File.ReadAllText(Path.Combine(root, "itemtypes/spyglass.json")));
        var coloredItem = JObject.Parse(File.ReadAllText(Path.Combine(root, "itemtypes/spyglass-glass.json")));
        var metals = baseItem["variantgroups"]![0]!["states"]!.Values<string>().Select(s => s!).ToArray();
        var glasses = coloredItem["variantgroups"]![1]!["states"]!.Values<string>().Select(s => s!).Prepend("plain").ToArray();
        var items = new List<Item>(); var blocks = new List<Block>();
        foreach (string metal in metals)
        {
            items.Add(new Item { ItemId = items.Count + 1, Code = new AssetLocation("spyglassfork", "spytube-" + metal) });
            foreach (string glass in glasses)
                items.Add(new Item { ItemId = items.Count + 1, Code = new AssetLocation("spyglassfork", "spyglass-" + metal + (glass == "plain" ? "" : "-" + glass)) });
        }
        foreach (string glass in glasses)
            blocks.Add(new Block { BlockId = blocks.Count + 1, Code = new AssetLocation("game", "glass-" + glass) });
        var fastSearch = new System.Collections.Generic.OrderedDictionary<IRecipeIngredientBase, List<IRecipeBase>>();
        var world = Proxy.Make<IWorldAccessor>((m, values) => m.Name switch {
            "get_Items" => items, "get_Blocks" => blocks, "get_Logger" => Proxy.Make<ILogger>(),
            "get_FastSearchRecipesByIngredient" => fastSearch,
            "GetItem" => items.FirstOrDefault(i => Equals(i.Code, values![0])),
            "GetBlock" => blocks.FirstOrDefault(b => Equals(b.Code, values![0])),
            _ => Proxy.Default(m.ReturnType)
        });
        var api = Proxy.Make<ICoreAPI>((m, _) => m.Name == "get_World" ? world : Proxy.Default(m.ReturnType));
        foreach (var collectible in items.Cast<CollectibleObject>().Concat(blocks))
            HarmonyLib.AccessTools.Field(typeof(CollectibleObject), "api").SetValue(collectible, api);
        int checks = 0, count = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL " + message); checks++; }
        foreach (string name in new[] { "spyglass.json", "spyglass-glass.json" })
        {
            // Same domain-aware JSON conversion and wildcard expansion as RecipeLoader.
            var recipe = JObject.Parse(File.ReadAllText(Path.Combine(root, "recipes/grid", name))).ToObject<GridRecipe>("spyglassfork")!;
            foreach (var generated in recipe.GenerateRecipesForAllIngredientCombinations(world).Cast<GridRecipe>())
            {
                Check(generated.Resolve(world, "lens recipe regression"), "glass recipe resolves against collectible registry");
                string glass = generated.Ingredients!["G"].Code!.Path.Substring("glass-".Length);
                string metal = generated.Ingredients["T"].Code!.Path.Substring("spytube-".Length);
                string expected = "spyglass-" + metal + (glass == "plain" ? "" : "-" + glass);
                Check(generated.Output!.Code!.Path == expected && generated.Output.Code.Domain == "spyglassfork", "glass ingredient determines matching output variant");
                var stack = new ItemStack(blocks.Single(b => b.Code.Path == "glass-" + glass));
                Check(generated.ResolvedIngredients![1]!.SatisfiesAsIngredient(stack), "intended glass satisfies recipe");
                var wrong = new ItemStack(blocks.First(b => b.Code.Path != "glass-" + glass));
                Check(!generated.ResolvedIngredients[1]!.SatisfiesAsIngredient(wrong), "other glass cannot craft this lens color");
                if (glass != "plain")
                {
                    string texture = coloredItem["texturesByType"]!["*"]!["glass"]!["base"]!.Value<string>()!.Replace("{glass}", glass);
                    Check(texture == "game:block/glass/" + glass, "held model uses matching original glass texture");
                    Check(coloredItem["durabilitybytype"]!["spyglass-" + metal + "-*" ] != null, "colored variant keeps metal durability");
                }
                count++;
            }
        }
        Check(count == metals.Length * glasses.Length, "all metal and glass recipe combinations exist");
        Console.WriteLine($"PASS {count} actual game recipe expansions and {checks} crafting/asset assertions");
        return checks;
    }
}
