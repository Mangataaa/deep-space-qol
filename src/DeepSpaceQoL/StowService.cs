using System.Diagnostics;
using DeepSpaceQoL.Core;

namespace DeepSpaceQoL;

internal static class StowService
{
    private static readonly HashSet<string> PortablePackageIdentifiers = new(StringComparer.Ordinal)
    { "water_test_container", "water_tablet_container", "nutrient_tablet_container", "nutrifruit_seed_packet" };

    internal sealed class SourceScan
    {
        public List<GameItem> Candidates { get; } = new();
        public int Pinned, Locked, Equipment, Unowned, Restricted, Quest;
        public string Summary => $"跳过剧情保护 {Quest} 件、固定 {Pinned} 件、锁定 {Locked} 件、设备/箱体 {Equipment} 件、非自有 {Unowned} 件、原生不可移 {Restricted} 件。";
    }

    internal static StoragePage[] AutomaticTargets(IEnumerable<StoragePage> pages) =>
        pages.Where(p => p.Owner != null && StorageRules.CanAutoAccess(p.Inventory)).ToArray();

    internal static SourceScan ScanSources(IEnumerable<StoragePage> pages, bool reclaimOffer = false)
    {
        var result = new SourceScan();
        var inventories = pages.Select(p => (GameInventory)p.Inventory).ToList();
        var entry = EmporiumEntry.Instance;
        if (reclaimOffer && entry?.backInvinvElement != null) inventories.Add(entry.backInvinvElement);
        var seen = new HashSet<GameItem>();
        foreach (var inventory in inventories.Distinct())
        {
            bool accessible = StorageRules.CanAutoAccess(inventory);
            foreach (var item in inventory.childItems.ToArray())
            {
                if (item == null || !seen.Add(item) || !ReferenceEquals(item.parentInventory, inventory)) continue;
                if (StorageRules.IsQuestItem(item) || StorageRules.IsQuestInventory(inventory)) { result.Quest++; continue; }
                if (!GeneralHelper.IsItemOwned(item)) { result.Unowned++; continue; }
                if (!accessible || item.IsTag("ITEM_BEHIND_LOCK_TAG") || StorageRules.Policy(item).IsSaleProtected() ||
                    ((IsPortablePackage(item) || IsCompositeGood(item)) && StorageRules.HasProtectedContents(item))) { result.Locked++; continue; }
                if (ItemPinHelper.IsPinned(item)) { result.Pinned++; continue; }
                if (!IsMovableStock(item)) { result.Equipment++; continue; }
                if (!StorageRules.CanAutomate(item)) { result.Restricted++; continue; }
                result.Candidates.Add(item);
            }
        }
        return result;
    }

    public static string Run(bool reclaimOffer = false)
    {
        var pages = StorageCatalog.GetPages();
        var targets = AutomaticTargets(pages);
        if (targets.Length == 0) return "没有可自动收纳的储物页：请确认有真实储物柜/箱子，并解除目标页或整箱锁。";
        var scan = ScanSources(pages, reclaimOffer);
        var items = scan.Candidates
            .OrderBy(i => i.identifier, StringComparer.Ordinal)
            .ThenByDescending(i => i.modifiedShape?.width * i.modifiedShape?.height ?? 0).ToArray();
        int moved = 0, left = 0, alreadyStored = 0;
        var compacted = new HashSet<GameGridInventory>();
        var dirty = new HashSet<GameGridInventory>();
        var sorted = new HashSet<GameGridInventory>();
        var clock = Stopwatch.StartNew();
        foreach (var item in items)
        {
            if (clock.ElapsedMilliseconds > 2000) { left++; continue; }
            if (!StorageRules.CanAutomate(item)) { scan.Restricted++; continue; }
            var current = pages.FirstOrDefault(p => ReferenceEquals(p.Inventory, item.parentInventory));
            // Stable placement: no oscillation between two matching labels.
            if (current?.Owner != null && CategoryMatcher.HasCategories(current.Rule) && StorageRules.Matches(current.Rule, item)) { alreadyStored++; continue; }
            var matching = targets.Where(p => CategoryMatcher.HasCategories(p.Rule) && StorageRules.Matches(p.Rule, item));
            // A full matching page must not bounce existing stock between two generic boxes.
            var fallback = current?.Owner != null && !CategoryMatcher.HasCategories(current.Rule)
                ? Enumerable.Empty<StoragePage>() : targets.Where(p => !CategoryMatcher.HasCategories(p.Rule));
            var destinations = matching.Concat(fallback)
                .Where(p => !ReferenceEquals(p.Inventory, item.parentInventory) && StorageRules.CanAutoAccess(p.Inventory) && StorageRules.Allows(p.Inventory, item))
                .OrderBy(p => CategoryMatcher.HasCategories(p.Rule) ? 0 : 1)
                .ThenBy(p => p.Owner!.IsGameItemType("STORAGE") ? 0 : 1).ToArray();
            bool hasMatch = destinations.Any(p => CategoryMatcher.HasCategories(p.Rule));
            // Already in an unlabeled storage page and no category destination exists.
            if (!hasMatch && current?.Owner != null && !CategoryMatcher.HasCategories(current.Rule)) { alreadyStored++; continue; }
            bool placed = false;
            foreach (var destination in destinations)
            {
                if (clock.ElapsedMilliseconds > 2000) break;
                if (!StorageRules.CanAutoAccess(destination.Inventory)) continue;
                var slot = FindWholeSlot(destination.Inventory, item);
                if (slot == null && compacted.Add(destination.Inventory))
                {
                    if (ArrangeService.TryArrange(destination.Inventory, out _)) sorted.Add(destination.Inventory);
                    slot = FindWholeSlot(destination.Inventory, item);
                }
                if (slot == null) continue;
                MoveWhole(item, slot);
                dirty.Add(destination.Inventory);
                if (current?.Owner != null) dirty.Add(current.Inventory);
                moved++;
                placed = true;
                break;
            }
            if (!placed) left++;
        }
        // Automatically tidy storage pages only; root areas may contain installed equipment.
        foreach (var page in targets)
        {
            if (clock.ElapsedMilliseconds > 2500) break;
            // Inserting after an earlier space-making pass invalidates that layout.
            // Always tidy changed pages again once all transfers have finished.
            if (StorageRules.CanAutoAccess(page.Inventory) && (dirty.Contains(page.Inventory) || !compacted.Contains(page.Inventory)) &&
                ArrangeService.TryArrange(page.Inventory, out _)) sorted.Add(page.Inventory);
        }
        return $"已收纳 {moved} 件，整理 {sorted.Count} 页，已归位 {alreadyStored} 件；{left} 件因空间、原生限制或本轮耗时保留原处。{scan.Summary}";
    }

    /// <summary>Audited consumable packages move as whole goods; their internal grids are never storage sources/targets.</summary>
    internal static bool IsPortablePackage(GameItem item) => item != null && item.identifier != null &&
        PortablePackageIdentifiers.Contains(item.identifier) && item.contentWindow?.childElement is GameGridInventory &&
        (item.IsTag("PRE_CONTAINER_TAG") || item.IsTag("STORABLE_PACKAGE_TAG"));

    // Guns own functional ammunition/attachment inventories. Move the original whole
    // item only; these windows must never become ordinary storage sources or targets.
    internal static bool IsCompositeGood(GameItem item) => item != null &&
        item.IsGameItemType("FIREARM") && item.contentWindow != null &&
        !StorageCatalog.IsStorage(item);

    /// <summary>Move loose goods and safe portable packages, never facilities, machinery or real storage shells.</summary>
    internal static bool IsMovableStock(GameItem item) => item != null &&
        !(item is GameCharacterItem) &&
        !StorageRules.IsQuestItem(item) &&
        !item.IsTag("SYSTEM_TAG") && !item.IsTag("SYSTEM_TAG_UTILITY") &&
        !item.IsTag("machinery") && !item.IsTag("machinery_drawn_power") && !item.IsTag("BROKEN_MACHINE_TAG") &&
        !item.IsTag("NESTABLE_DEVICE_TAG") && !item.IsTag("ITEM_BEHIND_LOCK_TAG") && !item.IsGameItemType("MACHINE") &&
        !item.IsGameItemType("STORAGE") && !item.IsTag("BACKPACK_TAG") &&
        (((IsPortablePackage(item) || IsCompositeGood(item)) && !StorageRules.HasProtectedContents(item)) ||
            (item.contentWindow == null && !item.IsTag("CONTAINER_TAG") && !item.IsTag("PRE_CONTAINER_TAG")));

    private static SlotMarker? FindWholeSlot(GameGridInventory inventory, GameItem item)
    {
        if (!inventory.MayHaveValidInventorySlot(item) || !item.MayRemove() || item.MaxNumRemove() < item.unitCount) return null;
        var box = inventory.inventoryShape;
        var original = item.modifiedShape;
        if (original == null || box == null) return null;
        // No splitting or merging: keep the actual item instance and all its state.
        for (int turn = 0; turn < 4; turn++)
        {
            var shape = new GridShapeBuilder(original).SetOrientation((original.orientation + turn) % 4);
            for (int y = box.minY; y <= box.maxY; y++) for (int x = box.minX; x <= box.maxX; x++)
            {
                shape.SetPosition(x, y);
                var slot = inventory.TryInventorySlot(item, item.unitCount, shape);
                if (slot != null && slot.targetItem == null && slot.numTransfer >= item.unitCount) return slot;
            }
        }
        return null;
    }

    private static void MoveWhole(GameItem item, SlotMarker slot)
    {
        var original = item.parentInventory;
        var shape = item.modifiedShape.Clone();
        int count = item.unitCount;
        try
        {
            int transferred = slot.TryAcceptOnce(count);
            if (transferred != count || !ReferenceEquals(item.parentInventory, slot.inventory) || item.unitCount != count)
                throw new InvalidOperationException("原生移动未完成。");
        }
        catch
        {
            // The game's slot API expels before accepting. Recover an unparented item.
            if (!ReferenceEquals(item.parentInventory, original))
            {
                item.parentInventory?.Expel(item);
                item.modifiedShape = shape;
                if (original == null || !original.UncheckedAccept(item))
                    throw new InvalidOperationException("移动恢复失败，请立即保留日志并重新读档。");
            }
            throw;
        }
    }
}
