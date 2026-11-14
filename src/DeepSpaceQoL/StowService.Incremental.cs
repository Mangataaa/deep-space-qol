using System.Collections;
using System.Diagnostics;
using DeepSpaceQoL.Core;
using UnityEngine;

namespace DeepSpaceQoL;

internal static partial class StowService
{
    private static int generation;
    internal static bool IsRunning { get; private set; }
    internal static string CurrentStatus { get; private set; } = "";

    /// <summary>UI entry point. Do not call synchronous Run from interactive controls.</summary>
    internal static string Start(bool reclaimOffer = false)
    {
        if (IsRunning) return CurrentStatus;
        if (!ModEntry.Ready(out string reason)) return reason;
        int ticket = ++generation;
        var entry = EmporiumEntry.Instance;
        int saveSlot = PlayerStore.Instance.saveSlotId;
        IsRunning = true;
        CurrentStatus = "正在收纳：扫描物品与储物规则……再次点击可查看进度。";
        try
        {
            if (ModHelper.StartCoroutine(ModEntry.Id, RunGuarded(ticket, entry, saveSlot, reclaimOffer)) == null)
                throw new InvalidOperationException("模组尚未进入运行状态，请稍后重试收纳。");
        }
        catch { IsRunning = false; throw; }
        return CurrentStatus;
    }

    /// <summary>Cancel at a frame boundary. Completed whole transfers stay completed;
    /// pending searches have not moved items and need no rollback.</summary>
    internal static void Cancel(string? reason = null)
    {
        generation++;
        IsRunning = false;
        CurrentStatus = reason ?? "收纳已取消；已完成的整件转移保留，未处理物品留在原处。";
        if (reason != null && ModEntry.Enabled) ControlPanel.Status(CurrentStatus);
    }

    private static IEnumerator RunGuarded(int ticket, EmporiumEntry entry, int saveSlot, bool reclaimOffer)
    {
        // Let Unity paint the initial status before scanning any inventories.
        yield return null;
        bool Active()
        {
            if (generation != ticket || !IsRunning) return false;
            if (!ModEntry.Enabled || !ReferenceEquals(EmporiumEntry.Instance, entry) ||
                !PlayerStore.HasInstance || PlayerStore.Instance.saveSlotId != saveSlot)
            { CurrentStatus = "收纳已停止：当前场景或存档已变化。"; return false; }
            if (Input.GetKeyDown(KeyCode.Escape))
            { CurrentStatus = "收纳已取消；已完成的整件转移保留，未处理物品留在原处。"; return false; }
            if (!ModEntry.Ready(out string reason))
            { CurrentStatus = "收纳已停止：" + reason + " 已完成的整件转移保留。"; return false; }
            return true;
        }
        void Report(string text)
        {
            if (generation != ticket) return;
            CurrentStatus = text;
            ControlPanel.Status(text);
        }
        var work = RunSteps(reclaimOffer, Active, Report).GetEnumerator();
        bool failed = false;
        try
        {
            while (true)
            {
                bool more;
                try { more = Active() && work.MoveNext(); }
                catch (Exception error)
                {
                    ModEntry.Log?.Error(error);
                    CurrentStatus = "收纳已停止：" + error.Message + " 已完成的整件转移保留；请查看日志。";
                    failed = true; break;
                }
                if (!more) break;
                yield return work.Current;
            }
        }
        finally
        {
            work.Dispose();
            if (generation == ticket)
            {
                IsRunning = false;
                if (ModEntry.Enabled && (failed || ReferenceEquals(EmporiumEntry.Instance, entry))) ControlPanel.Status(CurrentStatus);
            }
        }
    }

    private static IEnumerable<object?> RunSteps(bool reclaimOffer, Func<bool> active, Action<string> report)
    {
        if (!active()) yield break;
        var pages = StorageCatalog.GetPages();
        var targets = AutomaticTargets(pages);
        if (targets.Length == 0)
        { report("没有可自动收纳的储物页：请确认有真实储物柜/箱子，并解除目标页或整箱锁。"); yield break; }
        var scan = new SourceScan();
        var inventories = pages.Select(p => (GameInventory)p.Inventory).ToList();
        if (reclaimOffer && EmporiumEntry.Instance?.backInvinvElement != null) inventories.Add(EmporiumEntry.Instance.backInvinvElement);
        var seen = new HashSet<GameItem>(); var slice = Stopwatch.StartNew();
        foreach (var inventory in inventories.Distinct())
        {
            if (!active()) yield break;
            foreach (var item in inventory.childItems.ToArray())
            {
                if (item == null || !seen.Add(item) || !ReferenceEquals(item.parentInventory, inventory)) continue;
                AssessSource(scan, inventory, item);
                if (slice.Elapsed.TotalMilliseconds >= 3) { yield return null; slice.Restart(); if (!active()) yield break; }
            }
        }
        var items = scan.Candidates.OrderBy(i => i.identifier, StringComparer.Ordinal)
            .ThenByDescending(i => i.modifiedShape?.width * i.modifiedShape?.height ?? 0).ToArray();
        int moved = 0, left = 0, alreadyStored = 0, processed = 0;
        var compacted = new HashSet<GameGridInventory>(); var dirty = new HashSet<GameGridInventory>(); var sorted = new HashSet<GameGridInventory>();
        double nextReport = 0; var progress = Stopwatch.StartNew();
        foreach (var item in items)
        {
            if (slice.Elapsed.TotalMilliseconds >= 3) { yield return null; slice.Restart(); }
            if (!active()) yield break;
            if (progress.Elapsed.TotalMilliseconds >= nextReport)
            {
                report($"正在收纳 {processed}/{items.Length}：已转移 {moved} 件，已归位 {alreadyStored} 件，暂未放入 {left} 件。可按 Esc 取消。");
                nextReport = progress.Elapsed.TotalMilliseconds + 500;
            }
            processed++;
            if (!StorageRules.CanAutomate(item) || !IsMovableStock(item)) { scan.Restricted++; continue; }
            var source = item.parentInventory;
            var current = pages.FirstOrDefault(p => ReferenceEquals(p.Inventory, source));
            if (current?.Owner != null && CategoryMatcher.HasCategories(current.Rule) && StorageRules.Matches(current.Rule, item))
            { dirty.Add(current.Inventory); alreadyStored++; continue; }
            var matching = targets.Where(p => CategoryMatcher.HasCategories(p.Rule) && StorageRules.Matches(p.Rule, item));
            var fallback = current?.Owner != null && !CategoryMatcher.HasCategories(current.Rule)
                ? Enumerable.Empty<StoragePage>() : targets.Where(p => !CategoryMatcher.HasCategories(p.Rule));
            var destinations = matching.Concat(fallback)
                .Where(p => !ReferenceEquals(p.Inventory, source) && StorageRules.CanAutoAccess(p.Inventory) && StorageRules.Allows(p.Inventory, item))
                .OrderBy(p => CategoryMatcher.HasCategories(p.Rule) ? 0 : 1).ThenBy(p => p.Owner!.IsGameItemType("STORAGE") ? 0 : 1).ToArray();
            if (!destinations.Any(p => CategoryMatcher.HasCategories(p.Rule)) && current?.Owner != null && !CategoryMatcher.HasCategories(current.Rule))
            { dirty.Add(current.Inventory); alreadyStored++; continue; }
            bool placed = false;
            foreach (var destination in destinations)
            {
                if (!active()) yield break;
                if (!CanPlace(destination, item, source)) continue;
                SlotMarker? slot = null;
                foreach (var step in FindWholeSlotSteps(destination.Inventory, item, source, active, s => slot = s)) yield return step;
                if (!active()) yield break;
                if (slot == null && compacted.Add(destination.Inventory))
                {
                    report($"正在收纳 {processed}/{items.Length}：整理目标空间，已转移 {moved} 件。可按 Esc 取消。");
                    foreach (var step in ArrangeService.TryArrangeSteps(destination.Inventory, active, (changed, _) =>
                        { if (changed) sorted.Add(destination.Inventory); })) yield return step;
                    if (!active()) yield break;
                    foreach (var step in FindWholeSlotSteps(destination.Inventory, item, source, active, s => slot = s)) yield return step;
                }
                if (!active()) yield break;
                if (slot == null || !CanPlace(destination, item, source)) continue;
                // Slot lookup may have yielded; perform one fresh native validation at
                // the final coordinate immediately before an atomic whole-item move.
                var checkedSlot = destination.Inventory.TryInventorySlot(item, item.unitCount, slot.itemGridShape);
                if (checkedSlot == null || checkedSlot.targetItem != null || checkedSlot.numTransfer < item.unitCount) continue;
                MoveWhole(item, checkedSlot);
                dirty.Add(destination.Inventory); if (current?.Owner != null) dirty.Add(current.Inventory);
                moved++; placed = true;
                break;
            }
            if (!placed) left++;
            // At most a few milliseconds of transfer/lookup work per rendered frame.
            if (slice.Elapsed.TotalMilliseconds >= 3) { yield return null; slice.Restart(); }
        }
        int pageNumber = 0;
        foreach (var page in targets)
        {
            if (!active()) yield break;
            pageNumber++;
            // Revisit changed destinations and pages with already-stored stock, while
            // skipping empty/locked pages and installed equipment in root areas.
            if (!StorageRules.CanAutoAccess(page.Inventory) || !dirty.Contains(page.Inventory)) continue;
            report($"收纳转移完成：整理变动储物页 {pageNumber}/{targets.Length}，已转移 {moved} 件。可按 Esc 取消。");
            foreach (var step in ArrangeService.TryArrangeSteps(page.Inventory, active, (changed, _) =>
                { if (changed) sorted.Add(page.Inventory); })) yield return step;
            yield return null;
        }
        if (!active()) yield break;
        report($"已收纳 {moved} 件，整理 {sorted.Count} 页，已归位 {alreadyStored} 件；{left} 件因空间或原生限制保留原处。{scan.Summary}");
    }

    private static void AssessSource(SourceScan scan, GameInventory inventory, GameItem item)
    {
        if (StorageRules.IsQuestItem(item) || StorageRules.IsQuestInventory(inventory)) { scan.Quest++; return; }
        if (!GeneralHelper.IsItemOwned(item)) { scan.Unowned++; return; }
        if (!StorageRules.CanAutoAccess(inventory) || item.IsTag("ITEM_BEHIND_LOCK_TAG") || StorageRules.Policy(item).IsSaleProtected() ||
            ((IsPortablePackage(item) || IsCompositeGood(item)) && StorageRules.HasProtectedContents(item))) { scan.Locked++; return; }
        if (ItemPinHelper.IsPinned(item)) { scan.Pinned++; return; }
        if (!IsMovableStock(item)) { scan.Equipment++; return; }
        if (!StorageRules.CanAutomate(item)) { scan.Restricted++; return; }
        scan.Candidates.Add(item);
    }

    private static bool CanPlace(StoragePage destination, GameItem item, GameInventory source) =>
        ReferenceEquals(item.parentInventory, source) && StorageRules.CanAutomate(item) && IsMovableStock(item) &&
        StorageRules.CanAutoAccess(destination.Inventory) && StorageRules.Allows(destination.Inventory, item) &&
        (!CategoryMatcher.HasCategories(destination.Rule) || StorageRules.Matches(destination.Rule, item));

    private static IEnumerable<object?> FindWholeSlotSteps(GameGridInventory inventory, GameItem item, GameInventory source,
        Func<bool> active, Action<SlotMarker?> completed)
    {
        completed(null);
        if (!inventory.MayHaveValidInventorySlot(item) || !item.MayRemove() || item.MaxNumRemove() < item.unitCount) yield break;
        var box = inventory.inventoryShape; var original = item.modifiedShape.Clone(); int count = item.unitCount;
        if (box == null) yield break;
        var slice = Stopwatch.StartNew();
        int attempts = 0;
        for (int turn = 0; turn < 4; turn++)
        {
            var shape = new GridShapeBuilder(original).SetOrientation((original.orientation + turn) % 4);
            for (int y = box.minY; y <= box.maxY; y++) for (int x = box.minX; x <= box.maxX; x++)
            {
                if (((attempts++ & 15) == 0 && !active()) || !ReferenceEquals(item.parentInventory, source) || item.unitCount != count) yield break;
                shape.SetPosition(x, y);
                var slot = inventory.TryInventorySlot(item, count, shape);
                if (slot != null && slot.targetItem == null && slot.numTransfer >= count) { completed(slot); yield break; }
                if (slice.Elapsed.TotalMilliseconds >= 3) { yield return null; slice.Restart(); }
            }
        }
    }
}
