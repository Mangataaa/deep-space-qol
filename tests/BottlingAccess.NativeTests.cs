using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using DeepSpaceQoL;

// Real installed game assemblies and linked production access/catalogue/rules execute here.
// Data-only fixtures never invoke a Unity constructor, transfer liquid, draw UI or patch the game.
string managed = args.Length == 1 ? Path.GetFullPath(args[0]) : @"D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed";
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string path = Path.Combine(managed, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
NativeBottlingTests.Run();

static class NativeBottlingTests
{
    private static int checks;
    private static T Native<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException(message); }
    private static void Set(object target, string name, object? value)
    {
        for (Type? type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                ?? type.GetField("<" + name + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null) continue;
            field.SetValue(target, value); return;
        }
        throw new MissingFieldException(target.GetType().FullName, name);
    }
    private static void SetStatic(Type type, string name, object? value) =>
        (type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) ??
         type.GetField("<" + name + ">k__BackingField", BindingFlags.NonPublic | BindingFlags.Static) ??
         throw new MissingFieldException(type.FullName, name)).SetValue(null, value);

    private static void Tag(GameItem item, string tag, bool enabled = true) =>
        item.state.dict[tag] = new TagState(tag, tag) { valueEnabled = enabled };
    private static GameItem Item(string id, params string[] tags)
    {
        var item = Native<GameItemElement>();
        item.identifier = id; item.unitCount = 1; item.canUseOutsideCombat = true;
        item.itemTypes = new(); item.itemFeatures = new(); item.state = new(); item.modifiedState = item.state;
        foreach (string tag in tags.Append("IS_OWNED_TAG")) Tag(item, tag);
        // IsDestroyed only compares the pointer to zero; this fake pointer is never dereferenced.
        var handler = Native<TreeNodeRender>(); Set(handler, "m_CachedPtr", new IntPtr(1)); Set(item, "handler", handler);
        return item;
    }
    private static GameItem LiquidItem(string id, int capacity, params string[] tags)
    {
        var item = Item(id, tags.Concat(new[] { WaterHelper.LIQUID_CONTAINER_TAG, WaterHelper.LIQUID_CONTAINER_CAPACITY,
            "CONTAINER_TEMP_TAG" }).ToArray());
        item.state.dict[WaterHelper.LIQUID_CONTAINER_CAPACITY].valueInt = capacity;
        return item;
    }
    private static GameGridInventory Grid()
    {
        var grid = Native<GameGridInventory>(); Set(grid, "items", new List<GameItem>()); return grid;
    }
    private static void Put(GameGridInventory grid, params GameItem[] items)
    {
        var list = (List<GameItem>)typeof(GameGridInventory).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(grid)!;
        foreach (var item in items) { list.Add(item); item.parentInventory = grid; }
    }
    private static void Remove(GameGridInventory grid, GameItem item) =>
        ((List<GameItem>)typeof(GameGridInventory).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(grid)!).Remove(item);
    private static PixelWindow Window(GameItem owner, PixelElement content)
    {
        var window = Native<PixelWindow>(); Set(window, "childElement", content);
        Set(window, "parentItems", new List<GameItem> { owner }); Set(owner, "contentWindow", window);
        if (content is GameInventory inv) Set(inv, "parent", window);
        if (content is MachineryPanelRow row) row.parent = window;
        return window;
    }
    private static MachineryPanel Panel(string role, PixelElement content)
    { var p = Native<MachineryPanel>(); p.role = role; Set(p, "content", content); return p; }
    private static MachineryPanelRow Row(params MachineryPanel[] panels)
    {
        var row = Native<MachineryPanelRow>(); Set(row, "panels", panels.ToList());
        Set(row, "linked", new List<GraphNodeStorage>());
        foreach (var panel in panels) panel.owner = row;
        // Exercise native CollectStorage/Relink/AddParent instead of inventing graph links.
        row.RelinkStorage();
        return row;
    }
    private static MachineryIntake Intake(PixelElement content)
    { var intake = Native<MachineryIntake>(); Set(intake, "content", content); return intake; }
    private static (GameItem owner, GameGridInventory output, GameGridInventory input) Printer(string id = "bottle_printer", bool wrapped = true)
    {
        var owner = Item(id, "CONTAINER_TAG", "IMPORTANT_TAG");
        var output = Grid(); var input = Grid();
        var row = Row(Panel("power", wrapped ? Intake(input) : input), Panel("output", wrapped ? Intake(output) : output));
        Window(owner, row);
        return (owner, output, input);
    }
    private static EmporiumEntry Store()
    {
        StorageRules.ClearCache();
        var entry = Native<EmporiumEntry>(); entry.invElement = Grid();
        foreach (string field in new[] { "frontInvinvElement", "backInvinvElementCounter", "backInvinvElement", "hiddenElement", "showcaseElement" })
            Set(entry, field, Grid());
        SetStatic(typeof(EmporiumEntry), "Instance", entry);
        SetStatic(typeof(PlayerStore), "instance", Native<PlayerStore>());
        SetStatic(typeof(GameMaster), "current", Native<GameMaster>());
        return entry;
    }
    private static GameItem Source(EmporiumEntry store)
    {
        var source = LiquidItem("water_tank", 20_000_000, "WATER_TANK_TAG", "PINNED_TAG");
        source.mayInventoryRemoveItemFunc = (_, _) => false;
        // Same native water-container delegate condition, without running InitLiquid's localization/UI.
        source.mayThisTargetItemFunc = (s, t) => t.IsTag(WaterHelper.LIQUID_CONTAINER_TAG) &&
            WaterHelper.CanUseWaterContainer(s) && WaterHelper.CanUseWaterContainer(t);
        source.canThisTargetItemFunc = source.mayThisTargetItemFunc;
        Put(store.invElement, source); return source;
    }

    internal static void Run()
    {
        TestPinnedAndNativeInteraction();
        TestBoundedDiscovery();
        TestInstalledPrinters();
        TestLocksAndOwnership();
        TestTradeAccess();
        SetStatic(typeof(EmporiumEntry), "Instance", null);
        Check(BottlingCatalog.GetItems().Count == 0, "No shop must expose no liquid candidates.");
        StorageRules.ClearCache();
        Console.WriteLine($"Bottling access native tests passed: {checks} checks. Real Assembly-CSharp and production predicates/catalogue; offline fixtures, no Unity scene/actions/transfers.");
    }

    private static void TestPinnedAndNativeInteraction()
    {
        var store = Store(); var source = Source(store);
        var target = LiquidItem("bottled_water", 750_000, "PINNED_TAG");
        target.mayInventoryRemoveItemFunc = (_, _) => false; Put(store.invElement, target);
        Check(ItemPinHelper.IsPinned(source) && !source.MayRemove(), "Fixture has a real pinned, nonremovable source.");
        Check(ItemPinHelper.IsPinned(target) && !target.MayRemove(), "Fixture has a real pinned, nonremovable target.");
        Check(BottlingAccess.CanUseSource(source, out string initialReason), "Pinned/nonremovable source must allow in-place liquid access: " + initialReason);
        Check(BottlingAccess.CanUseTarget(source, target, out _), "Pinned/nonremovable target must allow in-place filling.");
        Check(source.MayTarget(target) && source.CanTarget(target), "Actual native MayTarget/CanTarget permit pinned bottles.");
        Check(BottlingAccess.CanTransfer(source, target, out _), "Production pair predicate must preserve native pin-independent interaction.");
        Check(source.parentInventory == store.invElement && target.parentInventory == store.invElement &&
            source.unitCount == 1 && target.unitCount == 1, "Permission checks must never relocate or split either item.");
        source.forceDisableUse = true;
        Check(!BottlingAccess.CanUseSource(source, out _), "Native forceDisableUse must still block a pinned source.");
        source.forceDisableUse = false; source.canUseOutsideCombat = false;
        Check(!BottlingAccess.CanUseSource(source, out _), "Native outside-combat use gate must still apply.");
        source.canUseOutsideCombat = true;
        source.mayThisTargetItemFunc = (_, _) => false;
        Check(!BottlingAccess.CanTransfer(source, target, out _), "Production transfer must execute native MayTarget veto.");
        source.mayThisTargetItemFunc = (_, _) => true; source.canThisTargetItemFunc = (_, _) => false;
        Check(!BottlingAccess.CanTransfer(source, target, out _), "Production transfer must execute native CanTarget veto.");
        source.canThisTargetItemFunc = (_, _) => true;
        target.parentInventory.mayItemTargetSlotItemFunc = (_, _, _, _) => false;
        Check(!BottlingAccess.CanTransfer(source, target, out _), "Native target-inventory interaction veto must be honored.");
        target.parentInventory.mayItemTargetSlotItemFunc = null;
        Check(BottlingAccess.CanTransfer(source, target, out _), "Removing a native veto restores access without moving items.");
        foreach (string id in new[] { "mini_bottle", "small_bottled_water", "bottled_water", "large_bottled_water", "water_jug" })
        { var bottle = LiquidItem(id, 750_000); Put(store.invElement, bottle); Check(BottlingAccess.CanUseTarget(source, bottle, out _), id + " is an audited native bottle."); }
        foreach (string tag in new[] { "WINE_CONTAINER_TAG", WaterHelper.FILTER_CONTAINER_TAG, WaterHelper.LIQUID_CONTAINER_DROPPER_TAG })
        { Tag(target, tag); Check(!BottlingAccess.CanUseTarget(source, target, out _), tag + " must exclude automatic filling."); Tag(target, tag, false); }
        var unknown = LiquidItem("unknown_mod_bottle", 100_000); Put(store.invElement, unknown);
        Check(!BottlingAccess.CanUseTarget(source, unknown, out _), "Unknown bottles must not enter the audited target set.");
        var large = LiquidItem("water_jug", 20_000_000); Put(store.invElement, large);
        Check(!BottlingAccess.CanUseTarget(source, large, out _), "Target must be strictly smaller than source.");
    }

    private static void TestBoundedDiscovery()
    {
        var store = Store(); var source = Source(store);
        var printer = Printer(); var otherMachine = Printer("water_purifier");
        var unknownMachine = Printer("unknown_mod_machine");
        Put(store.invElement, printer.owner, otherMachine.owner, unknownMachine.owner);
        var outputBottle = LiquidItem("small_bottled_water", 450_000); Put(printer.output, outputBottle);
        var inputBottle = LiquidItem("bottled_water", 750_000); Put(printer.input, inputBottle);
        var otherBottle = LiquidItem("bottled_water", 750_000); Put(otherMachine.output, otherBottle);
        var unknownBottle = LiquidItem("bottled_water", 750_000); Put(unknownMachine.output, unknownBottle);
        var tradeBottle = LiquidItem("bottled_water", 750_000); Put(store.backInvinvElement, tradeBottle);
        var looseHidden = LiquidItem("bottled_water", 750_000); Put(store.hiddenElement, looseHidden);
        var shownPrinter = Printer(); Put(store.showcaseElement, shownPrinter.owner);
        var shownOutput = LiquidItem("bottled_water", 750_000); Put(shownPrinter.output, shownOutput);
        var items = BottlingCatalog.GetItems();
        Check(items.Contains(source) && items.Contains(outputBottle) && items.Contains(tradeBottle), "Discover ordinary source, direct native printer output and direct own sale-counter bottle.");
        Check(items.Contains(shownOutput) && !items.Contains(looseHidden), "Fixture-root printers may expose output, without sweeping loose hidden/showcase goods.");
        foreach (var item in new[] { inputBottle, otherBottle, unknownBottle })
        { Check(!items.Contains(item), "Do not discover input/power or another machine's apparent output."); Check(!BottlingAccess.CanUseTarget(source, item, out _), "Production access must also reject unsupported equipment locations."); }
        Check(BottlingAccess.CanUseTarget(source, outputBottle, out _), "Actual printer output target must be usable.");
        Check(StorageCatalog.GetSources().All(i => i != printer.output && i != store.backInvinvElement), "Liquid scope must not broaden movable-stock/storage discovery.");
        Check(BottlingCatalog.PrinterOutput(otherMachine.owner) == null, "A different ID cannot borrow the output layout.");
        var malformed = Item("bottle_printer"); Window(malformed, Grid()); Put(store.invElement, malformed);
        Check(BottlingCatalog.PrinterOutput(malformed) == null, "Native ID with wrong window structure must fail closed.");
        var wrapper = Intake(Intake(Grid()));
        Window(malformed, Row(Panel("output", wrapper)));
        Check(BottlingCatalog.PrinterOutput(malformed) == null, "Do not recurse through multiple printer-output wrappers.");
        var detachedOutput = Grid(); var detachedRow = Row(Panel("output", Intake(detachedOutput)));
        Window(malformed, detachedRow); Set(detachedOutput, "parent", null);
        Check(BottlingCatalog.PrinterOutput(malformed) == null, "Visual output alone cannot borrow an unrelated/unlinked inventory.");
        Set(printer.owner, "destroyed", true);
        Check(!BottlingCatalog.GetItems().Contains(outputBottle), "Destroyed printers must not expose live-looking outputs.");
        Set(printer.owner, "destroyed", false);
        var fakeParent = LiquidItem("bottled_water", 750_000); fakeParent.parentInventory = printer.output;
        Check(!BottlingAccess.CanUseTarget(source, fakeParent, out _), "Parent pointer without actual inventory membership is not sufficient.");
        var nestedBox = Item("sec_box"); var nestedGrid = Grid(); Window(nestedBox, nestedGrid); Put(printer.output, nestedBox);
        var nestedBottle = LiquidItem("bottled_water", 750_000); Put(nestedGrid, nestedBottle);
        Check(!BottlingCatalog.GetItems().Contains(nestedBottle) && !BottlingAccess.CanUseTarget(source, nestedBottle, out _), "Never recursively open boxes inside machine outputs.");
        var direct = Printer(wrapped: false); Put(store.invElement, direct.owner);
        var directBottle = LiquidItem("bottled_water", 750_000); Put(direct.output, directBottle);
        Check(BottlingAccess.CanTransfer(source, directBottle, out _), "Legacy direct output remains supported alongside the current native intake.");
    }

    private static void TestInstalledPrinters()
    {
        foreach (string id in new[] { "machine_bay", "machine_bay_ext" })
        {
            var store = Store(); var source = Source(store);
            var bay = Item(id, "CONTAINER_TAG", "SYSTEM_TAG", "SYSTEM_TAG_UTILITY", "PINNED_TAG");
            var machines = Grid(); Window(bay, machines); Put(store.invElement, bay);
            var printer = Printer(); Put(machines, printer.owner);
            var bottles = Enumerable.Range(0, 3).Select(_ => LiquidItem("large_bottled_water", 1_500_000)).ToArray();
            Put(printer.output, bottles);
            Check(printer.output.GetParentItem() == printer.owner && machines.GetParentItem() == bay,
                "Native relink preserves printer and installed-bay ownership.");
            Check(BottlingCatalog.PrinterOutput(printer.owner) == printer.output,
                "Current MachineHelper.Panel wraps the direct output grid in exactly one intake.");
            Check(bottles.All(BottlingCatalog.GetItems().Contains), id + " must discover all three printed bottles.");
            Check(bottles.All(b => BottlingAccess.CanTransfer(source, b, out _)), id + " must permit all three native source/target pairs.");
            Check(StorageCatalog.GetSources().All(g => g != machines && g != printer.output) && !StorageCatalog.IsStorage(bay),
                "Printer-only traversal must not make the machine bay a stow/stock source or target.");
            var loose = LiquidItem("bottled_water", 750_000); Put(machines, loose);
            var other = Printer("water_purifier"); Put(machines, other.owner);
            var otherBottle = LiquidItem("bottled_water", 750_000); Put(other.output, otherBottle);
            var inputBottle = LiquidItem("bottled_water", 750_000); Put(printer.input, inputBottle);
            Check(!BottlingCatalog.GetItems().Contains(loose) && !BottlingCatalog.GetItems().Contains(otherBottle) &&
                !BottlingCatalog.GetItems().Contains(inputBottle), "Bay traversal only opens bottle-printer output, never loose goods, other machines or input.");
            StorageRules.Policy(bay).ContainerLocked = true;
            Check(!BottlingAccess.CanTransfer(source, bottles[0], out _), "An outer machine-bay mod lock still protects printer bottles.");
            StorageRules.Policy(bay).ContainerLocked = false; bay.contentWindow.isLocked = true;
            Check(!BottlingAccess.CanTransfer(source, bottles[0], out _), "An outer native bay window lock remains protective.");
            bay.contentWindow.isLocked = false; Tag(bay, "IS_OWNED_TAG", false);
            Check(!BottlingCatalog.GetItems().Contains(bottles[0]), "Nonowned machine bays cannot expose printer bottles.");
            Tag(bay, "IS_OWNED_TAG"); Set(bay, "destroyed", true);
            Check(!BottlingCatalog.GetItems().Contains(bottles[0]), "Destroyed machine bays cannot expose printer bottles.");
            Set(bay, "destroyed", false); Check(BottlingAccess.CanTransfer(source, bottles[0], out _), "Restoring bay access restores filling without moving the bottles.");
            bay.identifier = "unknown_machine_bay";
            Check(!BottlingCatalog.GetItems().Contains(bottles[0]), "Unknown container cannot borrow installed-printer traversal.");
            bay.identifier = id; Set(machines, "parent", null);
            Check(!BottlingCatalog.GetItems().Contains(bottles[0]), "Unlinked machine-bay inventory cannot borrow a visual owner.");
        }
    }

    private static void TestLocksAndOwnership()
    {
        var store = Store(); var source = Source(store); var printer = Printer(); Put(store.invElement, printer.owner);
        var target = LiquidItem("bottled_water", 750_000); Put(printer.output, target);
        Check(BottlingAccess.CanUseTarget(source, target, out _), "Unlocked owned output baseline.");
        Tag(printer.owner, "IS_OWNED_TAG", false);
        Check(!BottlingCatalog.GetItems().Contains(target) && !BottlingAccess.CanUseTarget(source, target, out _), "Owned bottle inside nonowned printer is excluded and denied.");
        Tag(printer.owner, "IS_OWNED_TAG");
        StorageRules.Policy(printer.owner).ContainerLocked = true;
        Check(!BottlingAccess.CanUseTarget(source, target, out _), "Printer owner mod lock protects output.");
        StorageRules.Policy(printer.owner).ContainerLocked = false;
        printer.owner.contentWindow.isLocked = true;
        Check(!BottlingAccess.CanUseTarget(source, target, out _), "Native printer window lock protects output.");
        printer.owner.contentWindow.isLocked = false;
        foreach (bool insert in new[] { true, false })
        {
            printer.output.overrideLockInsert = insert; printer.output.overrideLockRemove = !insert;
            Check(!BottlingAccess.CanUseTarget(source, target, out _), "Both native inventory seal flags remain protective, despite in-place operation.");
        }
        printer.output.overrideLockInsert = false; printer.output.overrideLockRemove = false;
        Tag(printer.owner, "LOCKED_TAG");
        Check(!BottlingAccess.CanUseTarget(source, target, out _), "Native locked ancestor item protects the output.");
        Tag(printer.owner, "LOCKED_TAG", false);
        foreach (string tag in new[] { "LOCKED_TAG", "ITEM_BEHIND_LOCK_TAG" })
        { Tag(target, tag); Check(!BottlingAccess.CanUseTarget(source, target, out _), tag + " must still deny in-place mutations."); Tag(target, tag, false); }
        var box = Item("sec_box"); var page = Grid(); Window(box, page); Put(store.invElement, box); Put(page, source);
        Remove(store.invElement, source);
        StorageRules.Policy(box).Page(0).SaleLocked = true;
        Check(!BottlingAccess.CanUseSource(source, out _), "Locked storage page must protect pinned source.");
        StorageRules.Policy(box).Page(0).SaleLocked = false;
        Check(BottlingAccess.CanUseSource(source, out _), "Unlocking page restores pinned source permission.");
        StorageRules.Policy(source).ContainerLocked = true;
        Check(!BottlingAccess.CanUseSource(source, out _), "Source's own explicit mod lock remains protective.");
        StorageRules.Policy(source).ContainerLocked = false;
        source.forceDisableUse = true;
        Check(!BottlingAccess.CanUseSource(source, out _), "Pinned source cannot bypass forceDisableUse under nested storage.");
        source.forceDisableUse = false;
        Tag(target, "IS_OWNED_TAG", false);
        Check(!BottlingAccess.CanUseTarget(source, target, out _), "Target ownership must be re-read after enumeration/sale.");
        Tag(target, "IS_OWNED_TAG");
        Check(BottlingAccess.CanTransfer(source, target, out _), "Clearing all protection gates restores actual native pair access.");
        Remove(store.invElement, printer.owner); Put(page, printer.owner);
        StorageRules.Policy(box).Page(0).SaleLocked = true;
        Check(!BottlingAccess.CanUseTarget(source, target, out _), "Ancestor storage-page lock must protect nested printer output.");
        StorageRules.Policy(box).Page(0).SaleLocked = false;
        Check(BottlingAccess.CanUseTarget(source, target, out _), "Unlocking ancestor page restores nested printer output access.");
        box.contentWindow.isLocked = true;
        Check(!BottlingAccess.CanUseTarget(source, target, out _), "Ancestor native window lock must protect nested printer output.");
        box.contentWindow.isLocked = false;
        Tag(box, "IS_OWNED_TAG", false);
        Check(!BottlingAccess.CanUseTarget(source, target, out _), "Deep owned printer/bottle cannot bypass nonowned ancestor box.");
        Tag(box, "IS_OWNED_TAG");
        target.unitCount = 2;
        Check(!BottlingAccess.CanUseTarget(source, target, out _), "Stacked bottles cannot be mutated as a single container.");
        target.unitCount = 1;
        Set(target, "destroyed", true);
        Check(!BottlingAccess.CanUseTarget(source, target, out _), "Destroyed targets must not be touched.");
        Set(target, "destroyed", false);
        Tag(source, "IS_OWNED_TAG", false);
        Check(!BottlingAccess.CanUseSource(source, out _), "Source ownership is required even when pinned.");
        Tag(source, "IS_OWNED_TAG");
        Check(BottlingAccess.CanTransfer(source, target, out _), "Restored nested fixture remains usable without movement.");
    }

    private static void TestTradeAccess()
    {
        var store = Store(); var source = Source(store); var target = LiquidItem("bottled_water", 750_000, "PINNED_TAG");
        Put(store.backInvinvElement, target);
        Check(BottlingCatalog.IsOnTradeCounter(target) && BottlingAccess.CanTransfer(source, target, out _), "Owned pinned sale-counter target is fillable in place.");
        Tag(target, "IS_OWNED_TAG", false);
        Check(!BottlingAccess.CanTransfer(source, target, out _), "Sold or customer bottle left on counter must never be filled.");
        Tag(target, "IS_OWNED_TAG");
        PlayerStore.Instance.isHandlingShootout = true;
        Check(NegociationUIManager.IsTradeLocked(), "Actual native shootout flag locks trade.");
        Check(!BottlingAccess.CanUseTarget(source, target, out string reason) && reason.Contains("交易"), "Trade lock must deny counter liquid change with a clear reason.");
        Check(BottlingAccess.CanUseSource(source, out _), "Trade lock need not deny a source outside the trade area.");
        var ordinary = LiquidItem("bottled_water", 750_000); Put(store.invElement, ordinary);
        Check(BottlingAccess.CanTransfer(source, ordinary, out _), "Non-counter liquid permission remains independent of trade locking.");
        var printer = Printer(); Put(store.backInvinvElement, printer.owner);
        var output = LiquidItem("bottled_water", 750_000); Put(printer.output, output);
        Check(BottlingCatalog.IsOnTradeCounter(output) && !BottlingAccess.CanUseTarget(source, output, out _), "Counter lock also protects nested printer-output valuation.");
        PlayerStore.Instance.isHandlingShootout = false; PlayerStore.Instance.isClientBeingArrested = true;
        Check(!BottlingAccess.CanUseTarget(source, target, out _), "Actual native arrest flag also protects counter liquid state.");
        PlayerStore.Instance.isClientBeingArrested = false;
        Check(BottlingAccess.CanTransfer(source, target, out _), "Trade unlock restores in-place access.");
        Put(store.backInvinvElement, source); Remove(store.invElement, source);
        PlayerStore.Instance.isHandlingShootout = true;
        Check(!BottlingAccess.CanUseSource(source, out _), "A source on the locked sale counter cannot be drained either.");
    }
}

namespace DeepSpaceQoL
{
    // Only unrelated mod logging is substituted. No game predicate/catalogue/lock code is stubbed.
    internal static class ModEntry { public static TestLog? Log => null; }
    internal sealed class TestLog { public void Warning(string message) => throw new InvalidOperationException(message); }
}
