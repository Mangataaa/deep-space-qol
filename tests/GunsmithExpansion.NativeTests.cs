using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Xml.Linq;
using DeepSpaceQoL.Core;
using HarmonyLib;
using Option = DeepSpaceQoL.Core.GunModificationPlanner.Option;
using Problem = DeepSpaceQoL.Core.GunModificationPlanner.Problem;

string managed = args.Length > 0 ? Path.GetFullPath(args[0]) : @"D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed";
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string path = Path.Combine(managed, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
string expansion = args.Length > 1 ? Path.GetFullPath(args[1]) : @"D:\SteamLibrary\steamapps\workshop\content\4348910\3834619049\GunsmithExpansion.dll";
GsxTests.Run(expansion);

static class GsxTests
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private const string Kit = "gsx_conversion_kit";
    private static readonly Assembly Mod = typeof(DeepSpaceQoL.ModEntry).Assembly;
    private static readonly Type Compat = Mod.GetType("DeepSpaceQoL.GunsmithExpansionCompatibility", true)!;
    private static readonly Type Service = Mod.GetType("DeepSpaceQoL.GunModdingService", true)!;
    private static readonly string[] Roles = { "mech_gun_tag_stock", "mech_gun_tag_rail", "mech_gun_tag_magazine", "mech_gun_tag_underbarrel", "mech_gun_tag_barrel", "mech_gun_tag_special1", "mech_gun_tag_special2", "mech_gun_tag_special3" };
    private static readonly string[] GunIds = { "gsx_pocket_pistol", "gsx_slug_gun", "gsx_service_10", "gsx_compact_smg", "gsx_hand_cannon", "gsx_carbine", "gsx_malorian", "gsx_gilded_pistol", "gsx_snub_revolver", "gsx_fries", "gsx_five_seven", "gsx_ll_pistol", "gsx_riot_gun" };
    private static int checks;
    private static void Check(bool condition, string reason) { checks++; if (!condition) throw new InvalidOperationException(reason); }
    private static object? Invoke(Type type, string name, params object?[] arguments)
    {
        try { return type.GetMethod(name, All)!.Invoke(null, arguments); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static bool Active => (bool)Compat.GetProperty("IsActive", All)!.GetValue(null)!;
    private static T Native<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static void Field(object target, string name, object? value)
    {
        for (Type? type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, All | BindingFlags.DeclaredOnly) ?? type.GetField("<" + name + ">k__BackingField", All | BindingFlags.DeclaredOnly);
            if (field != null) { field.SetValue(target, value); return; }
        }
        throw new MissingFieldException(target.GetType().FullName, name);
    }
    private static void Set(GameItem item, string tag, int value = 0)
    { item.state.dict[tag] = new TagState(tag, tag) { valueEnabled = true, valueInt = value }; item.modifiedState = item.state; }
    private static FixtureItem Item(string id)
    {
        var shape = new GridShapeBuilder(4, 2).SetDataFill(1);
        return new FixtureItem(id) { shape = shape, modifiedShape = shape.Clone(), state = new TagSystem(), modifiedState = new TagSystem(), itemFeatures = new(), itemTypes = new(), unitCount = 1 };
    }
    private static FixtureItem Gun(string id)
    {
        var gun = Item(id); GunHelper.InitGun(gun, 30, 40, 50, 60, 70, 80, 22, 100);
        Set(gun, "IS_OWNED_TAG"); return gun;
    }
    private static int[] ExpectedKinds(string id) => Enumerable.Range(0, 8).Where(kind => kind != 0 && (kind != 1 || id != "gsx_carbine") &&
        (kind != 2 || id is "gsx_carbine" or "gsx_fries" or "gsx_five_seven")).ToArray();
    private static (MachineryPanelRow Row, List<GameSlotInventory> Slots) Slots(FixtureItem gun, int[] kinds)
    {
        var row = Native<MachineryPanelRow>(); var panels = new List<MachineryPanel>(); var slots = new List<GameSlotInventory>();
        foreach (int kind in kinds)
        {
            var slot = Native<GameSlotInventory>(); var intake = Native<MachineryIntake>(); Field(intake, "content", slot);
            var grid = Native<GridPixelElement>(); Field(grid, "childElements", new PixelElement[] { intake }); Field(grid, "gridWidth", 1); Field(grid, "gridHeight", 1);
            var panel = Native<MachineryPanel>(); panel.role = Roles[kind]; Field(panel, "content", grid); panels.Add(panel); slots.Add(slot);
            Field(slot, "parent", grid); grid.parent = row;
        }
        Field(row, "panels", panels);
        var window = Native<PixelWindow>(); Field(window, "parentItems", new List<GameItem> { gun }); Field(window, "childElement", row);
        row.parent = window; Field(gun, "contentWindow", window); gun.Nodes.Add(window);
        for (int i = 0; i < kinds.Length; i++)
        {
            switch (kinds[i])
            {
                case 0: GunModHelper.EnableStockSlot(slots[i]); break;
                case 1: GunModHelper.EnableRailSlot(slots[i]); break;
                case 2: GunModHelper.EnableMagazineSlot(slots[i]); break;
                case 3: GunModHelper.EnableUnderbarrelSlot(slots[i]); break;
                case 4: GunModHelper.EnableBarrelSlot(slots[i]); break;
                default: GunModHelper.EnableSpecialSlot(slots[i]); break;
            }
        }
        return (row, slots);
    }
    private static bool ReadSlots(GameItem gun, out int[] kinds)
    {
        object?[] args = { gun, null, null };
        bool accepted = (bool)Service.GetMethod("TrySlots", All)!.Invoke(null, args)!;
        kinds = (int[])args[2]!; return accepted;
    }
    private static GunModificationPlanner.Result Solve(Problem p)
    {
        var search = new GunModificationPlanner.Search(p); int guard = 0;
        while (!search.Complete && guard++ < 1_000_000) search.Step(16);
        Check(search.Complete, "search remains bounded"); return search.Best;
    }
    internal static void Run(string path)
    {
        var mods = (List<LoadedMod>)typeof(ModLoader).GetField("mods", All)!.GetValue(null)!;
        mods.Clear(); Check(!Active, "no optional mod requires no expansion dependency");
        var vanilla = Gun("handmade_pistol"); Slots(vanilla, Enumerable.Range(0, 8).ToArray());
        Check(ReadSlots(vanilla, out var vanillaKinds) && vanillaKinds.SequenceEqual(Enumerable.Range(0, 8)), "vanilla eight-slot behavior preserved without GSX");
        var partial = Gun("gsx_service_10"); Slots(partial, ExpectedKinds(partial.identifier));
        Check(!ReadSlots(partial, out _), "unknown partial-slot gun is refused without active audited expansion");
        RequiredIdentifierSearch();
        if (!File.Exists(path))
        {
            Console.WriteLine($"GunsmithExpansion base checks passed: {checks}; optional actual-DLL checks skipped (not installed: {path}). No Unity scene or game save executed.");
            return;
        }
        string manifestPath = Path.Combine(Path.GetDirectoryName(path)!, "manifest.xml");
        if (!File.Exists(manifestPath))
        {
            Console.WriteLine($"GunsmithExpansion base checks passed: {checks}; actual-DLL checks refused because its adjacent manifest.xml is absent. No Unity scene or game save executed.");
            return;
        }
        var actualManifest = XDocument.Load(manifestPath).Root;
        Check(actualManifest?.Element("ID")?.Value == "gunsmith_expansion" && actualManifest.Element("ModVersion")?.Value == "2.1.1", "actual adjacent expansion manifest identifies the reviewed gunsmith_expansion 2.1.1");
        Assembly gsx = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        Check(gsx.GetName().Name == "GunsmithExpansion" && gsx.GetName().Version == new Version(2, 1, 1, 0), "actual optional DLL identity matches the reviewed 2.1.1 rules");
        Console.WriteLine($"Actual GSX 2.1.1 DLL SHA256: {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()}");
        var manifest = new ModManifest { ID = "gunsmith_expansion", ModVersion = "2.1.1" };
        var loaded = new LoadedMod { Manifest = manifest, Assembly = gsx, IsEnabled = false };
        mods.Add(loaded); Check(!Active, "installed but disabled expansion does not activate compatibility");
        loaded.IsEnabled = true; manifest.ModVersion = "0.0.0"; Check(!Active, "unreviewed expansion version does not activate audited exceptions");
        manifest.ModVersion = "2.1.1"; Check(Active, "enabled reviewed expansion version activates optional compatibility");
        Layouts(); NativeFilters(gsx); NativeOrders(gsx); NativeShapes(gsx); ProductionOptions(gsx); FinalAcceptanceCall(); NativeConversionRollback(gsx);
        mods.Clear(); Check(!Active && !ReadSlots(partial, out _), "disabling/unloading expansion removes partial-layout exception");
        Console.WriteLine($"GunsmithExpansion compatibility checks passed: {checks}. Actual installed GSX 2.1.1 DLL filter/order/overlay/conversion code executed on data-only fixtures; no Unity rendering, live transfer callbacks, save loading or live gameplay executed.");
    }
    private static void Layouts()
    {
        foreach (string id in GunIds)
        {
            int[] expected = ExpectedKinds(id);
            Check(((int[])Invoke(Compat, "SlotKinds", id)!).SequenceEqual(expected), "audited exact GSX slot mapping: " + id);
            var gun = Gun(id); var fixture = Slots(gun, expected);
            Check(ReadSlots(gun, out var actual) && actual.SequenceEqual(expected), "production discovers real GSX role mapping without shifting missing slots: " + id);
            var panels = (List<MachineryPanel>)fixture.Row.GetType().GetField("panels", All)!.GetValue(fixture.Row)!;
            string original = panels[0].role; panels[0].role = "unexpected_role";
            Check(!ReadSlots(gun, out _), "malformed/incomplete known GSX layout rejected: " + id); panels[0].role = original;
        }
        Check(Invoke(Compat, "SlotKinds", "gsx_future_gun") == null, "new unreviewed expansion gun has no audited slot exception");
        var unknown = Gun("unreviewed_gun"); Slots(unknown, new[] { 1, 3, 4, 5, 6, 7 });
        Check(!ReadSlots(unknown, out _), "arbitrary unknown partial native layout still refused");
    }
    private static void RequiredIdentifierSearch()
    {
        var strong = new Option { Token = 1, Identifier = "overpressure", Special = true, Flat = new[] { 100, 0, 0, 0, 0, 0 }, Cost = 1 };
        var kit = new Option { Token = 2, Identifier = Kit, Special = true, Percent = new[] { 75, 0, -10, 0, 0, 0 }, Flat = new[] { 8, 0, -3, 0, 0, 0 }, Cost = 90 };
        var p = new Problem { BaseStats = new[] { 10, 20, 30, 40, 50, 60 }, Mandatory = new[] { new GunModificationPlanner.Requirement { Stat = 0, Value = 15 } },
            RequiredIdentifiers = new[] { Kit }, Slots = new[] { new[] { new Option(), strong, kit } } };
        var plan = Solve(p); Check(plan.Choices is { Length: 1 } && plan.Choices[0].Identifier == Kit, "hidden conversion-kit condition outweighs higher numeric stats");
        p.Slots = new[] { new[] { new Option(), strong } }; Check(Solve(p).Choices == null, "missing conversion kit is not replaced with numerically strong ordinary part");
        p.Slots = new[] { new[] { new Option(), kit }, new[] { new Option(), kit } }; plan = Solve(p);
        Check(plan.Choices != null && plan.Choices.Count(o => o.Identifier == Kit) == 1, "one real conversion kit cannot be duplicated across special slots");
        p.RequiredIdentifiers = new[] { Kit, "second_required_part" }; Check(Solve(p).Choices == null, "all extra required identifiers must be present");
        p.RequiredIdentifiers = Array.Empty<string>(); p.Slots = new[] { new[] { new Option(), strong } };
        Check(Solve(p).Choices != null, "vanilla numeric-only planning continues without optional hidden requirements");
        p.RequiredIdentifiers = new[] { Kit }; p.Slots = Array.Empty<Option[]>();
        Check(Solve(p).Choices == null, "zero-slot search cannot bypass hidden required item");
    }
    private static void NativeFilters(Assembly gsx)
    {
        Type guns = gsx.GetType("GunsmithExpansion.Guns", true)!;
        Type reserved = gsx.GetType("GunsmithExpansion.ReservedMods", true)!;
        string[] revolvers = (string[])guns.GetField("REVOLVERS", All)!.GetValue(null)!;
        string[] builtUnder = (string[])guns.GetField("BUILT_IN_UNDERBARREL", All)!.GetValue(null)!;
        foreach (string gun in GunIds.Concat(new[] { "smg", "shotgun", "handmade_pistol", "third_party_receiver" }))
            foreach (string partId in new[] { "gsx_heavy_suppressor", "gsx_tactical_light", "gsx_polished_slide", "gsx_gold_inlay", "gl", "other_part" })
                foreach (int kind in new[] { 1, 3, 4, 5 })
                {
                    var part = Item(partId); if (partId == "gsx_heavy_suppressor") Set(part, "MOD_SUPPRESSOR_TAG");
                    bool expected = !(bool)Invoke(reserved, "IsUnfit", partId, gun)!;
                    if (kind == 4 && revolvers.Contains(gun))
                    {
                        var slot = Native<GameSlotInventory>(); slot.mayInventoryAddItemFunc = (_, _) => true;
                        Invoke(guns, "NoSuppressor", slot); expected &= slot.mayInventoryAddItemFunc(part, slot);
                    }
                    if (kind == 3 && builtUnder.Contains(gun))
                    {
                        var slot = Native<GameSlotInventory>(); Invoke(guns, "NoUnderbarrel", slot);
                        expected &= slot.mayInventoryAddItemFunc(part, slot);
                    }
                    Check((bool)Invoke(Compat, "AllowsPart", gun, kind, part)! == expected, $"production prefilter matches actual GSX restriction {gun}/{kind}/{partId}");
                }
    }
    private static GameGridInventory Grid()
    { var grid = Native<GameGridInventory>(); Field(grid, "items", new List<GameItem>()); Field(grid, "inventoryShape", new GridShapeBuilder(30, 30).SetDataFill(0)); return grid; }
    private static void Current(GunOrderData order, GameItem gun)
    {
        var store = Native<PlayerStore>(); store.ActivePermits = new();
        typeof(PlayerStore).GetField("instance", All)!.SetValue(null, store);
        var entry = Native<EmporiumEntry>(); Field(entry, "m_CachedPtr", new IntPtr(1));
        Field(entry, "frontInvinvElement", Grid()); Field(entry, "backInvinvElement", Grid());
        typeof(EmporiumEntry).GetField("<Instance>k__BackingField", All)!.SetValue(null, entry);
        gun.parentInventory = entry.frontInvinvElement;
        Field(order, "serviceGun", gun); Field(order, "serviceGunId", gun.identifier);
        var client = new StoreClient { clientIntent = StoreClient.ClientIntent.GUNSMITH, gunOrderData = order };
        var instance = Native<StoreClientInstance>(); Field(instance, "storeClient", client); store.currentClientInstance = instance;
        store.currentNegociatedItem = gun;
    }
    private static bool AcceptedByGsx(Assembly gsx, GunOrderData order)
    {
        object?[] args = { order, true };
        gsx.GetType("GunsmithExpansion.LessLethalOrderPatch", true)!.GetMethod("Postfix", All)!.Invoke(null, args);
        return (bool)args[1]!;
    }
    private static void NativeOrders(Assembly gsx)
    {
        Type ll = gsx.GetType("GunsmithExpansion.LessLethal", true)!;
        foreach (string id in GunIds)
        {
            bool special = id is "gsx_ll_pistol" or "gsx_riot_gun";
            Check(((string[])Invoke(Compat, "RequiredParts", id)!).SequenceEqual(special ? new[] { Kit } : Array.Empty<string>()), "only less-lethal conversion guns need hidden kit: " + id);
        }
        foreach (string id in new[] { "gsx_ll_pistol", "gsx_riot_gun" })
        {
            var gun = Gun(id); Set(gun, "GUN_CURRENT_FIREPOWER_TAG", 12);
            var order = new GunOrderData { isServiceOrder = true }; Current(order, gun);
            Invoke(ll, "MakeConversionOrder", order, gun);
            Check(order.mandatoryRequirements.Count == 1 && order.mandatoryRequirements[0].value == 21, "actual expansion conversion order requests ten extra firepower with strict native threshold");
            Set(gun, "GUN_CURRENT_FIREPOWER_TAG", 50);
            Check(order.IsAccepted() && !AcceptedByGsx(gsx, order), "numeric requirements alone pass but actual GSX hidden condition refuses missing kit");
            gun.Nodes.Add(Item(Kit)); Check(AcceptedByGsx(gsx, order), "actual GSX conversion order accepts installed kit");
            order.isServiceOrder = false; gun.parentInventory = EmporiumEntry.Instance.backInvinvElement;
            Check(AcceptedByGsx(gsx, order), "actual ordinary GSX purchase permits converted weapon");
            gun.Nodes.Clear(); Check(!AcceptedByGsx(gsx, order), "actual ordinary GSX purchase refuses non-lethal weapon without kit even when stats pass");
            gun.parentInventory = null;
        }
    }
    private static void NativeShapes(Assembly gsx)
    {
        Type overlays = gsx.GetType("GunsmithExpansion.Overlays", true)!;
        int found = 0;
        foreach (string id in GunIds.Concat(new[] { "smg", "shotgun", "glock_receiver" }))
            foreach (string partId in new[] { "gsx_holo_sight", "gsx_scope_2x", "gsx_heavy_suppressor", "gsx_angled_grip", "gsx_mag_10_ext", "gsx_bayonet" })
            {
                var gun = Gun(id); var part = Item(partId);
                var actual = (GridShape?)Invoke(overlays, "Shape", part, gun);
                if (actual == null) continue;
                found++; Check((bool)Invoke(Service, "SimpleShape", actual)!, "real expansion overlay remains safely modelled simple native shape: " + partId + "/" + id);
                var slot = Native<GameSlotInventory>(); Invoke(overlays, "InitGunSlot", slot);
                var marker = new SlotMarker { item = part, inventory = slot, itemGridShape = part.modifiedShape };
                var fromNative = part.GetParentShapeModifier(gun, marker, part.modifiedShape, part.state);
                Check(fromNative.Item2 != null && Cells(fromNative.Item2).SetEquals(Cells(actual)), "production native modifier path reads actual GSX slot overlay cells");
                var state = part.GetParentStateModifier(gun, marker, part.modifiedShape, part.state);
                Check((state.Item1?.Count ?? 0) == 0 && (state.Item2?.Count ?? 0) == 0, "GSX overlay does not silently alter gun state model");
            }
        Check(found >= 30, "actual GSX shape coverage includes multiple guns, native receivers and attachments");
    }
    private static HashSet<long> Cells(GridShape shape)
    {
        var result = new HashSet<long>();
        for (int y = shape.minY; y <= shape.maxY; y++) for (int x = shape.minX; x <= shape.maxX; x++) if (shape.Get(x, y) == 1) result.Add(((long)x << 32) | (uint)y);
        return result;
    }
    private static void ProductionOptions(Assembly gsx)
    {
        var station = Native<StoreStation>(); var events = Native<StoreEventManager>();
        Field(events, "activeEvents", new List<StoreEvent>()); station.storeEventManager = events;
        typeof(StoreStation).GetField("instance", All)!.SetValue(null, station);
        var store = Native<PlayerStore>(); store.ActivePermits = new();
        typeof(PlayerStore).GetField("instance", All)!.SetValue(null, store);
        Type sessionType = Service.GetNestedType("Session", All)!;
        Type overlays = gsx.GetType("GunsmithExpansion.Overlays", true)!;
        foreach (string id in new[] { "gsx_service_10", "gsx_carbine", "gsx_five_seven" })
        {
            var gun = Gun(id); int[] kinds = ExpectedKinds(id); var slots = Slots(gun, kinds).Slots;
            var session = Activator.CreateInstance(sessionType, nonPublic: true)!;
            Field(session, "Gun", gun); Field(session, "Slots", slots.ToArray()); Field(session, "SlotKinds", kinds); Field(session, "UsesGsxRules", true);
            for (int index = 0; index < kinds.Length; index++)
            {
                int kind = kinds[index]; string tag = kind switch { 1 => "MOD_RAIL_TAG", 2 => "MOD_MAGAZINE_TAG", 3 => "MOD_UNDER_RAIL_TAG", 4 => "MOD_BARREL_TAG", _ => "MOD_SPECIAL_TAG" };
                string partId = kind switch { 1 => "gsx_holo_sight", 2 => "gsx_mag_10_ext", 3 => "gsx_tactical_light", 4 => "gsx_heavy_suppressor", _ => "gsx_match_trigger" };
                var part = Item(partId); Set(part, "IS_OWNED_TAG"); Set(part, "WEAPON_MOD_TAG"); Set(part, tag);
                Invoke(overlays, "InitGunSlot", slots[index]);
                var expected = (GridShape?)Invoke(overlays, "Shape", part, gun);
                object?[] arguments = { session, part, index, index + 1, true, 1, null };
                Check((bool)Service.GetMethod("TryOption", All)!.Invoke(null, arguments)!, $"production TryOption accepts actual compacted role {id}/{index}->{kind}");
                object option = arguments[^1]!;
                Check((bool)option.GetType().GetField("Special", All)!.GetValue(option)! == (kind >= 5), "production special-slot flag follows role rather than compacted index");
                var cells = (long[])option.GetType().GetField("ShapeCells", All)!.GetValue(option)!;
                Check(cells.ToHashSet().SetEquals(expected == null ? new HashSet<long>() : Cells(expected)), "production planned option includes exact actual GSX overlay cells");
                var stored = (System.Collections.IDictionary)sessionType.GetField("Shapes", All)!.GetValue(session)!;
                Check(stored.Contains(option), "production session retains native overlay shape for final gun-fit validation");
                part.state.dict.Remove(tag); Set(part, kind == 1 ? "MOD_STOCK_TAG" : "MOD_RAIL_TAG");
                if (kind == 1 || kind == 2)
                {
                    object?[] wrong = { session, part, index, index + 100, true, 1, null };
                    Check(!(bool)Service.GetMethod("TryOption", All)!.Invoke(null, wrong)!, "compacted first slot does not inherit absent stock/rail role");
                }
            }
        }
    }
    private static void FinalAcceptanceCall()
    {
        MethodInfo commit = Service.GetMethod("Commit", All)!;
        var methods = new List<MethodInfo> { commit };
        methods.AddRange(PatchProcessor.GetOriginalInstructions(commit).Select(i => i.operand).OfType<MethodInfo>()
            .Where(m => m.Name.Contains("<Commit>", StringComparison.Ordinal)));
        Check(methods.SelectMany(method => PatchProcessor.GetOriginalInstructions(method)).Any(i => i.operand is MethodInfo method &&
            method.DeclaringType == typeof(GunOrderData) && method.Name == "IsAccepted"), "production final Commit path invokes actual native/GSX-patched IsAccepted instead of only numeric requirement checks");
    }
    private static void NativeConversionRollback(Assembly gsx)
    {
        Type ll = gsx.GetType("GunsmithExpansion.LessLethal", true)!;
        Type snapshotType = Mod.GetType("DeepSpaceQoL.ItemStateSnapshot", true)!;
        foreach (string id in new[] { "gsx_ll_pistol", "gsx_riot_gun" }) foreach (bool permit in new[] { false, true })
        {
            var gun = Gun(id); var slots = Slots(gun, ExpectedKinds(id)).Slots;
            gun.name = "original receiver"; gun.unitValue = 100; gun.itemTypes.Add("FIREARM");
            var order = new GunOrderData { isServiceOrder = true }; Current(order, gun);
            if (permit) PlayerStore.Instance.ActivePermits.Add(("FIREARM", id == "gsx_ll_pistol" ? 1 : 2));
            gun.parentInventory = null;
            gun.ValidateShapeState(); GunHelper.UpdateCurrentGunStats(gun);
            var originalTags = gun.state.dict.ToDictionary(p => p.Key, p => (p.Value.valueEnabled, p.Value.valueInt, p.Value.valueString));
            var originalTypes = gun.itemTypes.ToArray();
            var snapshot = Activator.CreateInstance(snapshotType, All, null, new object[] { gun }, null)!;
            var kit = Item(Kit); var target = slots[^1]; Field(target, "currentItem", kit); kit.parentInventory = target;
            Invoke(ll, "Refresh", gun, slots);
            Check(gun.IsTag("GSX_LETHAL_CONVERTED_TAG") && (gun.IsGameItemType("CONTRABAND") || gun.IsGameItemType(permit ? (id == "gsx_ll_pistol" ? "RESTRICTED_1" : "RESTRICTED_2") : "CONTRABAND")), "actual GSX conversion callback mutates persistent gun tag and legal type");
            Field(target, "currentItem", null); kit.parentInventory = null; Invoke(ll, "Refresh", gun, slots);
            Check(gun.IsTag("GSX_LETHAL_CONVERTED_TAG"), "actual GSX callback leaves conversion history after removing kit");
            Invoke(Service, "RestoreGunState", gun, snapshot);
            Check(!gun.IsTag("GSX_LETHAL_CONVERTED_TAG") && gun.itemTypes.SequenceEqual(originalTypes), "production rollback clears conversion history and restores all legal types");
            Check(originalTags.Count == gun.state.dict.Count && originalTags.All(p => gun.state.dict.TryGetValue(p.Key, out var value) && p.Value == (value.valueEnabled, value.valueInt, value.valueString)), "production rollback deeply restores native gun state after GSX callback");
            Check(gun.name == "original receiver" && gun.unitValue == 100, "production rollback preserves gun identity-facing fields");
        }
    }
}

// No textures, Unity objects or save files: native item, tag and graph code run on fixtures.
sealed class FixtureItem : GameItem
{
    public FixtureItem(string id) : base(id) { }
    internal readonly List<GraphNodeStorage> Nodes = new();
    public override List<GraphNodeStorage> children => Nodes;
    public override void Destroy() => throw new InvalidOperationException("Compatibility tests must not destroy real items.");
    public override void Validate() { }
}
