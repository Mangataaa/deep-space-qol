using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using DeepSpaceQoL.Core;

string managed = args.Length == 1 ? Path.GetFullPath(args[0]) : @"D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed";
AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(managed, name.Name + ".dll"))
    ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed, name.Name + ".dll")) : null;
Tests.Run();

static class Tests
{
    private static int checks;
    private static void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
    private static T Native<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static void Set(object instance, string name, object? value)
    {
        for (var type = instance as Type ?? instance.GetType(); type != null; type = type.BaseType)
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var field = type.GetField(name, flags) ?? type.GetField("<" + name + ">k__BackingField", flags);
            if (field == null) continue;
            field.SetValue(instance is Type ? null : instance, value); return;
        }
        throw new MissingFieldException(name);
    }
    private static MachineryPanel Panel(string role, PixelElement content, bool wrapped)
    {
        if (wrapped) { var intake = Native<MachineryIntake>(); Set(intake, "content", content); content = intake; }
        var panel = Native<MachineryPanel>(); panel.role = role; Set(panel, "content", content); return panel;
    }
    private static (GameItem item, GameSlotInventory input, GameGridInventory output) Finisher(bool wrapped)
    {
        var item = Native<GameItemElement>(); item.identifier = "chem_finisher"; item.unitCount = 1;
        var handler = Native<TreeNodeRender>(); Set(handler, "m_CachedPtr", new IntPtr(1)); Set(item, "handler", handler);
        var input = Native<GameSlotInventory>(); var output = Native<GameGridInventory>();
        var controls = Native<MachineryStack>(); Set(controls, "children", new List<PixelElement>());
        var row = Native<MachineryPanelRow>();
        Set(row, "panels", new List<MachineryPanel> { Panel("container", input, wrapped), Panel("output", output, wrapped), Panel("control", controls, false) });
        Set(row, "linked", new List<GraphNodeStorage>());
        foreach (var panel in row.Panels) panel.owner = row;
        var window = Native<PixelWindow>(); Set(window, "childElement", row); Set(window, "parentItems", new List<GameItem> { item });
        row.parent = window; Set(item, "contentWindow", window); row.RelinkStorage();
        return (item, input, output);
    }
    private sealed class Unit { internal bool Attached; }

    internal static void Run()
    {
        foreach (bool fitsFirst in new[] { false, true })
        foreach (bool fitsAfter in new[] { false, true })
        foreach (bool placed in new[] { false, true })
        {
            int raw = 10000, backup = -1, fits = 0, removed = 0, restores = 0;
            var unit = new Unit();
            var events = new List<string>();
            var result = ChemBatchTransaction.One(() => unit, _ => { }, _ => ++fits == 1 ? fitsFirst : fitsAfter,
                () => backup = raw, _ => { raw -= 5000; events.Add("consume"); },
                u => { u.Attached = placed; return placed; },
                () => { Check(!unit.Attached, "A failed product must be discarded before restoring raw material"); raw = backup; restores++; events.Add("restore"); },
                u => { u.Attached = false; removed++; events.Add("discard"); });
            bool ok = fitsFirst && fitsAfter && placed;
            Check((result == ChemBatchTransaction.Result.Produced) == ok, "Only a complete successful placement consumes material");
            Check(raw == (ok ? 5000 : 10000), "Full/changed/rejected output never loses raw material");
            Check(removed == (ok ? 0 : 1), "Failed new products are discarded exactly once");
            Check(restores == (fitsFirst && !ok ? 1 : 0), "Untouched input needs no rollback");
        }
        // Exception injection covers partial consumption and a callback that attaches then throws.
        foreach (string failure in new[] { "preview", "fit-first", "checkpoint", "consume-partial", "fit-after", "attach-throw", "placement" })
        {
            var unit = new Unit(); int raw = 10000, backup = -1, discarded = 0, restored = 0, fits = 0;
            try
            {
                ChemBatchTransaction.One(() => unit,
                    _ => { if (failure == "preview") throw new InvalidOperationException(); },
                    _ => { fits++; if (failure == (fits == 1 ? "fit-first" : "fit-after")) throw new InvalidOperationException(); return true; },
                    () => { if (failure == "checkpoint") throw new InvalidOperationException(); backup = raw; },
                    _ => { raw -= 2; if (failure == "consume-partial") throw new InvalidOperationException(); raw -= 4998; },
                    u => { u.Attached = true; if (failure == "attach-throw") throw new InvalidOperationException(); return failure != "placement"; },
                    () => { Check(!unit.Attached, "Attached failing result is removed before rollback"); raw = backup; restored++; },
                    u => { u.Attached = false; discarded++; });
                Check(failure == "placement", "Every injected exception must propagate");
            }
            catch (InvalidOperationException) { Check(failure != "placement", "Only injected native failures escape"); }
            Check(raw == 10000 && !unit.Attached && discarded == 1, "Partial writes recover exact source and no orphan product");
            Check(restored == (failure is "consume-partial" or "fit-after" or "attach-throw" or "placement" ? 1 : 0), "Rollback starts even when consumption throws midway");
        }
        int recovery = 0;
        try
        {
            ChemBatchTransaction.One(() => new Unit(), _ => { }, _ => true, () => { }, _ => { }, _ => false,
                () => recovery++, _ => throw new InvalidOperationException("discard failed"));
        }
        catch (InvalidOperationException) { }
        Check(recovery == 0, "Do not restore raw material when removal of a failed output cannot be confirmed");

        var service = typeof(DeepSpaceQoL.ModEntry).Assembly.GetType("DeepSpaceQoL.ChemicalBatchService")!;
        var tryParts = service.GetMethod("TryParts", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (bool wrapped in new[] { false, true })
        {
            var native = Finisher(wrapped);
            object?[] values = { native.item, null, null, null };
            Check((bool)tryParts.Invoke(null, values)! && ReferenceEquals(values[1], native.input) && ReferenceEquals(values[2], native.output),
                "Compiled production resolves the native machine input/output by role, including intake wrappers");
            native.item.identifier = "bottle_printer";
            Check(!(bool)tryParts.Invoke(null, new object?[] { native.item, null, null, null })!, "Another machine can never enter batch chemistry");
            native.item.identifier = "chem_finisher";
            var foreign = Native<PixelWindow>(); Set(foreign, "parentItems", new List<GameItem>());
            Set(native.output, "parent", foreign);
            Check(!(bool)tryParts.Invoke(null, new object?[] { native.item, null, null, null })!, "Foreign output inventory cannot be relabeled as this machine's output");
        }
        var countAvailable = service.GetMethod("AvailableCount", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(int), typeof(int) }, null)!;
        int Count(int amount, int perUnit) => (int)countAvailable.Invoke(null, new object[] { amount, perUnit })!;
        Check(Count(66000, 2000) == 33 && Count(2000000, 100) == 20000, "The actual batch count has no implicit sixteen-product cap");
        Check(Count(33999, 2000) == 16 && Count(34000, 2000) == 17, "Sixteen is only a possible material quotient, not a task limit");
        Check(Count(-1, 2000) == 0 && Count(2000, 0) == 0 && Count(2000, -1) == 0, "Invalid contents or recipe volume never start a batch");
        ExplicitLockedTargets(service);
        Console.WriteLine($"Chemical batch: {checks} checks passed; transaction failure recovery, actual installed native graph getters, uncapped material count and explicit locked-page permissions. No factories, Unity rendering, save writes or in-game production executed.");
    }

    private static void ExplicitLockedTargets(Type service)
    {
        var access = service.GetMethod("CanUseExplicitInventory", BindingFlags.Static | BindingFlags.NonPublic)!;
        var target = service.GetMethod("CanUseTarget", BindingFlags.Static | BindingFlags.NonPublic)!;
        var machine = Finisher(false).item;
        bool Allowed(GameInventory? inventory) => (bool)access.Invoke(null, new object?[] { inventory })!;
        Check(!Allowed(null), "Explicit chemistry destination still requires a real inventory");
        var entry = Native<EmporiumEntry>();
        var root = Native<GameGridInventory>(); Set(root, "items", new List<GameItem>()); entry.invElement = root;
        var owner = new FixtureItem { identifier = "sec_box", parentInventory = root };
        owner.state.GetTag("IS_OWNED_TAG").Enable(); owner.state.GetTag("IMPORTANT_TAG").Enable(); owner.SyncModifiedState();
        ((List<GameItem>)typeof(GameGridInventory).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(root)!).Add(owner);
        var grid = Native<GameGridInventory>(); Set(grid, "items", new List<GameItem>());
        var window = Native<PixelWindow>(); Set(window, "parentItems", new List<GameItem> { owner }); Set(window, "childElement", grid);
        Set(owner, "contentWindow", window); Set(grid, "parent", window);
        Set(typeof(EmporiumEntry), "Instance", entry);
        try
        {
            Check(Allowed(grid) && (bool)target.Invoke(null, new object[] { machine, grid })!, "Audited owned storage can be chosen explicitly");
            foreach (string policy in new[] { "{\"Version\":1,\"ContainerLocked\":true}", "{\"Version\":1,\"Pages\":{\"0\":{\"SaleLocked\":true}}}" })
            {
                owner.state.GetTag("DEEPSPACE_QOL_STORAGE_V1").Enable().SetString(policy); owner.SyncModifiedState();
                Check(Allowed(grid) && (bool)target.Invoke(null, new object[] { machine, grid })!, "QoL whole-box and page locks do not hide an explicitly selected output");
                var rules = typeof(DeepSpaceQoL.ModEntry).Assembly.GetType("DeepSpaceQoL.StorageRules")!;
                Check(!(bool)rules.GetMethod("CanAutoAccess", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, new object[] { grid })!, "Automatic stocking/stowing remains locked even when explicit chemistry is allowed");
            }
            owner.state.GetTag("DEEPSPACE_QOL_STORAGE_V1").Enable().SetString("{\"Version\":2}"); owner.SyncModifiedState();
            Check(!Allowed(grid), "Unknown policy versions remain fail-closed");
            owner.state.GetTag("DEEPSPACE_QOL_STORAGE_V1").Disable(); owner.SyncModifiedState();
            foreach (string flag in new[] { "LOU_TOY_BOX_TAG", "LOU_LOCKER_TAG", "LOCKED_TAG", "ITEM_BEHIND_LOCK_TAG" })
            {
                owner.state.GetTag(flag).Enable(); owner.SyncModifiedState();
                Check(!Allowed(grid), "Explicit chemistry never overrides native/story protection " + flag);
                owner.state.GetTag(flag).Disable(); owner.SyncModifiedState();
            }
            foreach (bool insert in new[] { false, true })
            {
                grid.overrideLockInsert = insert; grid.overrideLockRemove = !insert;
                Check(!Allowed(grid), "Native inventory insertion/removal seals remain protected");
            }
            grid.overrideLockInsert = grid.overrideLockRemove = false;
            owner.state.GetTag("IS_OWNED_TAG").Disable(); owner.SyncModifiedState();
            Check(!Allowed(grid), "Unowned destination remains rejected");
        }
        finally { Set(typeof(EmporiumEntry), "Instance", null); }
    }
}

sealed class FixtureItem : GameItem
{
    internal FixtureItem() : base("chemical_batch_fixture") { }
    public override void Validate() { }
    public override void Destroy() => throw new InvalidOperationException("Tests cannot destroy game objects");
}
