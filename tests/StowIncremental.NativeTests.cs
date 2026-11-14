using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using DeepSpaceQoL;

string managed = args.Length > 0 ? Path.GetFullPath(args[0]) : @"D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed";
AssemblyLoadContext.Default.Resolving += (_, name) => {
    string path = Path.Combine(managed, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
NativeStowTests.Run();

static class NativeStowTests
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
    private static T Native<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
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
    private static GameItem Item(string id, params string[] types)
    {
        var item = Native<GameItemElement>();
        item.identifier = id; item.unitCount = 1; item.itemTypes = types.ToList(); item.itemFeatures = new();
        item.state = new TagSystem(); item.modifiedState = item.state;
        item.state.dict["IS_OWNED_TAG"] = new TagState("IS_OWNED_TAG", "owned") { valueEnabled = true };
        foreach (string type in types) item.state.dict[type] = new TagState(type, type) { valueEnabled = true };
        item.shape = new GridShapeBuilder(1, 1).SetDataFill(1).Build(); item.modifiedShape = item.shape.Clone();
        var handler = Native<TreeNodeRender>(); Set(handler, "m_CachedPtr", new IntPtr(1)); Set(item, "handler", handler);
        return item;
    }
    private static GameGridInventory Grid()
    {
        var grid = Native<GameGridInventory>(); Set(grid, "items", new List<GameItem>());
        Set(grid, "inventoryShape", new GridShapeBuilder(40, 40).SetDataFill(0).SetDataOutside(1).Build());
        return grid;
    }
    private static void Put(GameGridInventory grid, GameItem item)
    {
        ((List<GameItem>)typeof(GameGridInventory).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(grid)!).Add(item);
        item.parentInventory = grid;
    }
    private static GameItem Cabinet(GameGridInventory grid)
    {
        var owner = Item("sec_box", "STORAGE", "CONTAINER_TAG", "IMPORTANT_TAG");
        var window = Native<PixelWindow>(); Set(window, "childElement", grid); Set(window, "parentItems", new List<GameItem> { owner });
        Set(owner, "contentWindow", window); Set(grid, "parent", window); return owner;
    }
    private static void SetEntry(EmporiumEntry? entry) => typeof(EmporiumEntry)
        .GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, entry);
    private static IEnumerable<object?> Steps(Func<bool> active, Action<string> report) =>
        (IEnumerable<object?>)typeof(StowService).GetMethod("RunSteps", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { false, active, report })!;

    internal static void Run()
    {
        var entry = Native<EmporiumEntry>(); entry.invElement = Grid(); SetEntry(entry);
        var target = Grid(); var owner = Cabinet(target); Put(entry.invElement, owner);
        var page = StorageCatalog.PagesOf(owner).Single();
        page.Rule.Categories = "tag:WATER";
        var stock = Item("bottled_water", "WATER", "LIQUID_CONTAINER_TAG"); Put(entry.invElement, stock);
        var canPlace = typeof(StowService).GetMethod("CanPlace", BindingFlags.Static | BindingFlags.NonPublic)!;
        bool Allowed() => (bool)canPlace.Invoke(null, new object[] { page, stock, entry.invElement })!;
        Check(Allowed(), "A matching unlocked page should be a current destination.");
        // Same StoragePage instance, changed live rules: no stale cached labels/locks.
        page.Rule.Categories = "tag:FOOD"; page.Rule.Restrict = false;
        Check(!Allowed(), "An in-flight destination must re-read changed labels even with manual restriction disabled.");
        page.Rule.Categories = "tag:WATER"; page.Rule.SaleLocked = true;
        Check(!Allowed(), "An in-flight destination must obey a newly set page lock.");
        page.Rule.SaleLocked = false; StorageRules.Policy(owner).ContainerLocked = true;
        Check(!Allowed(), "An in-flight destination must obey a newly set whole-container lock.");
        StorageRules.Policy(owner).ContainerLocked = false;
        ((List<GameItem>)typeof(GameGridInventory).GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(entry.invElement)!).Remove(stock);
        Put(target, stock);
        Check(!Allowed(), "A pending move cannot chase an item manually moved to a different source.");

        // Actual data-only native discovery/policy/source scan + the new resumable
        // orchestration. The final scene-rendering arrangement is substituted; no
        // Unity constructors, native drag/transfer actions or live save are executed.
        for (int i = 0; i < 1199; i++)
        {
            var item = Item("bottled_water", "WATER", "LIQUID_CONTAINER_TAG");
            item.modifiedShape = new GridShapeBuilder(item.modifiedShape).SetPosition(i % 40, i / 40).Build(); Put(target, item);
        }
        string final = ""; int frames = 0; double maxSlice = 0;
        ArrangeService.Visited.Clear();
        using (var work = Steps(() => true, s => final = s).GetEnumerator())
            while (true)
            {
                var watch = Stopwatch.StartNew(); bool more = work.MoveNext(); maxSlice = Math.Max(maxSlice, watch.Elapsed.TotalMilliseconds);
                if (!more) break; frames++;
            }
        Check(frames > 1, "A large real-native source scan should yield repeatedly.");
        Check(maxSlice < 100, "The warmed data scan must not reproduce a long single-frame stall.");
        Check(final.Contains("已归位 1200 件"), "Cross-frame collection must visit all candidates, without the former two-second abandonment.");
        Check(ArrangeService.Visited.SequenceEqual(new[] { target }), "Only the unlocked actual storage page with stock should be tidied.");
        Check(target.childItems.Count == 1200 && target.childItems.All(i => ReferenceEquals(i.parentInventory, target)), "Collection changed already-stored native item membership.");

        ArrangeService.Visited.Clear(); bool active = true; bool finalReported = false;
        using (var work = Steps(() => active, _ => finalReported = true).GetEnumerator())
        {
            Check(work.MoveNext(), "Cancellation fixture should yield during scan."); active = false;
            Check(!work.MoveNext(), "Cancelled orchestration should not continue on the next frame.");
        }
        Check(!finalReported && ArrangeService.Visited.Count == 0, "Cancellation must not run arrangement or report a false success.");
        Check(target.childItems.Count == 1200, "Cancelling the scan removed items.");

        ArrangeService.Visited.Clear(); page.Rule.SaleLocked = true; final = "";
        foreach (var _ in Steps(() => true, s => final = s)) { }
        Check(ArrangeService.Visited.Count == 0 && final.Contains("解除目标页或整箱锁"), "A newly locked destination must be refused before any scan/arrangement.");
        SetEntry(null); StorageRules.ClearCache();
        Console.WriteLine($"Stow incremental native: {checks} checks passed; items=1200, frames={frames}, max observed scan slice={maxSlice:F2} ms. Data-only native rules/discovery; no live inventory actions.");
    }
}

namespace DeepSpaceQoL
{
    internal static class ArrangeService
    {
        internal static readonly List<GameGridInventory> Visited = new();
        internal static bool TryArrange(GameGridInventory inventory, out string status) { status = ""; throw new InvalidOperationException("Synchronous arrangement must not be called by resumable stow."); }
        internal static IEnumerable<object?> TryArrangeSteps(GameGridInventory inventory, Func<bool> active, Action<bool, string> completed)
        {
            if (!active()) yield break; Visited.Add(inventory); yield return null;
            if (active()) completed(false, "Data-only test retains item poses.");
        }
    }
    internal static class ModEntry
    {
        internal const string Id = "deep_space_qol"; internal static bool Enabled => true;
        internal static TestLog? Log => null;
        internal static bool Ready(out string reason) { reason = ""; throw new InvalidOperationException("Offline tests do not enter the native UI scheduler."); }
    }
    internal static class ControlPanel { internal static void Status(string message) => throw new InvalidOperationException("Offline tests use an explicit report callback."); }
    internal sealed class TestLog { internal void Warning(string message) => throw new InvalidOperationException(message); internal void Error(Exception error) => throw error; }
}
