using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using DeepSpaceQoL;

string managed = args.Length == 1 ? Path.GetFullPath(args[0]) : @"D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed";
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string path = Path.Combine(managed, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
BerryTests.Run();

static class BerryTests
{
    private static int checks;
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException(message); }

    private static GameItem Berry(int? condition, int? variant)
    {
        var item = (GameItemElement)RuntimeHelpers.GetUninitializedObject(typeof(GameItemElement));
        item.identifier = "wine_berry"; item.state = new TagSystem(); item.modifiedState = item.state;
        Set(item, "BERRY_TAG", 0);
        if (condition.HasValue) Set(item, "BERRY_CONDITION_INT", condition.Value);
        if (variant.HasValue) Set(item, "BERRY_VISUAL_VARIANT_INT", variant.Value);
        return item;
    }
    private static void Set(GameItem item, string tag, int value) =>
        item.state.dict[tag] = new TagState(tag, tag) { valueEnabled = true, valueInt = value };

    internal static void Run()
    {
        Check(!NativeBerryQuality.TryDescribe(null, out _), "null is not a berry");
        foreach (int condition in new[] { -2, -1, 1, 2 })
            foreach (int variant in new[] { 0, 1, 2 })
            {
                var item = Berry(condition, variant);
                var before = item.state.dict.ToDictionary(p => p.Key, p => (p.Value.valueEnabled, p.Value.valueInt));
                Check(NativeBerryQuality.TryDescribe(item, out var quality), "valid berry accepted");
                Check(quality.Condition == condition && quality.VisualVariant == variant && quality.IsKnown, "actual native tag values preserved");
                Check(quality.WineContribution == (condition < 0 ? condition + 1 : condition), "matches WineHelper.StartFermentation contribution");
                string expected = condition switch { -2 => "霉变", -1 => variant == 2 ? "过熟" : "欠熟", 1 => "正常", _ => "优质小果" };
                Check(quality.Name == expected && quality.Badge.Length == 1, "label follows native InsBerryUI condition/variant");
                Check(quality.Description.Contains(expected) && quality.Description.Contains("酿造贡献"), "tooltip names quality and gameplay contribution");
                Check(item.state.dict.Count == before.Count && before.All(p => item.state.dict.TryGetValue(p.Key, out var tag) &&
                    (tag.valueEnabled, tag.valueInt) == p.Value), "read-only existing state unchanged");
            }
        foreach (int unknown in new[] { int.MinValue, -3, 0, 3, int.MaxValue })
        {
            var item = Berry(unknown, null);
            int count = item.state.dict.Count;
            Check(NativeBerryQuality.TryDescribe(item, out var quality) && quality.Condition == unknown && !quality.IsKnown, "unknown native tier is explicit, not guessed");
            Check(quality.WineContribution == null && quality.Badge == "?", "unknown does not invent a fermentation effect");
            Check(item.state.dict.Count == count && !item.state.dict.ContainsKey("BERRY_VISUAL_VARIANT_INT"), "missing variant not initialized by display");
        }
        var missing = Berry(null, null);
        Check(NativeBerryQuality.TryDescribe(missing, out var missingQuality) && !missingQuality.IsKnown && missingQuality.Condition == null, "missing tier explicitly unknown");
        Check(missing.state.dict.Count == 1 && missingQuality.Description.Contains("数据缺失"), "missing tier remains absent");
        var disabled = Berry(2, 0);
        disabled.state.dict["BERRY_CONDITION_INT"].valueEnabled = false;
        Check(NativeBerryQuality.TryDescribe(disabled, out var disabledQuality) && !disabledQuality.IsKnown, "disabled quality not silently consumed");
        disabled.state.dict["BERRY_TAG"].valueEnabled = false;
        Check(!NativeBerryQuality.TryDescribe(disabled, out _), "disabled berry tag is not a berry");
        var nonBerry = Berry(2, 0);
        nonBerry.state.dict.Remove("BERRY_TAG");
        Check(!NativeBerryQuality.TryDescribe(nonBerry, out _) && !nonBerry.state.dict.ContainsKey("BERRY_TAG"), "identifier alone is insufficient; read cannot add tags");
        // Native modified state, not saved base state, is what InsBerryUI reads.
        var modified = Berry(1, 0);
        modified.modifiedState = modified.state.Clone();
        modified.modifiedState.dict["BERRY_CONDITION_INT"] = new TagState("BERRY_CONDITION_INT", "BERRY_CONDITION_INT") { valueEnabled = true, valueInt = 2 };
        Check(NativeBerryQuality.TryDescribe(modified, out var changed) && changed.Condition == 2, "uses currently effective quality like native inspection");
        Check(modified.state.dict["BERRY_CONDITION_INT"].valueInt == 1, "saved original quality not rewritten");
        Console.WriteLine($"BerryQuality native checks passed: {checks}; no Unity UI or live patches executed.");
    }
}

namespace DeepSpaceQoL
{
    // Test-only mod lifecycle/log surface. Native game data types and production descriptor are real.
    internal static class ModEntry
    {
        internal const string Id = "deep_space_qol";
        internal static bool Enabled => false;
    }
}
