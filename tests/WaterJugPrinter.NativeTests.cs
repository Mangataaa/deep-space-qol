using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using DeepSpaceQoL;

string managed = args.Length == 1 ? Path.GetFullPath(args[0]) : @"D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed";
AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(managed, name.Name + ".dll"))
    ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed, name.Name + ".dll")) : null;
Tests.Run();

static class Tests
{
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    private static T Native<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static void Set(object instance, string name, object value)
    {
        for (var type = instance.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                ?? type.GetField("<" + name + ">k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null) { field.SetValue(instance, value); return; }
        }
        throw new MissingFieldException(name);
    }
    private static GameItem Item(string id = WaterJugPrinting.JugId, int capacity = WaterJugPrinting.CapacityParts)
    {
        var item = Native<GameItemElement>(); item.identifier = id; item.unitCount = 1;
        item.state = new TagSystem(); item.modifiedState = item.state;
        item.state.dict[WaterHelper.LIQUID_CONTAINER_TAG] = new TagState(WaterHelper.LIQUID_CONTAINER_TAG, WaterHelper.LIQUID_CONTAINER_TAG) { valueEnabled = true };
        item.state.dict[WaterHelper.LIQUID_CONTAINER_CAPACITY] = new TagState(WaterHelper.LIQUID_CONTAINER_CAPACITY, WaterHelper.LIQUID_CONTAINER_CAPACITY) { valueEnabled = true, valueInt = capacity };
        // Native IsDestroyed only compares this pointer with zero; never render/dereference it.
        var handler = Native<TreeNodeRender>(); Set(handler, "m_CachedPtr", new IntPtr(1)); Set(item, "handler", handler);
        return item;
    }
    private static MachineryPanel Panel(string role, PixelElement content)
    { var panel = Native<MachineryPanel>(); panel.role = role; Set(panel, "content", content); return panel; }
    private static MachineryIntake Wrap(PixelElement content)
    { var intake = Native<MachineryIntake>(); Set(intake, "content", content); return intake; }
    private static (GameItem item, GameSlotInventory input, GameGridInventory output, MachineryStack control) Printer(bool wrapped)
    {
        var item = Item(WaterJugPrinting.PrinterId);
        var input = Native<GameSlotInventory>(); var output = Native<GameGridInventory>();
        var control = Native<MachineryStack>(); Set(control, "children", new List<PixelElement>());
        var row = Native<MachineryPanelRow>();
        Set(row, "panels", new List<MachineryPanel> {
            Panel("power", wrapped ? Wrap(input) : input), Panel("output", wrapped ? Wrap(output) : output), Panel("control", control) });
        Set(row, "linked", new List<GraphNodeStorage>());
        foreach (var panel in row.Panels) panel.owner = row;
        var window = Native<PixelWindow>(); Set(window, "childElement", row); Set(window, "parentItems", new List<GameItem> { item });
        row.parent = window; Set(item, "contentWindow", window); row.RelinkStorage();
        return (item, input, output, control);
    }

    internal static void Run()
    {
        Check(WaterJugPrinting.PowerCost == 33, "Power cost equals three native large-bottle prints.");
        Check(WaterJugPrinting.IsRecipeJug(Item()), "The native capacity getter recognizes a 6L jug.");
        Check(!WaterJugPrinting.IsRecipeJug(null), "Null factory output is invalid.");
        Check(!WaterJugPrinting.IsRecipeJug(Item("large_bottled_water")), "Another recipe is never accepted.");
        Check(!WaterJugPrinting.IsRecipeJug(Item(capacity: 2_000_000)), "Capacity changes are not silently accepted.");
        foreach (bool wrapped in new[] { false, true })
        {
            var fixture = Printer(wrapped);
            Check(WaterJugPrinting.TryParts(fixture.item, out var power, out var output, out var control) &&
                ReferenceEquals(power, fixture.input) && ReferenceEquals(output, fixture.output) && ReferenceEquals(control, fixture.control),
                "Native panel roles and the single audited intake wrapper resolve the correct bays.");
            foreach (int upgrades in Enumerable.Range(0, 13))
            {
                fixture.item.state.dict[MachineBottlePrinter.PRINTER_UPGRADE_COUNT_TAG] = new TagState(MachineBottlePrinter.PRINTER_UPGRADE_COUNT_TAG, MachineBottlePrinter.PRINTER_UPGRADE_COUNT_TAG) { valueEnabled = true, valueInt = upgrades };
                Check(MachineBottlePrinter.IsLargeUnlocked(fixture.item) == (upgrades >= 9), "The native large-bottle unlock threshold is preserved.");
            }
            fixture.item.identifier = "another_machine";
            Check(!WaterJugPrinting.TryParts(fixture.item, out _, out _, out _), "Other machines are not targeted.");
            fixture.item.identifier = WaterJugPrinting.PrinterId;
            var foreign = Native<PixelWindow>(); Set(foreign, "parentItems", new List<GameItem>());
            Set(fixture.output, "parent", foreign);
            Check(!WaterJugPrinting.TryParts(fixture.item, out _, out _, out _), "Foreign/relocated output bays are rejected.");
        }
        foreach (bool unlocked in new[] { false, true })
        foreach (int available in Enumerable.Range(0, 67))
        foreach (bool fits in new[] { false, true })
        {
            int energy = available, created = 0, inserted = 0, charged = 0, discarded = 0;
            var result = WaterJugPrinting.PrintOne(unlocked, () => energy >= WaterJugPrinting.PowerCost,
                () => { created++; return Item(); }, _ => fits, _ => { inserted++; return true; },
                () => { charged++; energy -= WaterJugPrinting.PowerCost; return true; }, _ => discarded++, _ => true);
            bool success = unlocked && available >= WaterJugPrinting.PowerCost && fits;
            Check((result == WaterJugPrinting.Result.Printed) == success, "Upgrade, power and space all gate printing.");
            Check(energy == available - (success ? WaterJugPrinting.PowerCost : 0) && charged == (success ? 1 : 0), "Rejected prints never charge; a success charges exactly once.");
            Check(inserted == (success ? 1 : 0), "Only one complete output placement occurs.");
            Check(created == (unlocked && available >= WaterJugPrinting.PowerCost ? 1 : 0) &&
                discarded == (created == 1 && !success ? 1 : 0), "Rejected newly-created renderers are released; no orphan output.");
        }
        var events = new List<string>();
        var ok = WaterJugPrinting.PrintOne(true, () => { events.Add("check-power"); return true; },
            () => { events.Add("create"); return Item(); }, _ => { events.Add("check-fit"); return true; },
            _ => { events.Add("place"); return true; }, () => { events.Add("charge"); return true; }, _ => events.Add("discard"), _ => true);
        Check(ok == WaterJugPrinting.Result.Printed && events.SequenceEqual(new[] { "check-power", "create", "check-fit", "place", "charge" }), "Native placement completes before payment.");
        foreach (string failure in new[] { "factory", "nonempty", "place", "charge", "fit-exception", "place-exception", "charge-exception" })
        {
            int discarded = 0, charged = 0;
            try
            {
                var result = WaterJugPrinting.PrintOne(true, () => true,
                    () => Item(failure == "factory" ? "wrong_item" : WaterJugPrinting.JugId),
                    _ => failure == "fit-exception" ? throw new InvalidOperationException() : true,
                    _ => failure == "place-exception" ? throw new InvalidOperationException() : failure != "place",
                    () => { charged++; return failure == "charge-exception" ? throw new InvalidOperationException() : failure != "charge"; },
                    _ => discarded++, _ => failure != "nonempty");
                Check(result != WaterJugPrinting.Result.Printed, "Rejected operations do not report success.");
            }
            catch (InvalidOperationException) { Check(failure.EndsWith("exception"), "Only injected exceptions escape."); }
            Check(discarded == 1, "Failed output is discarded once, including exceptions.");
            if (failure is "factory" or "nonempty" or "place" or "fit-exception" or "place-exception") Check(charged == 0, "No payment precedes a valid placement.");
        }
        Console.WriteLine($"Water-jug printer native tests passed: {checks} checks; real capacity/unlock/graph getters plus production transaction. Factory/empty/placement/power callbacks are controlled fixtures; no Unity scene or saves used.");
    }
}

namespace DeepSpaceQoL
{
    internal static class ModEntry { internal static bool Enabled = true; }
}
