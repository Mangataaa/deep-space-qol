namespace DeepSpaceQoL;

/// <summary>Audited locations for in-place liquid operations, separate from movable stock.</summary>
internal static class BottlingCatalog
{
    internal const string BottlePrinterId = "bottle_printer";

    internal static GameGridInventory? PrinterOutput(GameItem? owner)
    {
        if (owner == null || owner.identifier != BottlePrinterId || owner is GameCharacterItem ||
            owner is GameItemElement element && element.IsDestroyed()) return null;
        // MachineBottlePrinter.CreateMachineInventoryWindow has a DIRECT grid in role output.
        // Never recurse into power, inputs, process vessels, arbitrary wrappers or other machines.
        var output = (owner.contentWindow?.childElement as MachineryPanelRow)?.GetPanel("output")?.content as GameGridInventory;
        return output != null && ReferenceEquals(output.GetParentItem(), owner) ? output : null;
    }

    internal static IReadOnlyList<GameInventory> GetInventories()
    {
        var ordinary = StorageCatalog.GetSources();
        var inventories = ordinary.ToList();
        var entry = EmporiumEntry.Instance;
        var trade = entry?.backInvinvElement;
        if (trade != null) inventories.Add(trade);
        // Only inspect direct machines in ordinary stock/storage, the sale counter, or visible
        // dedicated shop fixture roots. This does not admit the loose hidden/showcase goods.
        var machineRoots = inventories.Concat(new GameInventory?[] { entry?.hiddenElement, entry?.showcaseElement }
            .Where(inventory => inventory != null).Select(inventory => inventory!)).Distinct().ToArray();
        foreach (var inventory in machineRoots)
        {
            foreach (var item in inventory.childItems.ToArray())
            {
                if (item == null || !ReferenceEquals(item.parentInventory, inventory) ||
                    !GeneralHelper.IsItemEffectivelyOwned(item)) continue;
                var output = PrinterOutput(item);
                if (output != null) inventories.Add(output);
            }
        }
        return inventories.Distinct().ToArray();
    }

    internal static IReadOnlyList<GameItem> GetItems() => GetInventories()
        .SelectMany(inventory => inventory.childItems.Where(item => item != null &&
            ReferenceEquals(item.parentInventory, inventory))).Distinct().ToArray();

    internal static bool Contains(GameItem item) => item.parentInventory != null &&
        item.parentInventory.childItems.Contains(item) &&
        GetInventories().Any(inventory => ReferenceEquals(inventory, item.parentInventory));

    internal static bool IsOnTradeCounter(GameItem item)
    {
        var counter = EmporiumEntry.Instance?.backInvinvElement;
        var inventory = item.parentInventory;
        return counter != null && inventory != null && (ReferenceEquals(inventory, counter) ||
            GraphUtils.FindParentsType<GameInventory>(inventory, passthrough: _ => true)
                .Any(parent => ReferenceEquals(parent, counter)));
    }
}
