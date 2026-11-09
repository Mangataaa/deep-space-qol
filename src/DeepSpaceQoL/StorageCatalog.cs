using DeepSpaceQoL.Core;

namespace DeepSpaceQoL;

internal sealed class StoragePage
{
    public GameItem? Owner { get; }
    public int Index { get; }
    public GameGridInventory Inventory { get; }
    private readonly string rootName;
    public StoragePage(GameItem? owner, int index, GameGridInventory inventory, string rootName = "主库存（散放区）")
    { Owner = owner; Index = index; Inventory = inventory; this.rootName = rootName; }
    public PageRule Rule => Owner == null ? new PageRule() : StorageRules.Policy(Owner).Page(Index);
    public string Name => Owner == null ? rootName :
        $"{Owner.GetDisplayName(true)} #{Owner.uniqueId} / {StorageExpansionHelper.GetTabName(Owner, Index) ?? (!string.IsNullOrEmpty(Rule.Label) ? Rule.Label : "页 " + (Index + 1))}";
}

/// <summary>Owned shop inventories and storage pages; excludes loose hidden/showcase goods and trade offers.</summary>
internal static class StorageCatalog
{
    // Native STORAGE/CONTAINER_TAG also describe machine bays and functional equipment.
    // Only audited containers with ordinary storage pages may participate in automation.
    private static readonly HashSet<string> StorageIdentifiers = new(StringComparer.Ordinal)
    {
        "makeshift_storage_bay", "storage_bay", "storage_bay_large", "storage_bay_gun", "storage_bay_chem",
        "smuggler_bay_mini", "smuggler_bay", "smuggler_bay_mod", "hypertech_bay",
        "fanny_pack", "satchel", "kids_backpack", "kids_backpack_large", "backpack_small",
        "backpack_medium", "backpack_medium_military", "backpack_large", "backpack_large_military",
        "sec_box", "evidence_box", "lou_locker", "med_box", "eng_box", "service_box", "beerCase",
        // Still registered legacy containers have the same direct-grid storage contract.
        "lootbox_small", "lootbox_medium", "lootbox_large", "safe", "large_storage_bay",
        "pouch_general", "pouch_general_large", "firstaid_box"
    };

    private static readonly HashSet<string> TabbedStorageIdentifiers = new(StringComparer.Ordinal)
    { "storage_bay", "storage_bay_large", "storage_bay_gun", "storage_bay_chem", "hypertech_bay" };

    public static IReadOnlyList<StoragePage> GetPages()
    {
        var entry = EmporiumEntry.Instance;
        var root = entry?.invElement;
        var result = new List<StoragePage>();
        if (root == null) return result;
        var seen = new HashSet<GameInventory>();
        var pending = new Queue<StoragePage>();
        pending.Enqueue(new StoragePage(null, 0, root));
        if (entry!.frontInvinvElement != null) pending.Enqueue(new StoragePage(null, 0, entry.frontInvinvElement, "前柜台（仅自有物品）"));
        if (entry.backInvinvElementCounter != null) pending.Enqueue(new StoragePage(null, 0, entry.backInvinvElementCounter, "后工作台（非交易称重区）"));
        // Expose boxes in other owned locations without sweeping their loose display/hidden goods.
        foreach (var secondary in new[] { entry.hiddenElement, entry.showcaseElement })
            if (secondary != null)
                foreach (var item in secondary.childItems.ToArray())
                    if (GeneralHelper.IsItemOwned(item) && IsStorage(item))
                        foreach (var page in PagesOf(item)) pending.Enqueue(page);
        while (pending.Count > 0 && seen.Count < 512)
        {
            var page = pending.Dequeue();
            if (!seen.Add(page.Inventory)) continue;
            result.Add(page);
            foreach (var item in page.Inventory.childItems.ToArray())
            {
                if (item == null || !GeneralHelper.IsItemOwned(item) || !IsStorage(item)) continue;
                foreach (var nested in PagesOf(item)) pending.Enqueue(nested);
            }
        }
        return result;
    }

    public static IReadOnlyList<GameInventory> GetSources() => GetPages()
        .Where(p => StorageRules.CanAutoAccess(p.Inventory))
        .Select(p => (GameInventory)p.Inventory).ToArray();

    /// <summary>Only genuine container pages; root stock areas remain sources, not storage targets.</summary>
    public static IReadOnlyList<StoragePage> GetStoragePages() => GetPages().Where(p => p.Owner != null).ToArray();

    /// <summary>Native sealed windows protect all nested storage, independently of mod sale locks.</summary>
    internal static bool IsNativeAccessLocked(GameInventory inventory) =>
        inventory.IsInsertLocked() || inventory.IsRemoveLocked() ||
        GraphUtils.FindParentsType<GameInventory>(inventory, passthrough: _ => true).Any(i => i.IsInsertLocked() || i.IsRemoveLocked()) ||
        GraphUtils.FindParentsType<PixelWindow>(inventory, passthrough: _ => true).Any(w => w.isLocked) ||
        GraphUtils.FindParentsType<GameItem>(inventory, passthrough: _ => true).Any(LockHelper.IsCurrentlyLocked);

    public static bool IsStorage(GameItem item) => PagesOf(item).Count != 0;

    private static bool HasStorageIdentity(GameItem owner) => owner != null &&
        owner.identifier != null && StorageIdentifiers.Contains(owner.identifier) &&
        !(owner is GameCharacterItem) && !owner.IsGameItemType("MACHINE");

    internal static MachineryTabs? TabsOf(GameItem owner) => HasStorageIdentity(owner) &&
        TabbedStorageIdentifiers.Contains(owner.identifier)
            ? (owner.contentWindow?.childElement as MachineryPanelRow)?.GetPanel("storage")?.content as MachineryTabs
            : null;

    public static IReadOnlyList<StoragePage> PagesOf(GameItem owner)
    {
        var result = new List<StoragePage>();
        if (!HasStorageIdentity(owner) || owner.contentWindow == null) return result;
        if (owner.contentWindow.childElement is GameGridInventory direct)
        {
            result.Add(new StoragePage(owner, 0, direct));
            return result;
        }
        var tabs = TabsOf(owner);
        if (tabs != null)
        {
            for (int i = 0; i < tabs.count; i++)
            {
                var element = tabs.GetPage(i);
                var grid = element as GameGridInventory ?? (element as MachineryIntake)?.content as GameGridInventory;
                if (grid != null && !result.Any(p => ReferenceEquals(p.Inventory, grid)))
                    result.Add(new StoragePage(owner, i, grid));
            }
        }
        // No recursive fallback: a nested grid might be a machine input/output or equipment slot.
        return result;
    }

    public static StoragePage? Find(GameInventory inventory)
    {
        var owner = inventory.GetParentItem();
        return owner == null ? null : PagesOf(owner).FirstOrDefault(p => ReferenceEquals(p.Inventory, inventory));
    }
}
