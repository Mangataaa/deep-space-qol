using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using DeepSpaceQoL;

string managed = args.Length == 1 ? Path.GetFullPath(args[0]) : @"D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed";
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string path = Path.Combine(managed, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
AppraisalNativeTests.Run();

static class AppraisalNativeTests
{
    private static int checks;

    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException(message); }

    private static GameItem Item(string id, params string[] tags)
    {
        var item = (GameItemElement)RuntimeHelpers.GetUninitializedObject(typeof(GameItemElement));
        item.identifier = id;
        item.unitValue = 999999;
        item.unitCount = 1;
        item.itemTypes = new();
        item.itemFeatures = new();
        item.state = new TagSystem();
        item.modifiedState = item.state;
        foreach (string tag in tags)
            item.state.dict[tag] = new TagState(tag, tag) { valueEnabled = true };
        SetInt(item, "APPRAISAL_DEFAULT_VALUE", 7);
        SetInt(item, "APPRAISAL_CLIENT_VALUE", 19);
        return item;
    }

    private static void SetString(GameItem item, string key, string value) =>
        item.state.dict[key] = new TagState(key, key) { valueEnabled = true, valueString = value };
    private static void SetInt(GameItem item, string key, int value) =>
        item.state.dict[key] = new TagState(key, key) { valueEnabled = true, valueInt = value };

    private static void Price(GameItem item, int expected, string label)
    {
        var snapshot = item.state.dict.ToDictionary(p => p.Key, p => (p.Value.valueEnabled, p.Value.valueString, p.Value.valueInt));
        Check(AppraisalValuePolicy.TryGetPrice(item, out int value), label + ": must produce a registered price");
        Check(value == expected, label + $": expected {expected}, got {value}");
        Check(item.unitValue == 999999 && item.GetTagReadonly("APPRAISAL_DEFAULT_VALUE").GetInt() == 7 &&
            item.GetTagReadonly("APPRAISAL_CLIENT_VALUE").GetInt() == 19, label + ": valuation must not rewrite proposed/client/base prices");
        Check(item.state.dict.Count == snapshot.Count && item.state.dict.All(p => snapshot.TryGetValue(p.Key, out var old) &&
            old == (p.Value.valueEnabled, p.Value.valueString, p.Value.valueInt)), label + ": valuation must not change item state");
    }

    private static GameItem Hand()
    {
        var item = Item("hand", "APPRAISAL_ITEM", "HAND_TAG");
        SetString(item, "HAND_MATERIAL", "STEEL");
        SetString(item, "HAND_INTERFACE", "EMG");
        SetString(item, "HAND_CONDITION", "NONE");
        SetString(item, "HAND_ADDON_ARMORED", "NONE");
        SetString(item, "HAND_ADDON_BLADED", "NONE");
        return item;
    }

    private static GameItem Plasma()
    {
        var item = Item("plasma_fuel", "APPRAISAL_ITEM", "PLASMA_CELL_TAG");
        foreach (string tag in new[] { "PLASMA_CASING", "PLASMA_CASING_CONDITION_WEAR", "PLASMA_CASING_CONDITION_CRACK",
            "PLASMA_CASING_CONDITION_UNCOVERED", "PLASMA_CASING_CONDITION_REINFORCED", "PLASMA_SEAL" }) SetString(item, tag, "NONE");
        SetString(item, "PLASMA_COLOR", "STANDARD");
        return item;
    }

    private static GameItem Ribwich()
    {
        var item = Item("ribwich", "APPRAISAL_ITEM", "RIBWICH_TAG");
        foreach (string tag in new[] { "RIBWICH_MEAT_TAG", "RIBWICH_PICKLES_TAG", "RIBWICH_WRAP_TAG", "RIBWICH_BUN_TAG",
            "RIBWICH_DAVRE_LOGO_TAG", "RIBWICH_RIBWICH_LOGO_TAG", "RIBWICH_PRODUCTION_TAG", "RIBWICH_FACILITY_TAG" }) SetInt(item, tag, 0);
        SetInt(item, "RIBWICH_PRODUCTION_MONTH_TAG", 6);
        SetInt(item, "RIBWICH_PRODUCTION_YEAR_TAG", 435);
        SetInt(item, "RIBWICH_FACILITY_NUMBER_TAG", 10);
        return item;
    }

    internal static void Run()
    {
        Check(!AppraisalValuePolicy.TryGetPrice(null, out _), "No item must never produce a price");
        Check(!AppraisalValuePolicy.TryGetPrice(Item("hand", "HAND_TAG"), out _), "Inspectable but non-appraisal item must be rejected");
        Check(!AppraisalValuePolicy.TryGetPrice(Item("unknown", "APPRAISAL_ITEM"), out _), "Unknown registered type must not fall back to unitValue");
        Check(!AppraisalValuePolicy.TryGetPrice(Item("ins_berry", "BERRY_TAG"), out _), "Berry inspection has no native appraisal price input");

        var hand = Hand();
        Price(hand, 100, "Base hand");
        SetString(hand, "HAND_MATERIAL", "GOLD_PLATED"); SetString(hand, "HAND_INTERFACE", "NEURAL");
        Price(hand, 620, "Deluxe hand must include the set bonus");
        SetString(hand, "HAND_MATERIAL", "CARBON_COMPOSITE"); SetString(hand, "HAND_INTERFACE", "EMG_PLUS");
        SetString(hand, "HAND_ADDON_ARMORED", "ARMORED"); SetString(hand, "HAND_ADDON_BLADED", "BLADED");
        Price(hand, 425, "Tactical hand must include both add-ons and set bonus");
        hand = Hand(); SetString(hand, "HAND_CONDITION", "DAMAGE"); SetString(hand, "HAND_INTERFACE", "UNSPECIFIED");
        Price(hand, 50, "Damaged hand native minimum");

        var fuel = Plasma();
        Price(fuel, 300, "Standard fuel");
        SetString(fuel, "PLASMA_COLOR", "HIGH_QUALITY"); SetString(fuel, "PLASMA_SEAL", "IGNITION_LAB");
        Price(fuel, 1200, "Premium fuel with matching lab seal");
        SetString(fuel, "PLASMA_CASING_CONDITION_REINFORCED", "REINFORCED");
        Price(fuel, 510, "Reinforcement invalidates the lab set bonus");
        fuel = Plasma(); SetString(fuel, "PLASMA_COLOR", "POOR1");
        SetString(fuel, "PLASMA_CASING_CONDITION_WEAR", "WEAR_HEAVY"); SetString(fuel, "PLASMA_CASING_CONDITION_CRACK", "CRACK");
        Price(fuel, 90, "Poor damaged fuel native minimum");

        var rib = Ribwich();
        int facilityBonus = RibwichHelper.GetFacilityBonus(10);
        Price(rib, 150 + facilityBonus, "Authentic ribwich");
        SetInt(rib, "RIBWICH_MEAT_TAG", 1); SetInt(rib, "RIBWICH_PICKLES_TAG", 1); SetInt(rib, "RIBWICH_BUN_TAG", 1);
        Price(rib, 1250 + facilityBonus, "Double-meat deluxe ribwich");
        SetInt(rib, "RIBWICH_WRAP_TAG", 1);
        Price(rib, 40, "Opened ribwich must use native reduced value");
        rib = Ribwich(); SetInt(rib, "RIBWICH_PRODUCTION_MONTH_TAG", 14);
        Price(rib, 40, "Counterfeit ribwich must use native reduced value");

        var addon = Item("qol_test_appraisal", "APPRAISAL_ITEM");
        AppraisalItemRegistry.Register(new AppraisalItemType
        {
            id = "deep_space_qol_offline_test", itemId = addon.identifier, source = "deep_space_qol_offline_test",
            createRandom = () => throw new InvalidOperationException("Valuation must not create items"),
            getCorrectValue = _ => 733
        });
        try
        {
            Price(addon, 733, "Mod API registered appraisal type");
            var registered = AppraisalItemRegistry.Get("deep_space_qol_offline_test");
            registered.getCorrectValue = _ => 0;
            Check(!AppraisalValuePolicy.TryGetPrice(addon, out _), "Missing/zero value must leave user input unchanged");
            registered.getCorrectValue = _ => -50;
            Check(!AppraisalValuePolicy.TryGetPrice(addon, out _), "Invalid negative value must leave user input unchanged");
        }
        finally { AppraisalItemRegistry.RemoveAll("deep_space_qol_offline_test"); }
        Console.WriteLine($"Appraisal native tests passed: {checks} checks; no UI/scene initialization or appraisal submission.");
    }
}
