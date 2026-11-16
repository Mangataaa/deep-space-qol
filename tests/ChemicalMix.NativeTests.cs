using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using DeepSpaceQoL;
using HarmonyLib;

string managed = args.Length == 1 ? Path.GetFullPath(args[0]) : @"D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed";
AssemblyLoadContext.Default.Resolving += (_, n) => File.Exists(Path.Combine(managed, n.Name + ".dll")) ?
    AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed, n.Name + ".dll")) : null;
Tests.Run();

static class Tests
{
    private static int checks;
    private const BindingFlags All = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
    private static readonly Assembly Production = typeof(ModEntry).Assembly;
    private static void Check(bool value, string why) { checks++; if (!value) throw new Exception(why); }
    private static bool Localize(string table, string key, object[] arguments, ref string __result) { __result = key; return false; }
    private static bool SyncOnly(GameItem target) { target.SyncModifiedState(); return false; }
    private static void FailPour(GameItem target, GameItem source)
    {
        target.state.GetTag("TEST_PARTIAL_CALLBACK").Enable().SetInt(999);
        source.unitValue = 777;
        throw new InvalidOperationException("injected native callback failure");
    }
    private static int[] Parts(params (string id, int amount)[] values)
    {
        var parts = new int[Liquid.Liquids.Count]; foreach (var p in values) parts[ChemicalMixModel.Index(p.id)] = p.amount; return parts;
    }
    private static int Part(int[] parts, string id) => ChemicalMixModel.Part(parts, id);
    private static ChemicalReagent Reagent(string kind, params (string id, int amount)[] parts) => new()
    { Kind = kind, Parts = Parts(parts), Temperature = 26, Name = kind };
    private static ChemicalMixPlan Search(int[] parts, bool finishing, int capacity, params ChemicalReagent[] reagents)
    {
        var plan = new ChemicalMixPlan(); foreach (int _ in ChemicalMixPlanner.Search(plan, parts, 26, capacity, "caffeine", finishing, reagents)) { }
        return plan;
    }
    private static DataItem Item(int[] parts, int capacity = 2000000)
    {
        var item = new DataItem();
        item.state.GetTag(WaterHelper.LIQUID_CONTAINER_TAG).Enable();
        item.state.GetTag(WaterHelper.LIQUID_CONTAINER_CAPACITY).Enable().SetInt(capacity);
        item.state.GetTag(ChemicalHelper.CONTAINER_TEMP_TAG).Enable().SetDouble(26);
        item.state.GetTag("IS_OWNED_TAG").Enable();
        item.itemFeatures.Add(new ItemFeature("CATEGORY_WATER_PURITY") { identifier = "CATEGORY_WATER_PURITY" });
        for (int i = 0; i < parts.Length; i++) item.state.GetTag(Liquid.Liquids[i].currentPart).Enable().SetInt(parts[i]);
        item.SyncModifiedState(); return item;
    }
    private static void Reject(Action call, string why)
    { try { call(); } catch (InvalidOperationException) { checks++; return; } throw new Exception(why); }
    private static void Set(object instance, string name, object? value)
    {
        var type = instance as Type ?? instance.GetType();
        for (; type != null; type = type.BaseType)
        {
            var field = type.GetField(name, All | BindingFlags.DeclaredOnly) ?? type.GetField("<" + name + ">k__BackingField", All | BindingFlags.DeclaredOnly);
            if (field != null) { field.SetValue(instance is Type ? null : instance, value); return; }
        }
        throw new MissingFieldException(name);
    }

    internal static void Run()
    {
        var localize = typeof(LocHelper).GetMethod("Get", All)!; var info = new PatchInfo();
        typeof(PatchInfo).GetMethod("AddPrefixes", All)!.Invoke(info, new object[] { "deep_space_qol.tests.chem_mix", new[] { new HarmonyMethod(typeof(Tests).GetMethod(nameof(Localize), All)!) } });
        typeof(Harmony).Assembly.GetType("HarmonyLib.PatchFunctions", true)!.GetMethod("UpdateWrapper", All)!.Invoke(null, new object[] { localize, info });
        var timer = Stopwatch.StartNew();
        try
        {
            NativeReactionOrder(); PlannerCases(); RawPreparation(); ExactTransfers(); Protection(); NameLayout();
        }
        finally
        {
            var detours = (IDictionary)typeof(Harmony).Assembly.GetType("HarmonyLib.PatchTools", true)!.GetField("detours", All)!.GetValue(null)!;
            if (detours[localize] is IDisposable d) { d.Dispose(); detours.Remove(localize); }
        }
        Console.WriteLine($"Chemical mix: {checks} checks passed in {timer.ElapsedMilliseconds} ms. Native reaction handlers on detached data, bounded two-stage planner, exact-dose conservation, compiled protection and rollback. No factories, scene, live inventory or saves used.");
    }

    private static void NativeReactionOrder()
    {
        var start = Parts(("caffeine", 10000), ("acid", 100), ("protein", 100), ("stabilizing_agent", 1));
        var next = ChemicalMixModel.Tick(start, 26);
        Check(Part(next, "acid") == 0 && Part(next, "protein") == 100 && Part(next, "debris") == 1 && Part(next, "stabilizing_agent") == 0,
            $"Native shallow TagState sharing must preserve the reaction order: acid={Part(next, "acid")}, protein={Part(next, "protein")}, debris={Part(next, "debris")}, stabilizer={Part(next, "stabilizing_agent")}");
        Check(Part(start, "acid") == 100 && Part(start, "protein") == 100, "Predictions never mutate input arrays");
        var pure = Parts(("caffeine", 50000), ("acid", 20000), ("base", 20000));
        var end = ChemicalMixModel.Settle(pure, 26)!;
        Check(Part(end, "acid") == 0 && Part(end, "base") == 0 && Part(end, "water") == 0 && Part(end, "caffeine") == 50000, "Native neutralization generates neither water nor salt");
        var noWater = ChemicalMixModel.Settle(Parts(("caffeine", 50000), ("chlorine", 5000), ("chemical_contaminant", 10000)), 26)!;
        Check(Part(noWater, "chemical_contaminant") == 10000, "Chlorine cannot purify without water");
        var chlorinated = ChemicalMixModel.Settle(Parts(("caffeine", 50000), ("water", 10000), ("chlorine", 5000), ("chemical_contaminant", 10000)), 26)!;
        Check(Part(chlorinated, "chemical_contaminant") > 0 && Part(chlorinated, "chemical_contaminant") < 10000 && Part(chlorinated, "caffeine") == 50000,
            "Chlorine lowers contamination with native residue, preserving active");
        var rng = new Random(9117); var compiled = Production.GetType("DeepSpaceQoL.ChemicalMixModel")!.GetMethod("Tick", All)!;
        var notify = typeof(WaterFeatureHelper).GetMethod("OnLiquidChanged", All)!;
        var info = new PatchInfo();
        typeof(PatchInfo).GetMethod("AddPrefixes", All)!.Invoke(info, new object[] { "deep_space_qol.tests.chem_mix.native_chain", new[] { new HarmonyMethod(typeof(Tests).GetMethod(nameof(SyncOnly), All)!) } });
        typeof(Harmony).Assembly.GetType("HarmonyLib.PatchFunctions", true)!.GetMethod("UpdateWrapper", All)!.Invoke(null, new object[] { notify, info });
        try
        {
            for (int n = 0; n < 60; n++)
            {
                var mix = Parts(("caffeine", 100000), ("water", rng.Next(10000)), ("acid", rng.Next(10000)), ("base", rng.Next(10000)),
                    ("protein", rng.Next(10000)), ("fat", rng.Next(10000)), ("lipobond_agent", rng.Next(10000)), ("chemical_contaminant", rng.Next(10000)),
                    ("microbe", rng.Next(10000)), ("chlorine", rng.Next(10000)), ("stabilizing_agent", rng.Next(10000)));
                var a = ChemicalMixModel.Tick(mix, 26); var b = (int[])compiled.Invoke(null, new object[] { mix, 26d })!;
                var native = Item(mix); ChemicalHelper.HandleTemperatureReaction(native);
                Check(a.SequenceEqual(b) && a.SequenceEqual(WaterHelper.GetLiquidParts(native)),
                    "Compiled prediction agrees with the installed complete native reaction chain, with only graphics notification isolated");
                Check(Part(a, "caffeine") == 100000 && a.All(p => p >= 0), "Room-temperature reaction vector retains active and nonnegative values");
            }
        }
        finally
        {
            var detours = (IDictionary)typeof(Harmony).Assembly.GetType("HarmonyLib.PatchTools", true)!.GetField("detours", All)!.GetValue(null)!;
            if (detours[notify] is IDisposable d) { d.Dispose(); detours.Remove(notify); }
        }
    }

    private static void PlannerCases()
    {
        var acid = Reagent("acid", ("acid", 100000));
        var alkali = Reagent("base", ("base", 100000));
        var oilAgent = Reagent("lipobond_agent", ("lipobond_agent", 100000));
        foreach (bool final in new[] { false, true })
        {
            var initial = Parts(("caffeine", 50000), ("acid", 10000));
            var plan = Search(initial, final, 200000, alkali);
            Check(plan.Doses.Count > 0 && Part(plan.Final, "acid") == 0 && Part(plan.Final, "base") == 0, "Both stages can precisely neutralize a single residual reagent");
            Check(plan.Doses.Sum(d => d.Volume) == 10000 && Part(initial, "acid") == 10000 && Part(alkali.Parts, "base") == 100000, "Planner doesn't consume source or target while calculating");
            var shortPlan = Search(initial, final, 200000, Reagent("base", ("base", 3000)));
            Check(shortPlan.Doses.Sum(d => d.Volume) <= 3000 && Part(shortPlan.Final, "acid") >= 7000 && shortPlan.Warnings.Count > 0, "Shortage uses only bound available amount and reports residual instead of inventing reagent");
            Check(Search(initial, final, 60000, alkali).Doses.Count == 0, "Full containers are never overflowed to force a reaction");
            Check(Search(Parts(("caffeine", 50000), ("physical_contaminant", 10000)), final, 200000, acid, alkali).Doses.Count == 0,
                "Physical pollution doesn't waste unrelated acid/base");
        }
        var fatty = Search(Parts(("caffeine", 50000), ("fat", 8000)), false, 200000, oilAgent);
        Check(fatty.Doses.Count > 0 && Part(fatty.Final, "fat") < 8000 && Part(fatty.Final, "debris") > 0 && Part(fatty.Final, "caffeine") == 50000,
            "Pretreatment creates native filterable sediment instead of deleting oil");
        Check(Search(Parts(("caffeine", 50000), ("fat", 8000)), true, 200000, oilAgent).Doses.Count == 0,
            "Finishing stage doesn't deliberately add fresh oil-precipitation reagent");
        var protein = Search(Parts(("caffeine", 50000), ("protein", 8000)), false, 200000, acid, alkali);
        Check(protein.Doses.Count > 0 && Part(protein.Final, "protein") < 8000 && Part(protein.Final, "debris") > 0,
            "Pretreatment considers precipitation before final neutralization");
        var dirty = Reagent("base", ("base", 50000), ("heavy_metal", 50000));
        var denied = Search(Parts(("caffeine", 50000), ("acid", 10000)), true, 200000, dirty);
        Check(denied.Doses.Count == 0, "Adding more permanent contamination than removed is not a purification plan");
        Check(ChemicalMixPlanner.ReagentKind(Parts(("base", 9000), ("caffeine", 1))) == "", "Source active ingredients cannot be laundered into artificial yield gains");
        Check(ChemicalMixPlanner.ReagentKind(Parts(("base", 9000), ("methylol", 1))) == "", "Unknown reaction precursors cannot enter from a source bottle");
        Check(ChemicalMixModel.SafeInput(Parts(("caffeine", 100), ("coffee_mush", 1)), 26) &&
            !ChemicalMixModel.SafeInput(Parts(("caffeine", 100)), 40) && !ChemicalMixModel.SafeInput(Parts(("caffeine", 100)), double.NaN), "Raw extraction, heating and invalid temperatures are gated");
        var impure = Reagent("base", ("base", 90000), ("physical_contaminant", 10000));
        var partial = Search(Parts(("caffeine", 50000), ("acid", 9000)), true, 200000, impure);
        Check(partial.Doses.Count > 0 && Part(partial.Final, "physical_contaminant") > 0 && ChemicalMixModel.Purity(partial.Final, "caffeine") < 100,
            "Actual source impurities survive the prediction and prevent false 100% claims");
        int consumed = 0;
        foreach (var dose in partial.Doses)
        {
            consumed += dose.Volume;
            Check(dose.Volume > 0 && ChemicalMixModel.Total(dose.AfterPour) <= 200000 &&
                ChemicalMixModel.Total(dose.SourceBefore) >= dose.Volume, "Every dose is feasible before any reaction frees capacity");
        }
        Check(consumed <= ChemicalMixModel.Total(impure.Parts), "Multi-step plan cannot reuse already consumed material");
        var chlorine = Reagent("chlorine", ("chlorine", 50000));
        var noWater = Search(Parts(("caffeine", 50000), ("chemical_contaminant", 9000)), true, 200000, chlorine);
        Check(noWater.Doses.Count == 0, "Finishing cannot consume chlorine ineffectively without water");
        var withWater = Search(Parts(("caffeine", 50000), ("water", 10000), ("chemical_contaminant", 9000)), true, 200000, chlorine);
        Check(withWater.Doses.Count > 0 && Part(withWater.Final, "chemical_contaminant") < 9000 && Part(withWater.Final, "chemical_contaminant") > 0,
            "Finishing lowers native chemical pollution but never fakes removal below the native floor");
    }

    private static void RawPreparation()
    {
        var alcohol = Reagent("alcohol", ("alcohol", 980000), ("water", 20000));
        var acid = Reagent("acid", ("acid", 100000));
        foreach (var pair in new[] { ("dream_cap_mush", "dream_cap_extract"), ("nightmare_cap_mush", "nightmare_extract"), ("coffee_mush", "caffeine") })
        {
            var initial = Parts((pair.Item1, 27333), ("water", 4000), ("protein", 2000), ("mineral", 500));
            var plan = new ChemicalMixPlan();
            foreach (int _ in ChemicalMixPlanner.Search(plan, initial, 26, 1000000, pair.Item2, false, new[] { alcohol, acid })) { }
            Check(plan.Raw == pair.Item1 && plan.Doses.Count >= 2 && plan.Doses[0].Reagent.Kind == "alcohol", "Cold mash with zero active starts with an alcohol dose before protein preparation");
            Check(Part(plan.Final, "alcohol") >= ChemicalMixPlanner.SolventTarget(initial) && Part(plan.Final, "protein") == 0 && Part(plan.Final, "debris") >= 2000,
                "Solvent threshold and protein precipitation are both planned from real bottle concentration");
            Check(Part(plan.Final, pair.Item1) == 27333 && Part(plan.Final, pair.Item2) == 0, "Cold dosing never pretends that native heated extraction has completed");
            Check(Part(initial, "alcohol") == 0 && Part(initial, "protein") == 2000 && Part(alcohol.Parts, "alcohol") == 980000, "Mash preview doesn't consume raw ingredients or reagent bottles");
            Check(plan.Warnings.Any(w => w.Contains("加热")), "Native heating requirement is explicit before filtering raw material");
            Check(ChemicalMixPlanner.DefaultActive(initial) == pair.Item2 && !ChemicalMixPlanner.HasTarget(initial, pair.Item2, true), "Raw category auto-selects matching recipe but can't masquerade as finishing input");
        }
        var raw = Parts(("dream_cap_mush", 10000), ("alcohol", 10000));
        var missing = new ChemicalMixPlan();
        foreach (int _ in ChemicalMixPlanner.Search(missing, raw, 26, 200000, "dream_cap_extract", false, new[] { Reagent("alcohol", ("alcohol", 10000)) })) { }
        Check(missing.Doses.Sum(d => d.Volume) == 10000 && Part(missing.Final, "alcohol") == 20000 && missing.Warnings.Any(w => w.Contains("酒精尚缺")), "Missing solvent uses only the explicitly bound supply and warns before low-yield heating");
        var completion = Production.GetType("DeepSpaceQoL.ChemicalMixService")!.GetMethod("RawCompletionHint", All)!;
        Check(((string)completion.Invoke(null, new object[] { missing.Final })!).Contains("暂勿加热"), "Partial-dose completion must not instruct the player to heat solvent-deficient mash");
        var existing = new ChemicalMixPlan();
        foreach (int _ in ChemicalMixPlanner.Search(existing, raw, 26, 200000, "dream_cap_extract", false, new[] { Reagent("alcohol", ("alcohol", 100000)) })) { }
        Check(existing.Doses.Sum(d => d.Volume) == 40000, "Existing solvent is subtracted from the five-times-mush requirement");
        Check(((string)completion.Invoke(null, new object[] { existing.Final })!).Contains("原生加热板"), "Only solvent-complete preparation instructs native heating before filtering");
        Check(ChemicalMixPlanner.SolventTarget(Parts(("coffee_mush", 10000))) == 7500 && ChemicalMixPlanner.SolventTarget(Parts(("dream_cap_mush", int.MaxValue))) == 10737418235L,
            "Coffee native per-tick denominator and large mushroom ratio use correct arithmetic");
        var warm = ChemicalMixModel.Settle(Parts(("dream_cap_mush", 10000), ("alcohol", 50000)), 45)!;
        Check(Part(warm, "dream_cap_mush") == 0 && Part(warm, "dream_cap_extract") == 5000 && Part(warm, "alcohol") == 50000,
            "Prepared mushroom ratio reaches native maximum extraction at actual warm temperature and doesn't consume solvent");
    }

    private static void NameLayout()
    {
        var method = Production.GetType("DeepSpaceQoL.NativeStorageNaming")!.GetMethod("MeasureName", All)!;
        foreach (var size in new[] { (48f, 48f), (96f, 96f), (48f, 144f), (144f, 48f) })
        {
            var layout = ((float Width, float Height, float FontMax, bool Visible))method.Invoke(null, new object[] { size.Item1, size.Item2 })!;
            Check(layout.Visible && layout.Width < size.Item1 && layout.Height < size.Item2 && layout.FontMax is >= 5 and <= 14, "Storage name fits inside ordinary/rotated box face without an external floating bar");
        }
        foreach (float width in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            var layout = ((float Width, float Height, float FontMax, bool Visible))method.Invoke(null, new object[] { width, 48f })!;
            Check(!layout.Visible, "Invalid/collapsed render size doesn't produce a stray label");
        }
    }

    private static void ExactTransfers()
    {
        var rng = new Random(6116);
        var compiled = Production.GetType("DeepSpaceQoL.ChemicalDoseTransfer")!.GetMethod("Move", All)!;
        for (int i = 0; i < 80; i++)
        {
            var parts = Parts(("base", rng.Next(1000, 100000)), ("water", rng.Next(1000)), ("chemical_contaminant", rng.Next(2000)));
            var input = Parts(("caffeine", 30000), ("acid", 10000));
            var source = Item(parts); var target = Item(input); int amount = rng.Next(1, ChemicalMixModel.Total(parts) + 1);
            bool notified = false;
            Action<GameItem, GameItem, bool> changed = (t, s, empty) => { notified = ReferenceEquals(s, source) && ReferenceEquals(t, target) && !empty; };
            compiled.Invoke(null, new object[] { source, target, amount, changed });
            var expected = ChemicalMixModel.Add(input, parts, amount);
            Check(notified && WaterHelper.GetLiquidParts(target).SequenceEqual(expected), "Compiled quota pour calls notification with exact actual whole-mixture portions");
            Check(WaterHelper.GetLiquidParts(source).Sum() == parts.Sum() - amount && WaterHelper.GetLiquidParts(target).Sum() == input.Sum() + amount,
                "Exact pour conserves all microlitres");
            Check(WaterHelper.GetLiquidParts(source).Select((p, n) => p + WaterHelper.GetLiquidParts(target)[n]).SequenceEqual(parts.Select((p, n) => p + input[n])),
                "No individual pollutant/active component is created or deleted during pouring");
        }
        var s1 = Item(Parts(("base", 10000))); var t1 = Item(Parts(("caffeine", 50000)), 50000);
        Reject(() => ChemicalDoseTransfer.Move(s1, t1, 1, (_, _, _) => { }), "Overflow rejected before any write");
        Check(Part(WaterHelper.GetLiquidParts(s1), "base") == 10000, "Overflow does not deduct source");
        Reject(() => ChemicalDoseTransfer.Move(s1, s1, 1, (_, _, _) => { }), "Same-container pour rejected");
        t1.state.GetTag(WaterHelper.FILTER_CONTAINER_TAG).Enable(); t1.SyncModifiedState();
        Reject(() => ChemicalDoseTransfer.Move(s1, t1, 1, (_, _, _) => { }), "Exact dosing cannot bypass installed filter use");

        var from = Item(Parts(("base", 9000), ("physical_contaminant", 1000)));
        var into = Item(Parts(("caffeine", 30000), ("acid", 9000)));
        int[] originalFrom = WaterHelper.GetLiquidParts(from), originalTo = WaterHelper.GetLiquidParts(into);
        var notify = typeof(WaterFeatureHelper).GetMethod("OnLiquidContainerPourInto", All)!;
        var info = new PatchInfo();
        typeof(PatchInfo).GetMethod("AddPrefixes", All)!.Invoke(info, new object[] { "deep_space_qol.tests.chem_mix.rollback", new[] { new HarmonyMethod(typeof(Tests).GetMethod(nameof(FailPour), All)!) } });
        typeof(Harmony).Assembly.GetType("HarmonyLib.PatchFunctions", true)!.GetMethod("UpdateWrapper", All)!.Invoke(null, new object[] { notify, info });
        try
        {
            var pour = Production.GetType("DeepSpaceQoL.ChemicalMixService")!.GetMethod("Pour", All)!;
            try { pour.Invoke(null, new object[] { from, into, 1000, ChemicalMixModel.Add(originalTo, originalFrom, 1000) }); throw new Exception("Expected callback failure"); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException error && error.Message.Contains("已回滚")) { checks++; }
            Check(WaterHelper.GetLiquidParts(from).SequenceEqual(originalFrom) && WaterHelper.GetLiquidParts(into).SequenceEqual(originalTo),
                "Compiled transaction restores exact source and target compositions after partial native callback failure");
            Check(from.unitValue == 0 && !into.IsTag("TEST_PARTIAL_CALLBACK"), "Rollback also restores scalar and partial callback tag mutations");
        }
        finally
        {
            var detours = (IDictionary)typeof(Harmony).Assembly.GetType("HarmonyLib.PatchTools", true)!.GetField("detours", All)!.GetValue(null)!;
            if (detours[notify] is IDisposable d) { d.Dispose(); detours.Remove(notify); }
        }
    }

    private static void Protection()
    {
        var method = Production.GetType("DeepSpaceQoL.ChemicalMixService")!.GetMethod("CanUseContainer", All)!;
        bool Allowed(GameItem? item) => (bool)method.Invoke(null, new object?[] { item, "" })!;
        Check(!Allowed(null), "Null container rejected");
        var entry = (EmporiumEntry)RuntimeHelpers.GetUninitializedObject(typeof(EmporiumEntry));
        var grid = (GameGridInventory)RuntimeHelpers.GetUninitializedObject(typeof(GameGridInventory)); Set(grid, "items", new List<GameItem>());
        entry.invElement = grid; Set(typeof(EmporiumEntry), "Instance", entry);
        try
        {
            foreach (string flag in new[] { "PINNED_TAG", "LOU_TOY_BOX_TAG", "LOU_TOY_TAG", "LOU_LOCKER_TAG", "IMPORTANT_TAG", "ITEM_BEHIND_LOCK_TAG", "FILTER_CONTAINER_TAG", "WINE_CONTAINER_TAG" })
            {
                var item = Item(Parts(("caffeine", 10000))); item.parentInventory = grid;
                ((List<GameItem>)typeof(GameGridInventory).GetField("items", All)!.GetValue(grid)!).Add(item);
                item.state.GetTag(flag).Enable(); item.SyncModifiedState();
                if (flag == "PINNED_TAG") Check(Allowed(item), "Pinned but unlocked owned container supports in-place liquid use");
                else Check(!Allowed(item), "Compiled protection rejects " + flag);
            }
            var foreign = Item(Parts(("caffeine", 10000))); foreign.parentInventory = grid;
            foreign.state.GetTag("IS_OWNED_TAG").Disable(); foreign.SyncModifiedState();
            Check(!Allowed(foreign), "Customer/unowned container rejected");

            var owner = Item(Parts()); owner.identifier = "sec_box";
            owner.state.GetTag("IMPORTANT_TAG").Enable(); owner.SyncModifiedState(); owner.parentInventory = grid;
            ((List<GameItem>)typeof(GameGridInventory).GetField("items", All)!.GetValue(grid)!).Add(owner);
            var pageGrid = (GameGridInventory)RuntimeHelpers.GetUninitializedObject(typeof(GameGridInventory)); Set(pageGrid, "items", new List<GameItem>());
            var window = (PixelWindow)RuntimeHelpers.GetUninitializedObject(typeof(PixelWindow));
            Set(window, "parentItems", new List<GameItem> { owner }); Set(window, "childElement", pageGrid);
            Set(owner, "contentWindow", window); Set(pageGrid, "parent", window);
            var pageType = Production.GetType("DeepSpaceQoL.StoragePage")!;
            var page = Activator.CreateInstance(pageType, All, null, new object?[] { owner, 0, pageGrid, "test" }, null)!;
            var canPage = Production.GetType("DeepSpaceQoL.ChemicalMixService")!.GetMethod("CanUsePage", All)!;
            bool PageAllowed() => (bool)canPage.Invoke(null, new[] { page })!;
            Check(PageAllowed(), "Audited owned ordinary storage page can be a reagent source");
            foreach (string policy in new[] { "{\"Version\":1,\"ContainerLocked\":true}", "{\"Version\":1,\"Pages\":{\"0\":{\"SaleLocked\":true}}}" })
            {
                owner.state.GetTag("DEEPSPACE_QOL_STORAGE_V1").Enable().SetString(policy); owner.SyncModifiedState();
                Check(PageAllowed(), "Explicit chemistry selection allows QoL whole-box/page locks");
            }
            owner.state.GetTag("DEEPSPACE_QOL_STORAGE_V1").Disable(); owner.state.GetTag("LOU_TOY_BOX_TAG").Enable(); owner.SyncModifiedState();
            Check(!PageAllowed(), "Lou identity prunes a storage-shaped reagent source");
        }
        finally { Set(typeof(EmporiumEntry), "Instance", null); }
    }
}

sealed class DataItem : GameItem
{
    internal DataItem() : base("chemical_mix_test") { }
    public override void Validate() { }
    public override void Destroy() => throw new Exception("No test may destroy game items");
}
