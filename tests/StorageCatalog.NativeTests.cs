using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using DeepSpaceQoL;

// The installed game's real types/methods execute on data-only native fixtures.
// No Unity/native constructors, scene initialization, inventory actions or patches run.
string managed = args.Length == 1 ? Path.GetFullPath(args[0]) : @"D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed";
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string path = Path.Combine(managed, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
NativeStorageTests.Run();

static class NativeStorageTests
{
    private static int checks;
    private static readonly string[] Cabinets = {
        "makeshift_storage_bay", "storage_bay", "storage_bay_large", "storage_bay_gun", "storage_bay_chem",
        "smuggler_bay_mini", "smuggler_bay", "smuggler_bay_mod", "hypertech_bay"
    };
    private static readonly string[] BagsAndBoxes = {
        "fanny_pack", "satchel", "kids_backpack", "kids_backpack_large", "backpack_small",
        "backpack_medium", "backpack_medium_military", "backpack_large", "backpack_large_military",
        "sec_box", "evidence_box", "lou_locker", "med_box", "eng_box", "service_box", "beerCase",
        "lootbox_small", "lootbox_medium", "lootbox_large", "safe", "large_storage_bay",
        "pouch_general", "pouch_general_large", "firstaid_box"
    };
    private static readonly string[] Functional = {
        "machine_bay", "machine_bay_ext", "toolbox", "intake_hopper", "outlet_hopper",
        "nutrifruit_seed_packet", "dossier", "trashcan", "water_test_container", "water_tablet_container",
        "nutrient_tablet_container", "save_bag", "back_inv_save_bag", "hidden_save_bag", "showcase_save_bag",
        "sold_save_bag", "storage_bay_expansion_kit", "technician_backpack", "backpack_small_modular",
        "backpack_medium_modular", "backpack_large_modular", "water_purifier", "automatic_meat_processor",
        "unknown_mod_container"
    };

    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException(message); }

    private static T Native<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static void Set(object target, string name, object? value)
    {
        for (Type? type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                ?? type.GetField("<" + name + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null) continue;
            field.SetValue(target, value);
            return;
        }
        throw new MissingFieldException(target.GetType().FullName, name);
    }

    private static GameItem Item(string id, params string[] tags)
    {
        var item = Native<GameItemElement>();
        item.identifier = id;
        item.unitCount = 1;
        item.itemTypes = tags.ToList();
        item.itemFeatures = new();
        item.state = new TagSystem();
        item.modifiedState = item.state;
        foreach (string tag in tags.Append("IS_OWNED_TAG"))
            item.state.dict[tag] = new TagState(tag, tag) { valueEnabled = true };
        return item;
    }

    private static GameGridInventory Grid()
    {
        var grid = Native<GameGridInventory>();
        Set(grid, "items", new List<GameItem>());
        return grid;
    }

    private static GameItem Alive(GameItem item)
    {
        // Native IsDestroyed only compares handler against Unity null. Give that
        // data-only comparison a nonzero cached pointer; never dereference it or call a Unity engine API.
        var handler = Native<TreeNodeRender>();
        Set(handler, "m_CachedPtr", new IntPtr(1));
        Set(item, "handler", handler);
        return item;
    }

    private static PixelWindow Window(GameItem owner, PixelElement element)
    {
        var window = Native<PixelWindow>();
        Set(window, "childElement", element);
        Set(window, "parentItems", new List<GameItem> { owner });
        Set(owner, "contentWindow", window);
        if (element is GameInventory inventory) Set(inventory, "parent", window);
        if (element is MachineryPanelRow row) row.parent = window;
        return window;
    }

    private static GameItem Direct(string id, params string[] tags)
    {
        var item = Item(id, tags);
        Window(item, Grid());
        return item;
    }

    private static MachineryIntake Intake(PixelElement element)
    {
        var intake = Native<MachineryIntake>();
        Set(intake, "content", element);
        return intake;
    }

    private static MachineryTabs Tabs(params PixelElement[] elements)
    {
        var tabs = Native<MachineryTabs>();
        var field = typeof(MachineryTabs).GetField("tabs", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var list = (IList)Activator.CreateInstance(field.FieldType)!;
        Type tabType = field.FieldType.GetGenericArguments()[0];
        for (int i = 0; i < elements.Length; i++)
        {
            var tab = RuntimeHelpers.GetUninitializedObject(tabType);
            Set(tab, "page", elements[i]);
            Set(tab, "caption", "Native " + i);
            list.Add(tab);
        }
        field.SetValue(tabs, list);
        return tabs;
    }

    private static MachineryPanel Panel(string role, PixelElement element)
    {
        var panel = Native<MachineryPanel>();
        panel.role = role;
        Set(panel, "content", element);
        return panel;
    }

    private static MachineryPanelRow Row(params MachineryPanel[] panels)
    {
        var row = Native<MachineryPanelRow>();
        Set(row, "panels", panels.ToList());
        return row;
    }

    private static GameItem Tabbed(string id, params GameGridInventory[] grids)
    {
        var owner = Item(id, "STORAGE", "CONTAINER_TAG", "SYSTEM_TAG");
        var row = Row(Panel("storage", Tabs(grids.Select(g => (PixelElement)Intake(g)).ToArray())));
        Window(owner, row);
        // Native machinery relinking attaches page storage to the row, not the visual wrappers.
        foreach (var grid in grids) Set(grid, "parent", row);
        return owner;
    }

    private static void Put(GameGridInventory grid, params GameItem[] items)
    {
        var list = (List<GameItem>)typeof(GameGridInventory).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(grid)!;
        foreach (var item in items) { list.Add(item); item.parentInventory = grid; }
    }

    private static void SetEntry(EmporiumEntry? entry) => typeof(EmporiumEntry)
        .GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, entry);

    internal static void Run()
    {
        TestIdentities();
        TestStructures();
        TestDiscoveryAndLocks();
        TestMovePredicate();
        TestStowSourcesTargetsAndPackages();
        SetEntry(null);
        StorageRules.ClearCache();
        Console.WriteLine($"Storage catalog native tests passed: {checks} checks. Real Assembly-CSharp; offline data-only fixtures; no live inventory actions/Unity scene calls.");
    }

    private static void TestIdentities()
    {
        foreach (string id in Cabinets.Concat(BagsAndBoxes))
        {
            var owner = Direct(id, "STORAGE", "CONTAINER_TAG");
            Check(StorageCatalog.IsStorage(owner), id + " should expose its direct native storage grid.");
            var pages = StorageCatalog.PagesOf(owner);
            Check(pages.Count == 1 && pages[0].Index == 0 && pages[0].Owner == owner, id + " page identity/index must be stable.");
            Check(StorageCatalog.Find(pages[0].Inventory)?.Owner == owner, id + " grid must resolve back to its real owner.");
        }
        var beer = Direct("beerCase", "PRE_CONTAINER_TAG", "BREVAGE");
        Check(StorageCatalog.IsStorage(beer), "Real beer box needs explicit support even without CONTAINER_TAG/STORAGE.");
        foreach (string id in Functional)
        {
            // Deliberately give the broad tags that caused the old false positive.
            var owner = Direct(id, "STORAGE", "CONTAINER_TAG", "PRE_CONTAINER_TAG", "BACKPACK_TAG");
            Check(!StorageCatalog.IsStorage(owner), id + " must not become ordinary storage through broad tags.");
            Check(StorageCatalog.PagesOf(owner).Count == 0 && StorageCatalog.TabsOf(owner) == null, id + " must not expose functional inventories.");
            Check(StorageCatalog.Find((GameGridInventory)owner.contentWindow.childElement) == null, id + " inventory must not get a storage-page rule.");
        }
        var missingWindow = Item("storage_bay", "STORAGE");
        Check(!StorageCatalog.IsStorage(missingWindow), "Known ID without its real storage window is not storage.");
        var machineMasquerade = Direct("storage_bay", "MACHINE", "STORAGE");
        Check(!StorageCatalog.IsStorage(machineMasquerade), "Machine type must override a known ID.");
    }

    private static void TestStructures()
    {
        foreach (string id in new[] { "storage_bay", "storage_bay_large", "storage_bay_gun", "storage_bay_chem", "hypertech_bay" })
        {
            var grids = new[] { Grid(), Grid(), Grid() };
            var owner = Tabbed(id, grids);
            var pages = StorageCatalog.PagesOf(owner);
            Check(StorageCatalog.IsStorage(owner) && StorageCatalog.TabsOf(owner)?.count == 3, id + " native storage tabs must work.");
            Check(pages.Select(p => p.Index).SequenceEqual(new[] { 0, 1, 2 }), id + " native tab indices must stay stable.");
            Check(pages.Select(p => p.Inventory).SequenceEqual(grids), id + " wrapped tabs must return the actual grids.");
        }
        foreach (string id in new[] { "machine_bay", "machine_bay_ext", "unknown_machine", "backpack_small" })
        {
            var owner = Tabbed(id, Grid(), Grid());
            Check(!StorageCatalog.IsStorage(owner) && StorageCatalog.PagesOf(owner).Count == 0, id + " cannot borrow cabinet tab traversal.");
        }
        var first = Grid(); var second = Grid(); var output = Grid();
        var known = Item("storage_bay", "STORAGE");
        Window(known, Row(Panel("output", output), Panel("storage", Tabs(first, Native<GameSlotInventory>(), Intake(second), first))));
        var found = StorageCatalog.PagesOf(known);
        Check(found.Count == 2 && found[0].Index == 0 && found[1].Index == 2, "Skip functional tab slots, deduplicate grids, preserve original indices.");
        Check(found[0].Inventory == first && found[1].Inventory == second && found.All(p => p.Inventory != output), "Never traverse an output panel or unrelated machine grid.");
        Window(known, Row(Panel("output", Tabs(Intake(first)))));
        Check(!StorageCatalog.IsStorage(known), "A known cabinet with no storage-role panel must not expose other panels.");
        Window(known, Row(Panel("storage", Grid())));
        Check(!StorageCatalog.IsStorage(known), "Unexpected storage panel structure must fail closed.");
        Window(known, Intake(Grid()));
        Check(!StorageCatalog.IsStorage(known), "Do not search arbitrary visual wrappers recursively.");
        Window(known, Native<GameSlotInventory>());
        Check(!StorageCatalog.IsStorage(known), "A functional slot is not a free arrangement grid.");
    }

    private static void TestDiscoveryAndLocks()
    {
        StorageRules.ClearCache();
        var entry = Native<EmporiumEntry>();
        entry.invElement = Grid();
        foreach (string property in new[] { "frontInvinvElement", "backInvinvElementCounter", "backInvinvElement", "hiddenElement", "showcaseElement" })
            Set(entry, property, Grid());
        SetEntry(entry);
        var left = Grid(); var right = Grid();
        var cabinet = Tabbed("storage_bay", left, right);
        var nested = Direct("backpack_small", "BACKPACK_TAG");
        var nestedGrid = (GameGridInventory)nested.contentWindow.childElement;
        var nestedGoods = Item("nested_food", "FOOD");
        Put(nestedGrid, nestedGoods); Put(left, nested); Put(right, Item("right_food", "FOOD"));
        var machine = Direct("machine_bay", "STORAGE", "CONTAINER_TAG");
        var machineGrid = (GameGridInventory)machine.contentWindow.childElement;
        var hiddenInMachine = Direct("sec_box", "CONTAINER_TAG");
        Put(machineGrid, hiddenInMachine, Item("machine_output", "FOOD"));
        var functional = Direct("trashcan", "CONTAINER_TAG");
        var unowned = Direct("sec_box", "CONTAINER_TAG");
        unowned.modifiedState.dict.Remove("IS_OWNED_TAG");
        var loose = Item("loose_food", "FOOD");
        Put(entry.invElement, cabinet, machine, functional, unowned, loose);
        var hiddenBox = Direct("med_box", "CONTAINER_TAG");
        var shownBox = Direct("eng_box", "CONTAINER_TAG");
        Put(entry.hiddenElement, hiddenBox, Item("loose_hidden", "FOOD"));
        Put(entry.showcaseElement, shownBox, Item("loose_shown", "FOOD"));
        var pages = StorageCatalog.GetPages();
        Check(pages.Count == 8, "Only three owned root stock areas and five genuine storage pages should be discovered.");
        Check(pages.Any(p => p.Owner == cabinet && p.Inventory == left) && pages.Any(p => p.Owner == nested), "Traverse genuine nested storage.");
        Check(pages.All(p => p.Inventory != machineGrid && p.Owner != hiddenInMachine && p.Owner != functional && p.Owner != unowned), "Never descend machines/functional containers or unowned boxes.");
        Check(pages.Any(p => p.Owner == hiddenBox) && pages.Any(p => p.Owner == shownBox), "Discover owned boxes in hidden/showcase locations.");
        var sources = StorageCatalog.GetSources();
        Check(sources.Contains(entry.invElement), "Main inventory remains an automatic loose-goods source.");
        Check(sources.Contains(entry.frontInvinvElement) && sources.Contains(entry.backInvinvElementCounter), "Owned front/work counters remain stock roots.");
        Check(!sources.Contains(entry.backInvinvElement) && !sources.Contains(entry.hiddenElement) && !sources.Contains(entry.showcaseElement), "Trade offer and loose hidden/showcase goods must not enter normal sources.");
        var targets = StorageCatalog.GetStoragePages();
        Check(targets.Count == 5 && targets.All(p => p.Owner != null), "Storage-only API must exclude root stock areas.");
        Check(!StorageRules.IsLocked(nestedGrid), "Unlocked nested storage stays available.");
        StorageRules.Policy(cabinet).Page(0).SaleLocked = true;
        Check(StorageRules.IsLocked(left) && StorageRules.IsLocked(nestedGrid), "Locking a native tab protects its nested boxes.");
        Check(!StorageRules.IsLocked(right) && !StorageRules.IsLocked(entry.invElement), "Tab lock must not spread to siblings/main inventory.");
        sources = StorageCatalog.GetSources();
        Check(!sources.Contains(left) && !sources.Contains(nestedGrid) && sources.Contains(right) && sources.Contains(entry.invElement), "Automatic sources must honor ancestral page locks without losing main inventory.");
        StorageRules.Policy(cabinet).Page(0).SaleLocked = false;
        StorageRules.Policy(cabinet).ContainerLocked = true;
        Check(StorageRules.IsLocked(left) && StorageRules.IsLocked(right) && StorageRules.IsLocked(nestedGrid), "Container lock protects every tab and nested box.");
        StorageRules.Policy(cabinet).ContainerLocked = false;
        StorageRules.Policy(nested).ContainerLocked = true;
        Check(StorageRules.IsLocked(nestedGrid) && !StorageRules.IsLocked(left), "Nested box lock must not lock its parent's other goods.");
        StorageRules.Policy(nested).ContainerLocked = false;
        cabinet.contentWindow.isLocked = true;
        Check(StorageCatalog.IsNativeAccessLocked(left) && StorageCatalog.IsNativeAccessLocked(nestedGrid), "Native locked window protects all child pages and nested boxes.");
        sources = StorageCatalog.GetSources();
        Check(!sources.Contains(left) && !sources.Contains(right) && !sources.Contains(nestedGrid) && sources.Contains(entry.invElement), "Native lock must block auto sources without consuming the root source.");
        cabinet.contentWindow.isLocked = false;
        left.overrideLockRemove = true;
        Check(StorageCatalog.IsNativeAccessLocked(left), "Native remove lock must block automation.");
        Check(StorageCatalog.IsNativeAccessLocked(nestedGrid) && !StorageCatalog.IsNativeAccessLocked(right), "Ancestor native remove lock protects nested storage without locking sibling tabs.");
        left.overrideLockRemove = false;
        left.overrideLockInsert = true;
        Check(StorageCatalog.IsNativeAccessLocked(left), "Native insert lock must block automatic stowing/arrangement.");
        Check(StorageCatalog.IsNativeAccessLocked(nestedGrid), "Ancestor native insert lock protects nested storage too.");
        left.overrideLockInsert = false;
        cabinet.modifiedState.dict["LOCKED_CONTAINER_TAG"] = new TagState("LOCKED_CONTAINER_TAG", "LOCKED_CONTAINER_TAG") { valueEnabled = true };
        cabinet.modifiedState.dict["LOCK_STATUS"] = new TagState("LOCK_STATUS", "LOCK_STATUS") { valueEnabled = true, valueBool = true };
        Check(StorageCatalog.IsNativeAccessLocked(left) && StorageCatalog.IsNativeAccessLocked(nestedGrid), "Native LOCK_STATUS protects children even before the locked window is restored.");
        cabinet.modifiedState.dict["LOCK_STATUS"].valueBool = false;
        Check(!StorageCatalog.IsNativeAccessLocked(left) && !StorageCatalog.IsNativeAccessLocked(nestedGrid), "An opened native box must become usable again.");
        Check(StorageCatalog.GetSources().Contains(nestedGrid), "Clearing native/mod locks restores nested sources.");
        SetEntry(null);
        Check(StorageCatalog.GetPages().Count == 0 && StorageCatalog.GetStoragePages().Count == 0 && StorageCatalog.GetSources().Count == 0, "No store scene means no inventories.");
    }

    private static void TestMovePredicate()
    {
        foreach (string id in Cabinets.Concat(BagsAndBoxes).Concat(Functional))
            Check(!StowService.IsMovableStock(Direct(id, "CONTAINER_TAG")), id + " shell/equipment must not be automatically relocated.");
        foreach (string tag in new[] { "MACHINE", "STORAGE", "SYSTEM_TAG", "SYSTEM_TAG_UTILITY", "NESTABLE_DEVICE_TAG", "CONTAINER_TAG", "BACKPACK_TAG", "PRE_CONTAINER_TAG", "ITEM_BEHIND_LOCK_TAG" })
            Check(!StowService.IsMovableStock(Item("test_device", tag)), tag + " must prevent relocating equipment even without a window.");
        foreach (string tag in new[] { "machinery", "machinery_drawn_power", "BROKEN_MACHINE_TAG" })
        {
            var device = Item("test_machine", tag);
            device.itemTypes.Clear();
            Check(!StowService.IsMovableStock(device), tag + " must protect a device even if its MACHINE type is missing.");
        }
        foreach (string tag in new[] { "FOOD", "WATER", "LIQUID_CONTAINER_TAG", "TOOL", "MEDICAL", "HOUSEHOLD_GOOD", "ENGINEERING_SUPPLIES", "MODULE", "MODULE_TAG", "SHIP_MODULE_TAG" })
            Check(StowService.IsMovableStock(Item("loose_goods", tag)), tag + " loose goods must remain eligible, including normal water bottles/tools.");
        var character = Native<GameCharacterItem>();
        Check(!StowService.IsMovableStock(character), "Characters must never be stowed as goods.");
        Check(!StowService.IsMovableStock(null!), "Null is never movable stock.");
    }

    private static void TestStowSourcesTargetsAndPackages()
    {
        StorageRules.ClearCache();
        var entry = Native<EmporiumEntry>();
        entry.invElement = Grid();
        Set(entry, "frontInvinvElement", Grid());
        Set(entry, "backInvinvElement", Grid());
        SetEntry(entry);
        var first = Grid(); var second = Grid();
        var cabinet = Tabbed("storage_bay_large", first, second);
        Put(entry.invElement, cabinet);
        var bottle = Alive(Item("bottled_water", "WATER", "LIQUID_CONTAINER_TAG"));
        var pinned = Alive(Item("small_bottled_water", "WATER", "LIQUID_CONTAINER_TAG", "PINNED_TAG"));
        var blocked = Alive(Item("water_jug", "WATER", "LIQUID_CONTAINER_TAG"));
        blocked.mayInventoryRemoveItemFunc = (_, _) => false;
        var unowned = Alive(Item("large_bottled_water", "WATER", "LIQUID_CONTAINER_TAG"));
        unowned.modifiedState.dict.Remove("IS_OWNED_TAG");
        var testKit = Alive(Direct("water_test_container", "HOUSEHOLD_GOOD", "WATER_PURIFICATION_SUPPLY", "PRE_CONTAINER_TAG", "CONTAINER_TOOLTIP_TAG"));
        var tablets = Alive(Direct("water_tablet_container", "HOUSEHOLD_GOOD", "WATER_PURIFICATION_SUPPLY", "PRE_CONTAINER_TAG", "CONTAINER_TOOLTIP_TAG"));
        testKit.uniqueId = 7984; tablets.uniqueId = 7995;
        var kitGrid = (GameGridInventory)testKit.contentWindow.childElement;
        var tabletGrid = (GameGridInventory)tablets.contentWindow.childElement;
        var strip = Alive(Item("water_test_strip", "HOUSEHOLD_GOOD", "WATER_PURIFICATION_SUPPLY", "WATER_TESTING_STRIP_TAG"));
        Put(kitGrid, strip);
        Put(tabletGrid, Alive(Item("water_tablet", "HOUSEHOLD_GOOD", "WATER_PURIFICATION_SUPPLY")));
        Put(entry.invElement, bottle, pinned, blocked, unowned, testKit, tablets);
        first.identifier = "storage_native_tab_0";
        var page0 = StorageCatalog.PagesOf(cabinet)[0];
        page0.Rule.Categories = "tag:FILTER,tag:WATER,tag:WATER_PURIFICATION_SUPPLY";
        page0.Rule.Restrict = true;

        Check(!((GameItemElement)bottle).IsDestroyed(), "Native real item liveness check must pass for the data-only handler fixture.");
        Check(bottle.MayRemove() && StorageRules.CanAutomate(bottle), "An owned unpinned native bottle in main inventory must pass real CanAutomate/MayRemove.");
        Check(StorageRules.Matches(page0.Rule, bottle) && StorageRules.Allows(first, bottle), "Water category matches native nonempty bottled water.");
        Check(testKit.MayRemove() && tablets.MayRemove(), "Screenshot consumable packages must pass native removal rules.");
        Check(StorageRules.Matches(page0.Rule, testKit) && StorageRules.Matches(page0.Rule, tablets), "Screenshot water-page categories match the actual native testing/tablet package types.");
        Check(StowService.IsPortablePackage(testKit) && StowService.IsMovableStock(testKit) && StowService.IsMovableStock(tablets), "Audited filled portable packages must be movable as whole goods.");
        Check(!StorageCatalog.IsStorage(testKit) && !StorageCatalog.IsStorage(tablets), "Movable package shells must not become storage pages.");
        var pages = StorageCatalog.GetPages();
        Check(pages.All(p => p.Inventory != kitGrid && p.Inventory != tabletGrid), "Package contents must never be scanned as loose storage sources.");
        var scan = StowService.ScanSources(pages);
        Check(scan.Candidates.Contains(bottle) && scan.Candidates.Contains(testKit) && scan.Candidates.Contains(tablets), "Real stow source pipeline must include ordinary water and both screenshot packages.");
        Check(!scan.Candidates.Contains(strip), "Keep a filled test-kit package intact; do not extract its internal strips.");
        Check(scan.Pinned == 1 && scan.Unowned == 1 && scan.Restricted == 1 && scan.Equipment == 1, "Source diagnostics must report native pinned/unowned/removal restrictions and cabinet shells.");
        Check(scan.Summary.Contains("固定 1") && scan.Summary.Contains("非自有 1") && scan.Summary.Contains("原生不可移 1"), "Zero-action reports must retain specific source skip reasons.");
        var offered = Alive(Item("bottled_water", "WATER", "LIQUID_CONTAINER_TAG"));
        Put(entry.backInvinvElement, offered);
        Check(!StowService.ScanSources(pages).Candidates.Contains(offered), "Normal stowing must never silently reclaim the current trade offer.");
        Check(StowService.ScanSources(pages, reclaimOffer: true).Candidates.Contains(offered), "Explicit offer reclamation can include its owned loose goods.");
        entry.backInvinvElement.overrideLockRemove = true;
        Check(!StowService.ScanSources(pages, reclaimOffer: true).Candidates.Contains(offered), "Explicit reclaim must still honor source locks.");
        entry.backInvinvElement.overrideLockRemove = false;

        var stored = Alive(Item("mini_bottle", "WATER", "LIQUID_CONTAINER_TAG"));
        Put(first, stored);
        page0.Rule.SaleLocked = true;
        Check(!StorageRules.CanAutoAccess(first) && StorageRules.CanAutoAccess(second), "Mod page lock must block one-click access in both directions without locking siblings.");
        Check(!StowService.AutomaticTargets(pages).Any(p => p.Inventory == first) && StowService.AutomaticTargets(pages).Any(p => p.Inventory == second), "Locked page cannot be an automatic destination or automatic arrangement page.");
        Check(StorageRules.Allows(first, bottle), "A mod automation lock must not disable explicit manual placement into matching categories.");
        scan = StowService.ScanSources(pages);
        Check(!scan.Candidates.Contains(stored) && scan.Locked >= 1 && scan.Candidates.Contains(bottle), "Source scan must count and exclude locked contents while keeping main stock available.");
        Check(!StorageCatalog.GetSources().Contains(first), "Shared automatic source API must exclude a locked target/source page.");
        page0.Rule.SaleLocked = false;
        StorageRules.Policy(cabinet).ContainerLocked = true;
        Check(StowService.AutomaticTargets(pages).Length == 0, "Whole-cabinet lock must remove every automatic destination/arrangement page.");
        Check(StowService.Run().Contains("解除目标页或整箱锁"), "All-locked destinations must give an actionable status before any movement or arrangement.");
        StorageRules.Policy(cabinet).ContainerLocked = false;
        Check(StowService.AutomaticTargets(pages).Length == 2, "Unlocking a cabinet restores its automatic targets.");

        kitGrid.overrideLockRemove = true;
        Check(!StowService.IsMovableStock(testKit) && StorageRules.HasProtectedContents(testKit), "Native lock inside a portable package must prevent moving the package shell.");
        kitGrid.overrideLockRemove = false;
        StorageRules.Policy(strip).ContainerLocked = true;
        Check(!StowService.IsMovableStock(testKit), "A protected contained item must prevent moving its whole package.");
        scan = StowService.ScanSources(pages);
        Check(!scan.Candidates.Contains(testKit) && scan.Locked == 1, "Source diagnostics must classify protected package contents as locked.");
        StorageRules.Policy(strip).ContainerLocked = false;
        testKit.contentWindow.isLocked = true;
        Check(!StowService.IsMovableStock(testKit), "Native sealed package window must remain immovable.");
        testKit.contentWindow.isLocked = false;
        Check(StowService.IsMovableStock(testKit), "Clearing all package-content locks restores whole-package stowing.");
        testKit.itemTypes.Add("MACHINE");
        Check(!StowService.IsMovableStock(testKit), "Audited package ID cannot bypass device exclusions.");
        testKit.itemTypes.Remove("MACHINE");
        var unknownPackage = Alive(Direct("unknown_package", "PRE_CONTAINER_TAG", "WATER_PURIFICATION_SUPPLY"));
        Check(!StowService.IsMovableStock(unknownPackage), "Unknown PRE-container shapes remain excluded until audited.");
        foreach (var package in new[] {
            Direct("nutrient_tablet_container", "HOUSEHOLD_GOOD", "HYDROPONIC_SUPPLY", "PRE_CONTAINER_TAG"),
            Direct("nutrifruit_seed_packet", "SEED", "CONTAINER_TAG", "STORABLE_PACKAGE_TAG") })
        {
            Check(StowService.IsMovableStock(package), package.identifier + " audited portable package should move whole.");
            Check(!StorageCatalog.IsStorage(package), package.identifier + " package contents must not become ordinary storage.");
        }
        var wrongShape = Item("water_tablet_container", "PRE_CONTAINER_TAG", "WATER_PURIFICATION_SUPPLY");
        Window(wrongShape, Native<GameSlotInventory>());
        Check(!StowService.IsMovableStock(wrongShape), "A portable package ID must still have the audited direct-grid structure.");
        var emptyBottle = Alive(Item("bottled_water", "HOUSEHOLD_GOOD", "LIQUID_CONTAINER_TAG"));
        Check(StowService.IsMovableStock(emptyBottle) && !StorageRules.Matches(new DeepSpaceQoL.Core.PageRule { Categories = "tag:WATER" }, emptyBottle), "Native empty bottles remain movable HOUSEHOLD_GOOD; water-only labels must not fabricate WATER type.");

        var sortEntry = Native<EmporiumEntry>(); sortEntry.invElement = Grid(); SetEntry(sortEntry);
        var lockedSort = Grid(); var unlockedSort = Grid();
        var sortCabinet = Tabbed("storage_bay_large", lockedSort, unlockedSort);
        Put(sortEntry.invElement, sortCabinet);
        StorageRules.Policy(sortCabinet).Page(0).SaleLocked = true;
        ArrangeService.Capture = new();
        try
        {
            string status = StowService.Run();
            Check(ArrangeService.Capture.SequenceEqual(new[] { unlockedSort }), "Actual automatic arrangement loop must visit only unlocked storage targets, never root/locked pages.");
            Check(status.Contains("锁定") && status.Contains("已归位"), "Actual zero-move status must expose skip reasons and already-stored accounting.");
        }
        finally { ArrangeService.Capture = null; }
        SetEntry(null);
    }
}

namespace DeepSpaceQoL
{
    // Only unrelated mod integration surfaces are substituted. All game objects,
    // storage discovery and nested-lock/policy code are the real linked implementation.
    internal static class ArrangeService
    {
        public static List<GameGridInventory>? Capture;
        public static bool TryArrange(GameGridInventory inventory, out string status)
        {
            status = "";
            if (Capture == null) throw new InvalidOperationException("Offline tests must never perform native arrangement.");
            Capture.Add(inventory);
            return false;
        }
    }
    internal static class ModEntry { public static TestLog? Log => null; }
    internal sealed class TestLog { public void Warning(string message) => throw new InvalidOperationException(message); }
}
