using System.Runtime.Loader;
using System.Runtime.CompilerServices;
using System.Reflection;
using DeepSpaceQoL.Core;
using Option = DeepSpaceQoL.Core.GunModificationPlanner.Option;
using Problem = DeepSpaceQoL.Core.GunModificationPlanner.Problem;
using Requirement = DeepSpaceQoL.Core.GunModificationPlanner.Requirement;

string managed = args.Length > 0 ? Path.GetFullPath(args[0]) : @"D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed";
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string path = Path.Combine(managed, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
GunTests.Run();

static class GunTests
{
    private static int checks;
    private static readonly string[] Stats = { "GUN_FIREPOWER_TAG", "GUN_STYLE_TAG", "GUN_RELIABILITY_TAG", "GUN_RECOIL_TAG", "GUN_ERGONOMICS_TAG", "GUN_ACCURACY_TAG" };
    private static readonly string[] Current = { "GUN_CURRENT_FIREPOWER_TAG", "GUN_CURRENT_STYLE_TAG", "GUN_CURRENT_RELIABILITY_TAG", "GUN_CURRENT_RECOIL_TAG", "GUN_CURRENT_ERGONOMICS_TAG", "GUN_CURRENT_ACCURACY_TAG" };
    private static readonly string[] Flat = { "MOD_BASE_FIREPOWER_TAG", "MOD_BASE_STYLE_TAG", "MOD_BASE_RELIABILITY_TAG", "MOD_BASE_RECOIL_TAG", "MOD_BASE_ERGONOMICS_TAG", "MOD_BASE_ACCURACY_TAG" };
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
    private static void Set(GameItem item, string tag, int value) => item.state.dict[tag] = new TagState(tag, tag) { valueEnabled = true, valueInt = value };
    private static DataItem Item(string id)
    {
        var shape = new GridShapeBuilder(4, 2).SetDataFill(1);
        return new DataItem { identifier = id, state = new TagSystem(), modifiedState = new TagSystem(), itemFeatures = new(), itemTypes = new(), shape = shape, modifiedShape = shape.Clone(), unitCount = 1 };
    }
    internal static void Run()
    {
        NativeRequirements(); NativeStatMath(); NativeShapeMath(); KnownPlans(); RandomPlans(); Transactions(); ProductionAccess(); NativeSlotsAndSession();
        Console.WriteLine($"GunModding native/policy checks passed: {checks}. Native requirement/stat/shape code executed on data fixtures; no Unity scene, game save, rendering or live inventory callbacks executed.");
    }

    private static void NativeRequirements()
    {
        var gun = Item("test_gun"); Set(gun, "ADV_GUN_TAG", 0);
        foreach (bool larger in new[] { true, false }) foreach (int threshold in new[] { -31, -1, 0, 1, 29, 100 })
            foreach (int offset in new[] { -1, 0, 1 })
            {
                Set(gun, Current[0], threshold + offset); gun.modifiedState = gun.state;
                var native = new GunOrderRequirement { id = Current[0], value = threshold, isMustBeLargerThan = larger };
                var pure = new Requirement { Stat = 0, Value = threshold, Larger = larger };
                Check(native.IsMet(gun) == pure.IsMet(threshold + offset), "native strict greater/less boundary");
            }
        foreach (int caliber in new[] { 10, 12, 22, 45 }) foreach (int requested in new[] { 10, 12, 22, 45 })
        {
            Set(gun, "GUN_CALIBER_TAG", caliber); gun.modifiedState = gun.state;
            var native = new GunOrderRequirement { id = "GUN_CALIBER_TAG", value = requested, isCaliber = true };
            Check(native.IsMet(gun) == new Requirement { Stat = 7, Value = requested, Equal = true }.IsMet(caliber), "native caliber equality");
        }
        foreach (int suppression in new[] { 0, 1, 2 }) foreach (int required in new[] { 0, 1 })
        {
            Set(gun, "GUN_SUPPRESSED_TAG", suppression); gun.modifiedState = gun.state;
            Check(new GunOrderRequirement { id = "GUN_SUPPRESSED_TAG", value = required }.IsMet(gun) ==
                new Requirement { Stat = 6, Value = required }.IsMet(suppression), "makeshift/proper suppression threshold");
        }
    }

    private static void NativeStatMath()
    {
        var random = new Random(6341);
        var conditionIds = new[] { "equipment_condition_new", "equipment_condition_excellent", "equipment_condition_good", "equipment_condition_fair", "equipment_condition_poor", "unknown_condition" };
        var conditions = new[] { 30, 15, 0, -15, -30, 0 };
        for (int test = 0; test < 240; test++)
        {
            var gun = Item("test_gun"); Set(gun, "ADV_GUN_TAG", 0);
            int condition = test % conditionIds.Length, caliber = new[] { 10, 12, 22, 45 }[test % 4];
            var bases = Enumerable.Range(0, 6).Select(_ => random.Next(1, 100)).ToArray();
            for (int stat = 0; stat < 6; stat++) Set(gun, Stats[stat], bases[stat]);
            Set(gun, "GUN_CALIBER_TAG", caliber);
            gun.itemFeatures.Add(new ItemFeature { category = "CATEGORY_EQUIPMENT_CONDITION", realCondition = new ItemCondition { identifier = conditionIds[condition] } });
            var options = new List<Option>();
            for (int i = 0; i < test % 9; i++)
            {
                var part = Item("part_" + i); Set(part, "WEAPON_MOD_TAG", 0);
                var percent = Enumerable.Range(0, 6).Select(_ => random.Next(-90, 91)).ToArray();
                var flat = Enumerable.Range(0, 6).Select(_ => random.Next(-25, 26)).ToArray();
                for (int stat = 0; stat < 6; stat++) { Set(part, Stats[stat], percent[stat]); Set(part, Flat[stat], flat[stat]); }
                int suppression = random.Next(3);
                if (suppression > 0) { Set(part, "MOD_SUPPRESSOR_TAG", 0); if (suppression == 1) part.itemTypes.Add("MAKESHIFT"); }
                part.modifiedState = part.state; gun.Nodes.Add(part);
                options.Add(new Option { Token = i, Percent = percent, Flat = flat, Suppression = suppression });
            }
            gun.modifiedState = gun.state;
            GunHelper.UpdateCurrentGunStats(gun);
            var values = GunModificationPlanner.Calculate(bases, conditions[condition], caliber, Enumerable.Range(0, 8).Select(i => (long)i).ToArray(), options);
            for (int stat = 0; stat < 6; stat++) Check(values[stat] == gun.GetTagReadonly(Current[stat]).GetInt(), "exact native percentage+flat+condition integer math");
            Check(values[6] == gun.GetTagReadonly("GUN_SUPPRESSED_TAG").GetInt(), "native highest suppressor tier");
            Check(values[7] == gun.GetTagReadonly("GUN_CALIBER_TAG").GetInt(), "caliber remains unchanged");
            Check(values[8] == gun.GetTagReadonly("GUN_SIZE_TAG").GetInt(), "native base size count");
        }
    }

    private static void NativeShapeMath()
    {
        long[] Cells(GridShape shape)
        {
            var result = new List<long>();
            for (int y = shape.minY; y <= shape.maxY; y++) for (int x = shape.minX; x <= shape.maxX; x++)
                if (shape.Get(x, y) == 1) result.Add(((long)x << 32) | (uint)y);
            return result.ToArray();
        }
        for (int orientation = 0; orientation < 4; orientation++) foreach (bool flipped in new[] { false, true })
        {
            var basis = new GridShapeBuilder(4, 2).SetDataFill(1);
            var stock = new GridShapeBuilder(2, 3).SetDataFill(1).SetPosition(-2, 0);
            var barrel = new GridShapeBuilder(2, 1).SetDataFill(1).SetPosition(4, 0);
            var duplicate = new GridShapeBuilder(2, 1).SetDataFill(1).SetPosition(4, 0);
            var native = new GridShapeBuilder(basis).SetTransform(9, 8, flipped, orientation);
            native.MergeWithLocalShapes(new List<GridShape> { stock, barrel, duplicate });
            int nativeCount = Cells(native).Length;
            var calculated = GunModificationPlanner.Calculate(new int[6], 0, 10, Cells(basis), new[] {
                new Option { Token = 1, ShapeCells = Cells(stock) }, new Option { Token = 2, ShapeCells = Cells(barrel) }, new Option { Token = 3, ShapeCells = Cells(duplicate) } });
            Check(calculated[8] == nativeCount && nativeCount == 16, "native shape union includes negative stock cells and does not double-count overlap in any orientation");
        }
    }

    private static Option Part(int token, int firepower = 0, int accuracy = 0, long cost = 10, string? group = null, bool special = false, string? id = null) =>
        new() { Token = token, Identifier = id ?? "part_" + token, ExclusiveGroup = group ?? "", IsNew = true, Changes = 1, Cost = cost,
            Flat = new[] { firepower, 0, 0, 0, 0, accuracy }, Special = special };
    private static GunModificationPlanner.Result Solve(Problem p, int step = 32)
    {
        var search = new GunModificationPlanner.Search(p); int guard = 0;
        while (!search.Complete && guard++ < 2_000_000) search.Step(step);
        Check(search.Complete, "bounded search completes"); return search.Best;
    }
    private static Problem Base(params Option[][] slots) => new() { BaseStats = new[] { 10, 10, 10, 60, 10, 10 }, Caliber = 22, BaseCells = new long[] { 0, 1, 2, 3 }, Slots = slots };
    private static void KnownPlans()
    {
        var p = Base(new[] { new Option(), Part(0, 10, cost: 20), Part(1, 10, cost: 5) }, new[] { new Option(), Part(2, accuracy: 10) });
        p.Mandatory = new[] { new Requirement { Stat = 0, Value = 19 } }; p.Bonus = new[] { new Requirement { Stat = 5, Value = 19, Reward = 50 } };
        var r = Solve(p); Check(r.Choices != null && r.Choices[0].Token == 1 && r.Choices[1].Token == 2 && r.BonusMet == 1, "mandatory+bonus then cheapest real parts");
        p.Validate = o => o[0].Token != 1; r = Solve(p); Check(r.Choices![0].Token == 0, "native geometry rejection searches a different valid solution");
        p.Mandatory[0].Value = 20; r = Solve(p); Check(r.Choices == null, "equality does not satisfy native strict >");
        p = Base(new[] { Part(1, firepower: 10) }, new[] { Part(1, firepower: 10) });
        p.Mandatory = new[] { new Requirement { Stat = 0, Value = 29 } }; Check(Solve(p).Choices == null, "one actual part cannot be used twice");
        p = Base(new[] { Part(1, firepower: 10, special: true, id: "same") }, new[] { Part(2, firepower: 10, special: true, id: "same") });
        Check(Solve(p).Choices == null, "native special identifier duplicates rejected even with two copies");
        p = Base(new[] { Part(1, firepower: 10, group: "springs") }, new[] { Part(2, firepower: 10, group: "springs") });
        Check(Solve(p).Choices == null, "native exclusive groups rejected across different identifiers");
        p = Base(new[] { new Option(), Part(1, firepower: 10) }); p.Mandatory = new[] { new Requirement { Stat = 7, Value = 10, Equal = true } };
        Check(Solve(p).Choices == null, "accessories cannot fake caliber change");
        var makeshift = Part(2); makeshift.Suppression = 1; var proper = Part(3); proper.Suppression = 2;
        p = Base(new[] { new Option(), makeshift, proper }); p.Mandatory = new[] { new Requirement { Stat = 6, Value = 1 } };
        Check(Solve(p).Choices![0].Token == 3, "proper-suppressor order cannot be fulfilled with makeshift");
        var minus30 = Part(1); minus30.Flat[3] = -30; var minus31 = Part(2); minus31.Flat[3] = -31;
        p = Base(new[] { minus30, minus31 }); p.Mandatory = new[] { new Requirement { Stat = 3, Value = 30, Larger = false } };
        Check(Solve(p).Choices![0].Token == 2, "strict lower requirement uses 29, not equal 30");
        var keep = Part(1); keep.IsNew = false; keep.Changes = 0;
        p = Base(new[] { new Option { Changes = 1 }, keep }); r = Solve(p);
        Check(r.Choices![0] == keep && r.Changes == 0, "preserve original installed part when no improvement");
        p = Base(new[] { new Option() }); p.Mandatory = new[] { new Requirement { Stat = 0, Value = 10 } };
        Check(Solve(p).Choices == null, "locked/frozen slot with no compatible choice cannot produce a fake solution");
        p = Base(new[] { Part(1), Part(2) }, new[] { Part(3), Part(4) }); p.MaxNodes = 1; r = Solve(p);
        Check(r.SearchLimited && r.Choices == null && r.Nodes == 1, "node budget yields explicit incomplete search without partial plan");
        p = Base(new[] { Part(1, firepower: 10), Part(2, accuracy: 10) });
        p.Bonus = new[] { new Requirement { Stat = 0, Value = 19, Reward = 20 }, new Requirement { Stat = 5, Value = 19, Reward = 50 } };
        Check(Solve(p).Choices![0].Token == 2, "bonus reward has priority over optional part cost");
    }

    private static void RandomPlans()
    {
        var random = new Random(92332);
        for (int test = 0; test < 160; test++)
        {
            var p = Base(); p.BaseStats = Enumerable.Range(0, 6).Select(_ => random.Next(1, 100)).ToArray(); p.ConditionPercent = random.Next(-30, 31);
            p.Slots = Enumerable.Range(0, 4).Select(slot => Enumerable.Range(0, 4).Select(i => i == 0 ? new Option() : new Option {
                Token = slot * 4 + i, Identifier = "mod_" + random.Next(6), ExclusiveGroup = random.Next(4) == 0 ? "group_" + random.Next(2) : "", Special = slot >= 2,
                Percent = Enumerable.Range(0, 6).Select(_ => random.Next(-50, 51)).ToArray(), Flat = Enumerable.Range(0, 6).Select(_ => random.Next(-20, 21)).ToArray(),
                IsNew = true, Changes = 1, Cost = random.Next(1, 100) }).ToArray()).ToArray();
            p.Mandatory = Enumerable.Range(0, 2).Select(_ => new Requirement { Stat = random.Next(6), Value = random.Next(0, 100), Larger = random.Next(2) == 0 }).ToArray();
            p.Bonus = Enumerable.Range(0, 3).Select(_ => new Requirement { Stat = random.Next(6), Value = random.Next(0, 100), Larger = random.Next(2) == 0, Reward = random.Next(1, 100) }).ToArray();
            var brute = Brute(p); var sliced = Solve(p, 1); var batched = Solve(p, 1000);
            Check((brute == null) == (sliced.Choices == null), "random exhaustive existence agrees, including negative modifiers and lower requirements");
            Check((batched.Choices == null) == (sliced.Choices == null), "frame slicing does not change existence");
            if (brute != null)
            {
                Check(Score(p, brute).SequenceEqual(Score(p, sliced.Choices!)), "random exhaustive best reward/cost/count/change agrees");
                Check(Score(p, batched.Choices!).SequenceEqual(Score(p, sliced.Choices!)), "frame slicing preserves best score");
            }
        }
    }
    private static Option[]? Brute(Problem p)
    {
        Option[]? best = null; var selected = new Option[p.Slots.Length];
        void Visit(int slot)
        {
            if (slot == selected.Length)
            {
                var used = selected.Where(o => o.Token >= 0).ToArray();
                if (used.Select(o => o.Token).Distinct().Count() != used.Length) return;
                for (int i = 0; i < used.Length; i++) for (int j = i + 1; j < used.Length; j++)
                    if (used[i].ExclusiveGroup != "" && used[i].ExclusiveGroup == used[j].ExclusiveGroup ||
                        (used[i].Special || used[j].Special) && used[i].Identifier == used[j].Identifier) return;
                var values = GunModificationPlanner.Calculate(p.BaseStats, p.ConditionPercent, p.Caliber, p.BaseCells, selected);
                if (p.Mandatory.Any(r => !r.IsMet(values[r.Stat]))) return;
                if (best == null || LexGreater(Score(p, selected), Score(p, best))) best = selected.ToArray();
                return;
            }
            foreach (var option in p.Slots[slot]) { selected[slot] = option; Visit(slot + 1); }
        }
        Visit(0); return best;
    }
    private static long[] Score(Problem p, Option[] selected)
    {
        var values = GunModificationPlanner.Calculate(p.BaseStats, p.ConditionPercent, p.Caliber, p.BaseCells, selected);
        var met = p.Bonus.Where(r => r.IsMet(values[r.Stat])).ToArray();
        return new[] { met.Sum(r => (long)r.Reward), met.Length, -selected.Where(o => o.Token >= 0 && o.IsNew).Sum(o => o.Cost),
            -selected.Count(o => o.Token >= 0 && o.IsNew), -selected.Sum(o => (long)o.Changes) };
    }
    private static bool LexGreater(long[] a, long[] b) { for (int i = 0; i < a.Length; i++) { if (a[i] > b[i]) return true; if (a[i] < b[i]) return false; } return false; }

    private static void Transactions()
    {
        // The production recovery coordinator runs here with fault-injected transfer adapters.
        // Native Unity inventory callbacks are outside this data-only test's coverage.
        foreach (int failAt in new[] { -1, 0, 1, 2, 3 })
        {
            var original = new Dictionary<string, string> { ["old"] = "gun", ["new"] = "stock" };
            var live = new Dictionary<string, string>(original); var restored = new List<string>(); int calls = 0;
            void Fault() { if (calls++ == failAt) throw new InvalidOperationException("injected native callback failure"); }
            var result = WholeItemTransferTransaction.Run(new[] { "new", "old" }, () =>
            {
                Fault(); live["old"] = "return"; Fault(); live["new"] = "gun"; Fault();
                if (failAt == 3) throw new InvalidOperationException("postcondition rejected");
            }, item => { live.Remove(item); }, item =>
            {
                Check(live.Values.All(v => v != original[item]), "all changed items detached before any original slot is restored");
                live[item] = original[item]; restored.Add(item);
            }, () => Check(original.All(p => live.TryGetValue(p.Key, out var value) && value == p.Value), "all original item identities and locations recovered"));
            if (failAt < 0) Check(result.Succeeded && !result.Restored && live["old"] == "return" && live["new"] == "gun", "successful same-frame transfer preserves final real identities");
            else Check(!result.Succeeded && result.Restored && result.Error != null && restored.SequenceEqual(new[] { "new", "old" }), "any transfer/final validation failure restores both original locations in source-first order");
        }
        var failedRestore = WholeItemTransferTransaction.Run(new[] { "item" }, () => throw new InvalidOperationException("apply"), _ => { },
            _ => throw new InvalidOperationException("restore"), () => { });
        Check(!failedRestore.Succeeded && !failedRestore.Restored && failedRestore.RestoreError?.Message == "restore", "recovery failure is explicit, never claimed as restored");
    }

    private static T Native<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static void Field(object target, string name, object? value)
    {
        for (Type? type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly) ??
                type.GetField("<" + name + ">k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (field != null) { field.SetValue(target, value); return; }
        }
        throw new MissingFieldException(name);
    }
    private static GameGridInventory Grid()
    {
        var grid = Native<GameGridInventory>(); Field(grid, "items", new List<GameItem>());
        Field(grid, "inventoryShape", new GridShapeBuilder(20, 20).SetDataFill(0)); return grid;
    }
    private static void Put(GameGridInventory inventory, GameItem item)
    {
        ((List<GameItem>)typeof(GameGridInventory).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(inventory)!).Add(item);
        item.parentInventory = inventory;
    }
    private static PixelWindow Container(DataItem owner, GameGridInventory grid)
    {
        var window = Native<PixelWindow>(); Field(window, "parentItems", new List<GameItem> { owner }); Field(window, "childElement", grid);
        Field(owner, "contentWindow", window); Field(grid, "parent", window); owner.Nodes.Add(window); return window;
    }
    private static void ProductionAccess()
    {
        var production = typeof(DeepSpaceQoL.ModEntry).Assembly.GetType("DeepSpaceQoL.GunModdingService")!;
        var canModify = production.GetMethod("CanModifyGun", BindingFlags.NonPublic | BindingFlags.Static)!;
        var loose = production.GetMethod("IsLooseCandidate", BindingFlags.NonPublic | BindingFlags.Static)!;
        bool Can(GameItem item) => (bool)canModify.Invoke(null, new object[] { item })!;
        bool Candidate(GameItem item, GameInventory inventory) => (bool)loose.Invoke(null, new object[] { item, inventory })!;
        var store = Native<PlayerStore>(); typeof(PlayerStore).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, store);
        var entry = Native<EmporiumEntry>(); Field(entry, "m_CachedPtr", new IntPtr(1));
        entry.invElement = Grid(); Field(entry, "frontInvinvElement", Grid()); Field(entry, "backInvinvElement", Grid()); Field(entry, "backInvinvElementCounter", Grid());
        typeof(EmporiumEntry).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, entry);
        var gun = Item("handmade_pistol"); GunHelper.InitGun(gun, 20, 15, 30, 65, 50, 30, 22, 100);
        Set(gun, "SERVICE_GUN_TAG", 0); gun.modifiedState = gun.state; Put(entry.frontInvinvElement, gun);
        Check(!gun.IsTag("IMPORTANT_TAG") && GunHelper.IsServiceGun(gun) && !GeneralHelper.IsItemOwned(gun), "native gun init plus CreateServiceGun service tag does not invent IMPORTANT or player ownership");
        Check(Can(gun), "production CanModifyGun accepts native non-owned service gun");
        gun.state.dict.Remove("SERVICE_GUN_TAG"); gun.modifiedState = gun.state; Check(!Can(gun), "non-owned ordinary gun rejected");
        Set(gun, "IS_OWNED_TAG", 0); gun.modifiedState = gun.state; Check(Can(gun), "player-owned ordinary commission gun allowed");
        foreach (string tag in new[] { "PINNED_TAG", "IMPORTANT_TAG", "LOU_TOY_TAG", "LOU_TOY_BOX_TAG", "LOU_LOCKER_TAG" })
        {
            Set(gun, tag, 0); gun.modifiedState = gun.state; Check(!Can(gun), "protected/fixed target gun rejected: " + tag);
            gun.state.dict.Remove(tag); gun.modifiedState = gun.state;
        }
        Set(gun, "DEEPSPACE_QOL_STORAGE_V1", 0); gun.state.dict["DEEPSPACE_QOL_STORAGE_V1"].valueString = "{\"Version\":1,\"ContainerLocked\":true}"; gun.modifiedState = gun.state;
        Check(!Can(gun), "target gun's explicit automation lock respected");
        gun.state.dict.Remove("DEEPSPACE_QOL_STORAGE_V1"); gun.modifiedState = gun.state;
        var part = Item("rds"); Set(part, "WEAPON_MOD_TAG", 0); Set(part, "IS_OWNED_TAG", 0); part.itemTypes.Add("GUN_MOD"); part.modifiedState = part.state; Put(entry.invElement, part);
        Check(Candidate(part, entry.invElement), "production loose candidate accepts owned loose native GUN_MOD in root stock");
        Check(!Candidate(part, entry.backInvinvElement), "wrong source reference never accepted");
        foreach (string tag in new[] { "PINNED_TAG", "IMPORTANT_TAG", "LOU_TOY_TAG", "LOU_TOY_BOX_TAG", "LOU_LOCKER_TAG", "ITEM_BEHIND_LOCK_TAG" })
        {
            Set(part, tag, 0); part.modifiedState = part.state; Check(!Candidate(part, entry.invElement), "protected/fixed/behind-lock accessory excluded: " + tag);
            part.state.dict.Remove(tag); part.modifiedState = part.state;
        }
        part.state.dict.Remove("IS_OWNED_TAG"); part.modifiedState = part.state; Check(!Candidate(part, entry.invElement), "customer-owned accessory excluded");
        Set(part, "IS_OWNED_TAG", 0); part.modifiedState = part.state; part.unitCount = 2;
        Check(!Candidate(part, entry.invElement), "stack is never implicitly split/duplicated"); part.unitCount = 1;
        entry.invElement.overrideLockRemove = true; Check(!Candidate(part, entry.invElement), "native source removal lock respected"); entry.invElement.overrideLockRemove = false;
        var functional = Item("water_purifier"); Set(functional, "IS_OWNED_TAG", 0); functional.modifiedState = functional.state;
        var machineGrid = Grid(); Container(functional, machineGrid); part.parentInventory = machineGrid;
        Check(!Candidate(part, machineGrid), "machine input/output grid excluded even when part is owned");
        var otherGun = Item("smg"); Set(otherGun, "IS_OWNED_TAG", 0); otherGun.modifiedState = otherGun.state;
        var gunSlot = Native<GameSlotInventory>(); Field(gunSlot, "currentItem", part);
        var gunWindow = Native<PixelWindow>(); Field(gunWindow, "parentItems", new List<GameItem> { otherGun }); Field(gunWindow, "childElement", gunSlot); Field(gunSlot, "parent", gunWindow);
        part.parentInventory = gunSlot; Check(!Candidate(part, gunSlot), "never strip accessories from another gun's functional slot");
        var box = Item("service_box"); Set(box, "IS_OWNED_TAG", 0); box.modifiedState = box.state;
        var boxGrid = Grid(); Container(box, boxGrid); part.parentInventory = boxGrid; Put(entry.invElement, box);
        Check(Candidate(part, boxGrid), "audited ordinary storage box contents allowed");
        Set(box, "DEEPSPACE_QOL_STORAGE_V1", 0); box.state.dict["DEEPSPACE_QOL_STORAGE_V1"].valueString = "{\"Version\":1,\"ContainerLocked\":true}"; box.modifiedState = box.state;
        Check(!Candidate(part, boxGrid), "container automation lock excludes all nested candidate parts");
        box.state.dict.Remove("DEEPSPACE_QOL_STORAGE_V1"); box.modifiedState = box.state; Set(box, "LOU_TOY_BOX_TAG", 0); box.modifiedState = box.state;
        Check(!Candidate(part, boxGrid), "Lou box contents always excluded, independent of player locks");
        var originalState = new TagSystem(); originalState.dict["int"] = new TagState("int", "int") { valueEnabled = true, valueInt = 17 };
        var deep = (TagSystem)production.GetMethod("DeepState", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { originalState })!;
        originalState.dict["int"].valueInt = 99; Check(deep.dict["int"].valueInt == 17 && !ReferenceEquals(deep.dict["int"], originalState.dict["int"]), "production rollback state deep-copies mutable native TagState values");
    }

    private static void NativeSlotsAndSession()
    {
        var production = typeof(DeepSpaceQoL.ModEntry).Assembly.GetType("DeepSpaceQoL.GunModdingService")!;
        typeof(DeepSpaceQoL.ModEntry).GetField("Enabled", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, true);
        var entry = EmporiumEntry.Instance;
        var gun = Item("handmade_pistol"); GunHelper.InitGun(gun, 20, 15, 30, 65, 50, 30, 22, 100);
        Set(gun, "SERVICE_GUN_TAG", 0); gun.modifiedState = gun.state; Put(entry.frontInvinvElement, gun);
        var row = Native<MachineryPanelRow>();
        var roles = new[] { "mech_gun_tag_stock", "mech_gun_tag_rail", "mech_gun_tag_magazine", "mech_gun_tag_underbarrel", "mech_gun_tag_barrel", "mech_gun_tag_special1", "mech_gun_tag_special2", "mech_gun_tag_special3" };
        var panels = new List<MachineryPanel>(); var slots = new List<GameSlotInventory>();
        foreach (string role in roles)
        {
            var slot = Native<GameSlotInventory>(); var intake = Native<MachineryIntake>(); Field(intake, "content", slot);
            var grid = Native<GridPixelElement>(); Field(grid, "childElements", new PixelElement[] { intake }); Field(grid, "gridWidth", 1); Field(grid, "gridHeight", 1);
            var panel = Native<MachineryPanel>(); panel.role = role; Field(panel, "content", grid); panels.Add(panel); slots.Add(slot);
            Field(slot, "parent", grid); grid.parent = row;
        }
        Field(row, "panels", panels);
        var window = Native<PixelWindow>(); Field(window, "parentItems", new List<GameItem> { gun }); Field(window, "childElement", row); row.parent = window;
        Field(gun, "contentWindow", window); gun.Nodes.Add(window);
        GunModHelper.EnableStockSlot(slots[0]); GunModHelper.EnableRailSlot(slots[1]); GunModHelper.EnableMagazineSlot(slots[2]);
        GunModHelper.EnableUnderbarrelSlot(slots[3]); GunModHelper.EnableBarrelSlot(slots[4]);
        for (int i = 5; i < 8; i++) GunModHelper.EnableSpecialSlot(slots[i]);
        var accessory = Item("rds"); Set(accessory, "MOD_RAIL_TAG", 0); Set(accessory, "WEAPON_MOD_TAG", 0); Set(accessory, "IS_OWNED_TAG", 0); accessory.modifiedState = accessory.state;
        Check(slots[1].MayHaveValidInventorySlot(accessory), "real native rail slot accepts owned compatible part on non-owned service gun");
        Check(!slots[2].MayHaveValidInventorySlot(accessory), "real native magazine slot rejects rail-tagged part");
        GunModHelper.AllowOnlyWeapon(new[] { "smg" }, accessory);
        Check(!slots[1].MayHaveValidInventorySlot(accessory), "real native part-specific weapon predicate rejects incompatible receiver");
        GunModHelper.AllowOnlyWeapon(new[] { "handmade_pistol" }, accessory);
        Check(slots[1].MayHaveValidInventorySlot(accessory), "real native part-specific weapon predicate permits matching receiver");
        accessory.state.dict.Remove("IS_OWNED_TAG"); accessory.modifiedState = accessory.state;
        Check(!slots[1].MayHaveValidInventorySlot(accessory), "real native slot requires player-owned replacement part");
        Set(accessory, "IS_OWNED_TAG", 0); accessory.modifiedState = accessory.state; accessory.parentInventory = slots[1]; Field(slots[1], "currentItem", accessory);
        Check(accessory.MayRemove() && !GeneralHelper.IsInsideNonOwnedContainer(accessory), "native service ownership exception allows recovering player's installed part");
        accessory.state.dict.Remove("IS_OWNED_TAG"); accessory.modifiedState = accessory.state;
        Check(!accessory.MayRemove() && GeneralHelper.IsInsideNonOwnedContainer(accessory), "native original customer-owned service part cannot be removed");
        Field(slots[1], "currentItem", null); accessory.parentInventory = null;
        var oldSpring = Item("test_spring_old"); Set(oldSpring, "MOD_SPECIAL_TAG", 0); Set(oldSpring, "WEAPON_MOD_TAG", 0); Set(oldSpring, "IS_OWNED_TAG", 0); oldSpring.modifiedState = oldSpring.state;
        oldSpring.parentInventory = slots[5]; Field(slots[5], "currentItem", oldSpring);
        var newSpring = Item("test_spring_new"); Set(newSpring, "MOD_SPECIAL_TAG", 0); Set(newSpring, "WEAPON_MOD_TAG", 0); Set(newSpring, "IS_OWNED_TAG", 0); newSpring.modifiedState = newSpring.state;
        GunModHelper.RegisterExclusiveGroup("test_springs", "test_spring_old", "test_spring_new");
        Check(!slots[6].MayHaveValidInventorySlot(newSpring), "real native exclusive group blocks a different spring in another slot");
        Check(slots[5].MayHaveValidInventorySlot(newSpring), "real native target-slot replacement ignores outgoing conflicting part");
        newSpring.identifier = oldSpring.identifier;
        Check(!slots[6].MayHaveValidInventorySlot(newSpring), "real native special slot rejects another copy of same identifier");
        Field(slots[5], "currentItem", null); oldSpring.parentInventory = null;
        var order = new GunOrderData { isServiceOrder = true };
        order.mandatoryRequirements.Add(new GunOrderRequirement { id = Current[0], value = 29 });
        typeof(GunOrderData).GetField("serviceGun", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(order, gun);
        var client = new StoreClient { clientIntent = StoreClient.ClientIntent.GUNSMITH, gunOrderData = order };
        var instance = Native<StoreClientInstance>(); Field(instance, "storeClient", client);
        PlayerStore.Instance.currentClientInstance = instance; PlayerStore.Instance.isClientArrived = true;
        object?[] arguments = { null, "" };
        Check((bool)production.GetMethod("TrySession", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments)!, "production resolves only the actual current service order gun and all eight native slots");
        var foreign = Item("handmade_pistol"); Set(foreign, "SERVICE_GUN_TAG", 0); Set(foreign, "ADV_GUN_TAG", 0); foreign.modifiedState = foreign.state; Put(entry.frontInvinvElement, foreign);
        Check(ReferenceEquals(GunOrderData.ResolveOrderGun(), gun), "another gun carrying a service tag cannot replace the current customer's registered gun");
        Set(gun, "PINNED_TAG", 0); gun.modifiedState = gun.state; arguments = new object?[] { null, "" };
        Check(!(bool)production.GetMethod("TrySession", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments)!, "production session rejects pinned actual service gun before any movement");
        typeof(DeepSpaceQoL.ModEntry).GetField("Enabled", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, false);
    }
}

// Real native GameItem implementation without rendering; native GunHelper/GraphUtils/TagSystem execute.
sealed class DataItem : GameItem
{
    public DataItem() : base("fixture") { }
    internal readonly List<GraphNodeStorage> Nodes = new();
    public override List<GraphNodeStorage> children => Nodes;
    public override void Destroy() => throw new InvalidOperationException("Tests must never destroy an item.");
    public override void Validate() { }
}
