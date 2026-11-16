using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using HarmonyLib;

string managed = args.Length > 0 ? Path.GetFullPath(args[0]) : @"D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed";
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string path = Path.Combine(managed, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
GunStockTests.Run();

static class GunStockTests
{
    private const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string PolicyTag = "DEEPSPACE_QOL_STORAGE_V1";
    private static readonly Assembly Mod = typeof(DeepSpaceQoL.ModEntry).Assembly;
    private static readonly Type Service = Mod.GetType("DeepSpaceQoL.GunOrderStockingService", true)!;
    private static readonly string[] CurrentTags = { "GUN_CURRENT_FIREPOWER_TAG", "GUN_CURRENT_STYLE_TAG", "GUN_CURRENT_RELIABILITY_TAG", "GUN_CURRENT_RECOIL_TAG", "GUN_CURRENT_ERGONOMICS_TAG", "GUN_CURRENT_ACCURACY_TAG" };
    private static int checks;
    private static T Native<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static void Check(bool condition, string reason) { checks++; if (!condition) throw new InvalidOperationException(reason); }
    private static void Field(object target, string name, object? value)
    {
        for (Type? type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, All | BindingFlags.DeclaredOnly) ?? type.GetField("<" + name + ">k__BackingField", All | BindingFlags.DeclaredOnly);
            if (field != null) { field.SetValue(target, value); return; }
        }
        throw new MissingFieldException(target.GetType().FullName, name);
    }
    private static object? Call(string name, params object?[] arguments)
    {
        try { return Service.GetMethod(name, All)!.Invoke(null, arguments); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static bool Matches(StoreClient client, GameItem gun) => (bool)Call("MatchesOrder", client, gun)!;
    private static bool Candidate(StoreClient client, GameItem gun, GameInventory source) => (bool)Call("IsCandidate", client, gun, source)!;
    private static void Set(GameItem item, string tag, int value = 0)
    { item.state.dict[tag] = new TagState(tag, tag) { valueEnabled = true, valueInt = value }; item.modifiedState = item.state; }
    private static void Clear(GameItem item, string tag) { item.state.dict.Remove(tag); item.modifiedState = item.state; }
    private static void Policy(GameItem owner, string json)
    { Set(owner, PolicyTag); owner.state.dict[PolicyTag].valueString = json; }
    private static StockFixtureItem Item(string id, params string[] tags)
    {
        var item = new StockFixtureItem(id) { itemFeatures = new(), itemTypes = new(), state = new TagSystem(), unitCount = 1 };
        item.shape = new GridShapeBuilder(4, 2).SetDataFill(1); item.modifiedShape = item.shape.Clone();
        foreach (string tag in tags.Append("IS_OWNED_TAG")) Set(item, tag);
        return item;
    }
    private static StockFixtureItem Gun(string id = "smg")
    {
        var gun = Item(id, "ADV_GUN_TAG", "FIREARM", "WEAPON");
        foreach (string tag in CurrentTags) Set(gun, tag, 50);
        Set(gun, "GUN_CALIBER_TAG", 10); Set(gun, "GUN_SIZE_TAG", 8); Set(gun, "GUN_SUPPRESSED_TAG", 2);
        gun.itemTypes.AddRange(new[] { "FIREARM", "WEAPON" }); return gun;
    }
    private static StoreClient Client() => new()
    {
        clientIntent = StoreClient.ClientIntent.GUNSMITH, gunOrderData = new GunOrderData(), clientFaction = "NONE", tag = new(),
        clientBuyingTagList = new(), clientBuyingIdList = new(), clientBlackTagList = new(), clientBlackIdList = new(), varietyTrackingList = new(),
        useClientBudget = false, clientCash = 0
    };
    private static GameGridInventory Grid()
    {
        var grid = Native<GameGridInventory>(); Field(grid, "items", new List<GameItem>());
        Field(grid, "inventoryShape", new GridShapeBuilder(24, 20).SetDataFill(0)); return grid;
    }
    private static void Put(GameGridInventory grid, GameItem item)
    { ((List<GameItem>)typeof(GameGridInventory).GetField("items", All)!.GetValue(grid)!).Add(item); item.parentInventory = grid; }
    private static PixelWindow Window(StockFixtureItem owner, PixelElement element)
    {
        var window = Native<PixelWindow>(); Field(window, "parentItems", new List<GameItem> { owner }); Field(window, "childElement", element);
        Field(owner, "contentWindow", window); owner.Nodes.Add(window);
        if (element is GameInventory inventory) Field(inventory, "parent", window);
        if (element is MachineryPanelRow row) row.parent = window;
        return window;
    }
    private static StockFixtureItem Attach(StockFixtureItem gun, string id = "rds")
    {
        var part = Item(id, "WEAPON_MOD_TAG", "GUN_MOD"); part.itemTypes.Add("GUN_MOD");
        var slot = Native<GameSlotInventory>(); Field(slot, "currentItem", part); part.parentInventory = slot;
        Window(gun, slot); return part;
    }
    private static EmporiumEntry Entry()
    {
        var entry = Native<EmporiumEntry>(); Field(entry, "m_CachedPtr", new IntPtr(1)); entry.invElement = Grid();
        Field(entry, "frontInvinvElement", Grid()); Field(entry, "backInvinvElement", Grid()); Field(entry, "backInvinvElementCounter", Grid());
        typeof(EmporiumEntry).GetField("<Instance>k__BackingField", All)!.SetValue(null, entry); return entry;
    }
    internal static void Run()
    {
        ((List<LoadedMod>)typeof(ModLoader).GetField("mods", All)!.GetValue(null)!).Clear();
        Entry(); NativeRequirements(); NativeCandidateProtection(); StorageProtection(); ExpansionKit(); CompiledDispatchAndStage();
        Console.WriteLine($"Gun-order stocking native checks passed: {checks}. Real native requirement, ownership and storage predicates executed on data-only fixtures; compiled dispatch/final acceptance/rollback/UI-refresh calls inspected. No Unity rendering, item transfers, save files or live game actions executed.");
    }
    private static void NativeRequirements()
    {
        var client = Client(); var gun = Gun();
        client.gunOrderData.mandatoryRequirements.Add(new GunOrderRequirement { id = CurrentTags[0], value = 37 });
        foreach (int firepower in new[] { 36, 37, 38 })
        {
            Set(gun, CurrentTags[0], firepower);
            Check(Matches(client, gun) == (firepower > 37), "real strict firepower37 threshold: " + firepower);
        }
        Check(!client.IsClientBuyingThisItem(gun) && !client.useClientBudget && Matches(client, gun), "commission uses actual requirements despite empty general interests and no shopping budget");
        client.clientBuyingTagList.Add("FOOD"); client.clientBlackTagList.Add("FIREARM");
        Check(Matches(client, gun), "ordinary buy/black lists do not replace dedicated commission requirements");
        foreach (string tag in CurrentTags.Concat(new[] { "GUN_SUPPRESSED_TAG", "GUN_SIZE_TAG" })) foreach (bool larger in new[] { false, true })
        {
            client.gunOrderData.mandatoryRequirements = new() { new GunOrderRequirement { id = tag, value = 20, isMustBeLargerThan = larger } };
            foreach (int value in new[] { 19, 20, 21 })
            {
                Set(gun, tag, value);
                bool expected = client.gunOrderData.mandatoryRequirements[0].IsMet(gun);
                Check(Matches(client, gun) == expected, $"production and native mandatory predicate agree {tag}/{larger}/{value}");
            }
        }
        foreach (int actual in new[] { 10, 12, 22, 45 }) foreach (int requested in new[] { 10, 12, 22, 45 })
        {
            client.gunOrderData.mandatoryRequirements = new() { new GunOrderRequirement { id = "GUN_CALIBER_TAG", value = requested, isCaliber = true } };
            Set(gun, "GUN_CALIBER_TAG", actual); Check(Matches(client, gun) == (actual == requested), "all native calibers match exactly");
        }
        foreach (string tag in CurrentTags) Set(gun, tag, 50); Set(gun, "GUN_CALIBER_TAG", 10);
        client.gunOrderData.mandatoryRequirements = CurrentTags.Select(tag => new GunOrderRequirement { id = tag, value = 49 }).Append(new GunOrderRequirement { id = "GUN_CALIBER_TAG", value = 10, isCaliber = true }).ToList();
        Check(Matches(client, gun), "whole mandatory list can pass together");
        foreach (string tag in CurrentTags) { Set(gun, tag, 49); Check(!Matches(client, gun), "every mandatory requirement enforced: " + tag); Set(gun, tag, 50); }
        client.gunOrderData.bonusRequirements = new() { new GunOrderRequirement { id = CurrentTags[0], value = 5000 } };
        Check(Matches(client, gun), "unmet bonus is optional and does not disqualify accepted commission gun");
        client.isBusinessComplete = true; Check(!Matches(client, gun), "completed commission refused"); client.isBusinessComplete = false;
        client.gunOrderData.isServiceOrder = true; Check(!Matches(client, gun), "service order never replaced with inventory gun"); client.gunOrderData.isServiceOrder = false;
        Set(gun, "SERVICE_GUN_TAG"); Check(!Matches(client, gun), "customer service gun not resold to purchase commission"); Clear(gun, "SERVICE_GUN_TAG");
        client.clientIntent = StoreClient.ClientIntent.BUY; Check(!Matches(client, gun), "ordinary buyer does not use commission path");
        client.clientIntent = StoreClient.ClientIntent.GUNSMITH; Clear(gun, "ADV_GUN_TAG"); Check(!Matches(client, gun), "non-advanced-gun item refused");
    }
    private static void NativeCandidateProtection()
    {
        var client = Client(); var root = EmporiumEntry.Instance.invElement; var gun = Gun(); Put(root, gun);
        Check(Candidate(client, gun, root), "owned loose stock gun is eligible");
        Check(!Candidate(client, gun, Grid()), "mismatched source reference refused");
        gun.unitCount = 2; Check(!Candidate(client, gun, root), "gun stack is not implicitly split"); gun.unitCount = 1;
        foreach (string tag in new[] { "PINNED_TAG", "IMPORTANT_TAG", "LOU_TOY_TAG", "LOU_TOY_BOX_TAG", "LOU_LOCKER_TAG", "ITEM_BEHIND_LOCK_TAG" })
        {
            Set(gun, tag); Check(!Candidate(client, gun, root), "protected stock gun refused: " + tag); Clear(gun, tag);
        }
        Clear(gun, "IS_OWNED_TAG"); Check(!Candidate(client, gun, root), "non-owned gun refused"); Set(gun, "IS_OWNED_TAG");
        Policy(gun, "{\"Version\":1,\"ContainerLocked\":true}"); Check(!Candidate(client, gun, root), "gun automation lock refused"); Clear(gun, PolicyTag);
        root.overrideLockRemove = true; Check(!Candidate(client, gun, root), "native source removal lock refused"); root.overrideLockRemove = false;
        root.overrideLockInsert = true; Check(!Candidate(client, gun, root), "native source insertion lock refused"); root.overrideLockInsert = false;
        var part = Attach(gun); Check(Candidate(client, gun, root), "owned installed gun mod travels as part of original whole gun");
        foreach (string tag in new[] { "PINNED_TAG", "IMPORTANT_TAG", "LOU_TOY_TAG", "ITEM_BEHIND_LOCK_TAG" })
        {
            Set(part, tag); Check(!Candidate(client, gun, root), "protected contained accessory refused: " + tag); Clear(part, tag);
        }
        Clear(part, "IS_OWNED_TAG"); Check(!Candidate(client, gun, root), "customer-owned installed accessory refused"); Set(part, "IS_OWNED_TAG");
        Clear(part, "WEAPON_MOD_TAG"); Check(!Candidate(client, gun, root), "non-gun-mod content refused"); Set(part, "WEAPON_MOD_TAG");
        part.itemTypes.Clear(); Check(!Candidate(client, gun, root), "tagged content without native GUN_MOD type refused"); part.itemTypes.Add("GUN_MOD");
        Policy(part, "{\"Version\":1,\"ContainerLocked\":true}"); Check(!Candidate(client, gun, root), "contained accessory automation lock protects whole gun"); Clear(part, PolicyTag);
        gun.contentWindow.isLocked = true; Check(!Candidate(client, gun, root), "locked gun content window protects whole gun"); gun.contentWindow.isLocked = false;
        ((GameSlotInventory)part.parentInventory).overrideLockRemove = true; Check(!Candidate(client, gun, root), "native locked attachment slot protects whole gun"); ((GameSlotInventory)part.parentInventory).overrideLockRemove = false;
        client.gunOrderData.isServiceOrder = true; Check(!Candidate(client, gun, root), "service order candidate refused before staging"); client.gunOrderData.isServiceOrder = false;
        client.isSecurity = true;
        foreach (var riskItem in new GameItem[] { gun, part }) foreach (string tag in new[] { "CONTRABAND_ITEM_TAG", "CONTRABAND", "STOLEN_TAG" })
        { Set(riskItem, tag); Check(!Candidate(client, gun, root), "security commissioner rejects risk in whole gun or accessory: " + tag); Clear(riskItem, tag); }
        client.isSecurity = false; Check(Candidate(client, gun, root), "all temporary test locks removed restores eligibility");
    }
    private static void StorageProtection()
    {
        var client = Client(); var root = EmporiumEntry.Instance.invElement;
        var box = Item("service_box", "CONTAINER_TAG", "IMPORTANT_TAG", "PINNED_TAG"); var page = Grid(); var window = Window(box, page); Put(root, box);
        var gun = Gun(); Put(page, gun);
        Check(Candidate(client, gun, page), "pinned but unlocked audited storage shell does not block its gun contents");
        Policy(box, "{\"Version\":1,\"ContainerLocked\":true}"); Check(!Candidate(client, gun, page), "box automation lock protects nested gun");
        Policy(box, "{\"Version\":1,\"Pages\":{\"0\":{\"SaleLocked\":true}}}"); Check(!Candidate(client, gun, page), "specific page sale lock protects gun"); Clear(box, PolicyTag);
        window.isLocked = true; Check(!Candidate(client, gun, page), "native locked storage window protects gun"); window.isLocked = false;
        page.overrideLockRemove = true; Check(!Candidate(client, gun, page), "native source page remove lock protects gun"); page.overrideLockRemove = false;
        Set(box, "LOU_TOY_BOX_TAG"); Check(!Candidate(client, gun, page), "Lou box contents categorically excluded"); Clear(box, "LOU_TOY_BOX_TAG");
        box.identifier = "lou_locker"; Check(!Candidate(client, gun, page), "Lou locker identity blocks its contents without extra flags"); box.identifier = "service_box";
        Check(Candidate(client, gun, page), "unlocking ordinary storage returns accessibility");
        var machine = Item("water_purifier", "MACHINE"); var machineGrid = Grid(); Window(machine, machineGrid); Put(root, machine);
        gun.parentInventory = machineGrid; Check(!Candidate(client, gun, machineGrid), "functional machine grid cannot masquerade as stock source");
    }
    private static void ExpansionKit()
    {
        var mods = (List<LoadedMod>)typeof(ModLoader).GetField("mods", All)!.GetValue(null)!;
        mods.Add(new LoadedMod { Manifest = new ModManifest { ID = "gunsmith_expansion", ModVersion = "2.1.1" }, IsEnabled = true });
        var client = Client();
        foreach (string id in new[] { "gsx_ll_pistol", "gsx_riot_gun" })
        {
            var gun = Gun(id); Check(!Matches(client, gun), "GSX less-lethal gun without kit fails hidden purchase condition");
            Attach(gun, "gsx_conversion_kit"); Check(Matches(client, gun), "GSX installed lethal conversion kit permits eligible purchase gun");
        }
        Check(Matches(client, Gun("gsx_service_10")), "GSX ordinary lethal gun does not require conversion kit");
        mods.Clear(); Check(Matches(client, Gun("gsx_ll_pistol")), "disabled optional GSX does not inject its external hidden rule");
    }
    private static IEnumerable<MethodInfo> GeneratedStageMethods()
    {
        yield return Service.GetMethod("Stage", All)!;
        foreach (var type in Service.GetNestedTypes(All)) foreach (var method in type.GetMethods(All))
            if (method.Name.Contains("<Stage>", StringComparison.Ordinal)) yield return method;
    }
    private static void CompiledDispatchAndStage()
    {
        var run = PatchProcessor.GetOriginalInstructions(typeof(DeepSpaceQoL.StockingService).GetMethod("Run", All)!);
        int dispatch = run.FindIndex(i => i.operand is MethodInfo m && m.DeclaringType == Service && m.Name == "Run");
        int ordinary = run.FindIndex(i => i.operand is MethodInfo m && m.DeclaringType == typeof(StoreClient) && m.Name == "IsClientBuying");
        int budget = run.FindIndex(i => i.operand is FieldInfo f && f.DeclaringType == typeof(StoreClient) && f.Name == "useClientBudget");
        Check(dispatch >= 0 && ordinary > dispatch && budget > dispatch, "compiled commission dispatch precedes ordinary buyer and budget gates");
        var code = GeneratedStageMethods().SelectMany(method => PatchProcessor.GetOriginalInstructions(method)).ToArray();
        bool Calls(Type type, string name) => code.Any(i => i.operand is MethodInfo m && m.DeclaringType == type && m.Name == name);
        Check(Calls(typeof(GunOrderData), "IsAccepted"), "compiled Stage invokes final native/expansion-patched order acceptance");
        Check(Calls(typeof(GunOrderData), "ResolveOrderGun"), "compiled Stage verifies resolved native order gun identity");
        Check(Calls(typeof(SlotMarker), "TryAcceptOnce"), "compiled Stage uses native transfer rather than duplicating gun");
        Check(Calls(typeof(GameInventory), "UncheckedAccept"), "compiled Stage retains original-source restore path");
        Check(Calls(typeof(PlayerStore), "OnWeightedAreaChanged") && Calls(typeof(NegociationUIManager), "UpdateUI"), "compiled Stage explicitly rebuilds commission UI after transfer or rollback");
        Check(!code.Any(i => i.operand is MethodInfo m && (m.Name == "OnOrderAccepted" || m.Name == "Destroy")), "staging never delivers commission or destroys original gun");
        Check(code.Any(i => i.operand is MethodInfo m && m.DeclaringType?.Name == "WholeItemTransferTransaction" && m.Name == "Run"), "compiled Stage uses transaction with explicit recovery");
        Check(code.Any(i => i.operand is ConstructorInfo constructor && constructor.DeclaringType?.Name == "ItemStateSnapshot"), "compiled Stage captures deep snapshots of gun and installed members");
    }
}

sealed class StockFixtureItem : GameItem
{
    public StockFixtureItem(string id) : base(id) { }
    internal readonly List<GraphNodeStorage> Nodes = new();
    public override List<GraphNodeStorage> children => Nodes;
    public override void Destroy() => throw new InvalidOperationException("Stocking tests must never destroy an item.");
    public override void Validate() { }
}
