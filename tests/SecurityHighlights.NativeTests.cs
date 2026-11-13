using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using DeepSpaceQoL;

string managed = args.Length == 1 ? Path.GetFullPath(args[0]) : @"D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed";
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string path = Path.Combine(managed, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
SecurityHighlightTests.Run();

static class SecurityHighlightTests
{
    private static int checks;
    private static void Check(bool condition, string why)
    { checks++; if (!condition) throw new InvalidOperationException(why); }

    private static T Native<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static void Set(object target, string name, object? value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                ?? type.GetField("<" + name + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null) continue;
            field.SetValue(target, value);
            return;
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    private static GameItem Item(params string[] tags)
    {
        var item = Native<GameItemElement>();
        item.identifier = "data_only_test";
        item.unitCount = 1;
        item.itemFeatures = new();
        item.itemTypes = new();
        item.state = new TagSystem();
        item.modifiedState = item.state;
        foreach (var tag in tags) item.state.dict[tag] = new TagState(tag, tag) { valueEnabled = true };
        return item;
    }

    private static void Level(GameItem item, string level) =>
        item.modifiedState.dict["CONTRABAND_LEVEL"] = new TagState("CONTRABAND_LEVEL", "CONTRABAND_LEVEL")
            { valueEnabled = true, valueString = level };

    private static void PutInside(GameItem owner, params GameItem[] children)
    {
        var inventory = Native<GameGridInventory>();
        Set(inventory, "items", children.ToList());
        var window = Native<PixelWindow>();
        Set(window, "childElement", inventory);
        Set(window, "parentItems", new List<GameItem> { owner });
        Set(inventory, "parent", window);
        Set(owner, "contentWindow", window);
        foreach (var item in children) item.parentInventory = inventory;
    }

    internal static void Run()
    {
        Check(!SecurityHighlightPolicy.IsInspectionClient(null), "No visitor must clear cues.");
        foreach (var intent in Enum.GetValues<StoreClient.ClientIntent>())
        {
            var client = Native<StoreClient>();
            client.clientIntent = intent;
            client.isSecurity = true;
            Check(SecurityHighlightPolicy.IsInspectionClient(client) == (intent == StoreClient.ClientIntent.INSPECTION),
                "Only INSPECTION intent may activate cues; security buyers must not: " + intent);
            client.isSecurity = false;
            Check(SecurityHighlightPolicy.IsInspectionClient(client) == (intent == StoreClient.ClientIntent.INSPECTION),
                "An inspection intent remains an inspection regardless of faction alias: " + intent);
        }

        Check(SecurityHighlightPolicy.DirectRisk(null) == SecurityHighlightPolicy.RiskKind.None, "Null item must be safe.");
        Check(SecurityHighlightPolicy.Evaluate(null).All == SecurityHighlightPolicy.RiskKind.None, "Null graph must be safe.");
        var plain = Item();
        Check(SecurityHighlightPolicy.Evaluate(plain).All == SecurityHighlightPolicy.RiskKind.None, "Ordinary goods have no cue.");
        Check(plain.modifiedState.dict.Count == 0, "Scanning ordinary goods must not create any tag.");
        var missingLevel = Item("STOLEN_TAG");
        Check(SecurityHighlightPolicy.DirectRisk(missingLevel) == SecurityHighlightPolicy.RiskKind.Stolen, "Missing contraband level must not hide stolen goods.");
        Check(missingLevel.modifiedState.dict.Count == 1, "Read-only cue must not add absent CONTRABAND_LEVEL.");

        var levels = new[] { "", "CONTRABAND_LEVEL_NONE", "CONTRABAND_LEVEL_LOW", "CONTRABAND_LEVEL_MID", "CONTRABAND_LEVEL_HIGH", "CONTRABAND_LEVEL_CRITICAL" };
        for (int flags = 0; flags < 32; flags++) foreach (var level in levels)
        {
            var tags = new List<string>();
            if ((flags & 1) != 0) tags.Add("CONTRABAND_ITEM_TAG");
            if ((flags & 2) != 0) tags.Add("STOLEN_TAG");
            if ((flags & 4) != 0) tags.Add("ITEM_HIDDEN_TAG");
            if ((flags & 8) != 0) tags.Add("LOCKED_TAG");
            if ((flags & 16) != 0) tags.Add("ITEM_BEHIND_LOCK_TAG");
            var item = Item(tags.ToArray());
            Level(item, level);
            var before = item.modifiedState.dict.ToDictionary(p => p.Key, p => (p.Value.valueEnabled, p.Value.valueString, p.Value.valueInt));
            var risk = SecurityHighlightPolicy.Evaluate(item);
            Check((risk.All != SecurityHighlightPolicy.RiskKind.None) == ContrabandHelper.IsFlaggableItem(item),
                "Cue matches native flaggable predicate for flags=" + flags + " level=" + level);
            Check(risk.Contained == SecurityHighlightPolicy.RiskKind.None, "A leaf has no contained risk.");
            Check(before.Count == item.modifiedState.dict.Count && before.All(p =>
                item.modifiedState.dict.TryGetValue(p.Key, out var state) && p.Value == (state.valueEnabled, state.valueString, state.valueInt)),
                "Cue must not change existing tags, concealment, lock or heat.");
        }

        var stolen = Item("STOLEN_TAG");
        var illegal = Item("CONTRABAND_ITEM_TAG"); Level(illegal, "CONTRABAND_LEVEL_HIGH");
        var nested = Item(); PutInside(nested, stolen);
        var box = Item(); PutInside(box, nested, illegal);
        var result = SecurityHighlightPolicy.Evaluate(box);
        Check(result.Direct == SecurityHighlightPolicy.RiskKind.None, "Ordinary box itself is not contraband.");
        Check(result.Contained == (SecurityHighlightPolicy.RiskKind.Contraband | SecurityHighlightPolicy.RiskKind.Stolen),
            "Closed container cue must include nested contraband and stolen goods.");
        Check(SecurityHighlightPolicy.Describe(result) == "搜查提示：内部含违禁品及赃物。", "Contained risk wording must identify its location.");
        Check(ReferenceEquals(stolen.parentInventory?.parents[0], nested.contentWindow), "Cue must not move a nested item.");
        Check(box.state.dict.Count == 0 && nested.state.dict.Count == 0 && stolen.state.dict.Count == 1,
            "Nested traversal must preserve all tag dictionaries.");
        box.state.dict["STOLEN_TAG"] = new TagState("STOLEN_TAG", "STOLEN_TAG") { valueEnabled = true };
        result = SecurityHighlightPolicy.Evaluate(box);
        Check(result.Direct == SecurityHighlightPolicy.RiskKind.Stolen, "A stolen container retains its own risk.");
        Check(SecurityHighlightPolicy.Describe(result).Contains("赃物；内部含"), "Direct and contained risks must remain distinguishable.");

        // A malformed graph must not stall the UI loop or overflow the stack.
        PutInside(stolen, box);
        Check(SecurityHighlightPolicy.Evaluate(box).All == (SecurityHighlightPolicy.RiskKind.Contraband | SecurityHighlightPolicy.RiskKind.Stolen),
            "Visited-node traversal terminates safely even with cyclic data.");
        Check(SecurityHighlightPolicy.Describe(new SecurityHighlightPolicy.RiskInfo(0, 0)) == "", "No risk means no tooltip addition.");
        Console.WriteLine($"SecurityHighlights.NativeTests: {checks} checks passed; native data predicates only, no Unity UI or inspection actions executed.");
    }
}

namespace DeepSpaceQoL
{
    internal static class ModEntry { internal static bool Enabled = true; }
}
