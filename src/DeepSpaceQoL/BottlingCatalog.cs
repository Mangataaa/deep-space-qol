namespace DeepSpaceQoL;

/// <summary>Audited locations for in-place liquid operations, separate from movable stock.</summary>
internal static class BottlingCatalog
{
    internal const string BottlePrinterId = "bottle_printer";

    private static GameGridInventory? MachineBay(GameItem owner)
    {
        // ShipSystemDirectory's two native installation bays have direct storage grids.
        // These are traversal paths to printers, NEVER ordinary liquid/stow inventories.
        if (owner.identifier != "machine_bay" && owner.identifier != "machine_bay_ext" ||
            owner is GameCharacterItem || owner is GameItemElement element && element.IsDestroyed()) return null;
        var grid = owner.contentWindow?.childElement as GameGridInventory;
        return grid != null && ReferenceEquals(grid.GetParentItem(), owner) ? grid : null;
    }

    internal static GameGridInventory? PrinterOutput(GameItem? owner)
    {
        if (owner == null || owner.identifier != BottlePrinterId || owner is GameCharacterItem ||
            owner is GameItemElement element && element.IsDestroyed()) return null;
        // MachineHelper.Panel wraps the output grid in ONE MachineryIntake by default.
        // Unwrap that audited visual shell only; never recursively accept arbitrary machine content.
        var content = (owner.contentWindow?.childElement as MachineryPanelRow)?.GetPanel("output")?.content;
        var output = content as GameGridInventory ?? (content as MachineryIntake)?.content as GameGridInventory;
        return output != null && ReferenceEquals(output.GetParentItem(), owner) ? output : null;
    }

    internal static IReadOnlyList<GameInventory> GetInventories()
    {
        var ordinary = StorageCatalog.GetSources();
        var inventories = ordinary.ToList();
        var entry = EmporiumEntry.Instance;
        var trade = entry?.backInvinvElement;
        if (trade != null) inventories.Add(trade);
        // Inspect direct printers and printers installed directly in the two audited machine bays.
        // Traversing a bay must not expose its loose goods or other equipment inventories.
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
                var bay = MachineBay(item);
                if (bay == null) continue;
                foreach (var installed in bay.childItems.ToArray())
                {
                    if (installed == null || !ReferenceEquals(installed.parentInventory, bay) ||
                        !GeneralHelper.IsItemEffectivelyOwned(installed)) continue;
                    var installedOutput = PrinterOutput(installed);
                    if (installedOutput != null) inventories.Add(installedOutput);
                }
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
