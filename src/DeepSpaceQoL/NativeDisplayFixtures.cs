using HarmonyLib;
using UnityEngine;

namespace DeepSpaceQoL;

/// <summary>Two native grids. Effects see their original items once through the native showcase.
/// Persistence uses only the existing showcase save bag, with no custom item factory or sidecar.</summary>
internal static class NativeDisplayFixtures
{
    private static Binding? current;
    private static bool restoring;

    internal static void Refresh()
    {
        if (!ModEntry.Enabled) return;
        var entry = EmporiumEntry.Instance;
        if (entry == null || entry.showcaseElement?.handler == null || !PlayerStore.HasInstance || PlayerStore.Instance.saveSlotId < 0) return;
        if (current != null && !ReferenceEquals(current.Root, entry.showcaseElement)) ResetForLoad();
        if (current == null)
        {
            current = new Binding(entry);
            RecoverSavedItems();
        }
        current.Refresh();
    }

    internal static void ResetForLoad()
    {
        var old = current;
        current = null;
        old?.DestroyWindows();
    }

    // Called before the loader removes patches. Never destroy a window containing a live item.
    internal static void Dispose()
    {
        if (current == null) return;
        var binding = current;
        if (binding.Root.handler == null)
        {
            current = null;
            binding.DestroyWindows();
            return;
        }
        restoring = true;
        try
        {
            foreach (var tray in binding.Trays)
                foreach (var item in tray.Grid.childItems.ToArray())
                {
                    if (!ReferenceEquals(item.parentInventory, tray.Grid)) continue;
                    // Structural unload must also preserve pinned signs. Find geometry
                    // without clearing their pin tag or relying on MaxNumRemove().
                    var destination = binding.Root;
                    var shape = FindRestorationShape(destination, item);
                    if (shape == null && binding.Entry.invElement != null)
                    {
                        var fallback = FindRestorationShape(binding.Entry.invElement, item);
                        if (fallback != null) { destination = binding.Entry.invElement; shape = fallback; }
                    }
                    if (!tray.Grid.Expel(item)) throw new InvalidOperationException("无法取回陈列物品，已保留陈列区。");
                    if (shape != null)
                    {
                        item.shape = new GridShapeBuilder(item.shape).SetTransform(shape).Build();
                        item.modifiedShape = new GridShapeBuilder(item.modifiedShape).SetTransform(shape).Build();
                        item.DisableTag(DisplayFixturePolicy.RegionTag);
                        item.DisableTag(DisplayFixturePolicy.LayoutTag);
                    }
                    else
                    {
                        // No room even in the main inventory: keep the item visible in
                        // the old case instead of retaining an off-grid tray coordinate.
                        item.shape = new GridShapeBuilder(item.shape).SetPosition(0, 0).Build();
                        item.modifiedShape = new GridShapeBuilder(item.modifiedShape).SetPosition(0, 0).Build();
                        ModEntry.Log?.Warning("原生展示柜和主库存已满；停用时保留陈列物品但可能重叠，请逐件移出。建议停用前先点取下。");
                    }
                    // UncheckedAccept is also the native load path. If the old case is full, keep
                    // the original item (and its recovery marker), never drop or destroy it.
                    if (!destination.UncheckedAccept(item))
                    {
                        tray.Grid.UncheckedAccept(item);
                        throw new InvalidOperationException("陈列物品回收失败；原物品已保留，请勿保存并报告日志。");
                    }
                }
        }
        finally { restoring = false; }
        current = null;
        binding.DestroyWindows();
    }

    private static GridShape? FindRestorationShape(GameGridInventory target, GameItem item)
    {
        for (int y = 0; y < target.inventoryShape.height; y++)
            for (int x = 0; x < target.inventoryShape.width; x++)
            {
                var shape = new GridShapeBuilder(item.modifiedShape).SetPosition(x, y).Build();
                if (target.CheckShapePlacement(item, shape, new List<GameItem>())) return shape;
            }
        return null;
    }

    private static void RecoverSavedItems()
    {
        if (current == null) return;
        restoring = true;
        try
        {
            // These items were flattened into the standard showcase by the save encoder.
            // Reattach at the saved local grid position, including pinned items: this is load
            // restoration, not a user transfer or an automatic sale.
            foreach (var item in current.Root.childItems.Where(i => ReferenceEquals(i.parentInventory, current.Root)).ToArray())
            {
                var region = DisplayFixturePolicy.For(item);
                if (!DisplayFixturePolicy.IsSaved(item, region)) continue;
                var tray = current.Trays.First(t => t.Region == region);
                bool valid = DisplayFixturePolicy.TryRestoreLayout(item, out var shape, out var modified);
                if (valid && region == DisplayFixturePolicy.Region.Lamps &&
                    !tray.Grid.CheckShapePlacement(item, modified, new List<GameItem>()))
                {
                    // 0.4.0 used a tall 4x6 tray. Preserve every old lamp while
                    // migrating it to the new horizontal desktop surface, pins included.
                    var replacement = FindRestorationShape(tray.Grid, item);
                    if (replacement != null)
                    {
                        shape = new GridShapeBuilder(shape).SetPosition(replacement.minX, replacement.minY).Build();
                        modified = new GridShapeBuilder(modified).SetPosition(replacement.minX, replacement.minY).Build();
                    }
                }
                if (!valid || !tray.Grid.CheckShapePlacement(item, modified, new List<GameItem>()))
                {
                    ModEntry.Log?.Warning($"陈列区位置冲突，{item.identifier} #{item.uniqueId} 保留在原生展示柜，请手动重新放置。");
                    continue;
                }
                if (!current.Root.Expel(item)) continue;
                var fallbackShape = item.shape;
                var fallbackModified = item.modifiedShape;
                item.shape = shape; item.modifiedShape = modified;
                if (!tray.Grid.UncheckedAccept(item))
                {
                    item.shape = fallbackShape; item.modifiedShape = fallbackModified;
                    current.Root.UncheckedAccept(item);
                    throw new InvalidOperationException("陈列区读档恢复失败，物品保留在原生展示柜。");
                }
            }
        }
        finally { restoring = false; }
        RefreshEffects();
    }

    private static void RefreshEffects()
    {
        if (current == null) return;
        foreach (var item in current.Root.childItems.Where(i => i.identifier == "black_lamp")) BlackLampHelper.RefreshLampSprite(item);
        ClienteleUIManager.Instance?.RefreshUI();
    }

    private static string HangDisplayed()
    {
        if (current == null) return "请先进入店铺。";
        int moved = 0, skipped = 0;
        foreach (var item in current.Root.childItems.Where(i => ReferenceEquals(i.parentInventory, current.Root)).ToArray())
        {
            var region = DisplayFixturePolicy.For(item);
            if (region == DisplayFixturePolicy.Region.None || !GeneralHelper.IsItemOwned(item)) continue;
            if (ItemPinHelper.IsPinned(item) || item.MaxNumRemove() < item.unitCount) { skipped++; continue; }
            var tray = current.Trays.First(t => t.Region == region);
            if (GraphUtils.TryAcceptAll(tray.Grid, item) == 0 && ReferenceEquals(item.parentInventory, tray.Grid)) moved++;
            else skipped++;
        }
        RefreshEffects();
        return $"已将展示柜中 {moved} 件证照／灯牌／暗灯移到独立陈列区。" + (skipped > 0 ? $"{skipped} 件因固定、限制或空间不足保留原处。" : "可直接拖入或取下；效果按原版计算。");
    }

    private static string OpenEditor()
    {
        if (current == null) return "请先进入店铺。";
        current.OpenEditor();
        return "拖入证照或灯牌，关闭窗口后自动悬挂。暗灯可直接拖到右侧桌面。";
    }

    private static string TakeDown()
    {
        if (current == null) return "请先进入店铺。";
        int moved = 0, left = 0;
        foreach (var tray in current.Trays)
            foreach (var item in tray.Grid.childItems.ToArray())
            {
                if (ItemPinHelper.IsPinned(item) || item.MaxNumRemove() < item.unitCount) { left++; continue; }
                // A clear, explicit return action. Do not route to machines or sellable table.
                if (GraphUtils.TryAcceptAll(current.Root, item) == 0 && ReferenceEquals(item.parentInventory, current.Root)) moved++;
                else left++;
            }
        RefreshEffects();
        return $"已取下 {moved} 件并放回原生展示柜。" + (left > 0 ? $"{left} 件因固定或展示柜空间不足保留原处；请先腾出空间再取下。" : "");
    }

    private sealed class Tray
    {
        internal readonly DisplayFixturePolicy.Region Region;
        internal readonly PixelWindow Window;
        internal readonly GameGridInventory Grid;
        internal Tray(Binding owner, DisplayFixturePolicy.Region region, int width, int height, string title)
        {
            Region = region;
            bool editor = region == DisplayFixturePolicy.Region.Signs;
            Window = editor ? new PixelWindow(true, title, WindowStyle.Machinery) : new PixelWindow();
            Grid = new GameGridInventory(width, height);
            Grid.SetTransparency(editor ? .2f : 1);
            if (!editor)
            {
                Grid.HideBorder(); Window.SetTransparency(1); Window.HideBorder();
                Window.xPadding = Window.yPadding = 0;
            }
            Window.Attach(Grid);
            // Reachable once through the case graph. Keep the visible window registered
            // for rendering, but do not offer the same inventory twice in global searches.
            GraphHandler.windowGraphNodeRoots.Remove(Window);
            Grid.slotPriorityFunc = owner.Root.slotPriorityFunc;
            Grid.mayInventoryAddItemFunc = (item, inv) => DisplayFixturePolicy.For(item) == region && GeneralHelper.IsItemOwned(item) &&
                !owner.Root.IsInsertLocked() && (owner.Root.mayInventoryAddItemFunc?.Invoke(item, inv) ?? true);
            Grid.mayInventoryRemoveItemFunc = (item, inv) => !owner.Root.IsRemoveLocked() &&
                (owner.Root.mayInventoryRemoveItemFunc?.Invoke(item, inv) ?? true);
            Grid.onSlotAddItemLateFunc = (item, inv, slot) =>
            {
                if (!DisplayFixturePolicy.IsSaved(item, region))
                    item.ModifyTag(DisplayFixturePolicy.RegionTag, s => s.Enable().SetString(DisplayFixturePolicy.Key(region)));
                if (!restoring) owner.Root.onSlotAddItemLateFunc?.Invoke(item, inv, slot);
            };
            Grid.onSlotRemoveItemEndFunc = (item, inv, slot) =>
            {
                if (!restoring)
                {
                    item.DisableTag(DisplayFixturePolicy.RegionTag);
                    item.DisableTag(DisplayFixturePolicy.LayoutTag);
                    owner.Root.onSlotRemoveItemEndFunc?.Invoke(item, inv, slot);
                }
            };
            Window.Validate();
            Window.Hide();
        }
    }

    private sealed class Binding
    {
        internal readonly EmporiumEntry Entry;
        internal readonly GameGridInventory Root;
        internal readonly Tray[] Trays;
        private readonly FixturePresentation presentation;
        private readonly MachineryButton manage;
        private readonly MachineryRadioButton visitorsToggle;
        private bool editorOpen;
        internal Binding(EmporiumEntry entry)
        {
            Entry = entry; Root = entry.showcaseElement;
            Trays = new[] {
                new Tray(this, DisplayFixturePolicy.Region.Signs, 24, 4, "证照与灯牌"),
                new Tray(this, DisplayFixturePolicy.Region.Lamps, 8, 3, "暗灯") };
            visitorsToggle = new MachineryRadioButton("每日逐项判定预期普通顾客（次日起）", ModEntry.ExpectedVisitorsEnabled, enabled =>
            {
                ModEntry.SetExpectedVisitors(enabled);
                ControlPanel.Status(enabled
                    ? NativeDailyVisitors.PatchApplied ? "已开启：下一次每日排程按各来源原概率分别判定，今天的来客不重抽。" : "已保存开关，但当前版本排程未匹配，仍保留原版规则。请查看日志。"
                    : "已关闭：下一次每日排程恢复原版普通顾客名额。");
            }).SetDescription(NativeDailyVisitors.Describe());
            // The stack is UI-only: RelinkStorage keeps the same Grid as the window's
            // single storage child, so native save graph indices remain unchanged.
            Trays[0].Window.Detach();
            var contents = MachineryStack.Vertical(Trays[0].Grid, visitorsToggle,
                new MachineryText("按原概率分别判定，不足基础名额则随机补足。\n只影响下一次每日排程，不重抽今天；剧情来客不变。", 6, TMPro.HorizontalAlignmentOptions.Left));
            contents.SetAlign(MachineryStack.Align.Start);
            Trays[0].Window.Attach(contents);
            presentation = new FixturePresentation(Trays[0].Grid, Trays[1].Grid, () => ModEntry.OpenManagement(NativeDisplayFixtures.OpenEditor));
            manage = new MachineryButton("悬挂", () => ModEntry.OpenManagement(NativeDisplayFixtures.OpenEditor), 34, 14)
                .SetDescription("管理悬挂证照与灯牌：拖入物品，关闭后自动渲染吊线与原生图案。暗灯放在右侧桌面。");
            manage.handler.SetParent(Entry.showcaseWindow.handler);
            Trays[0].Window.closeCallback = () => { editorOpen = false; RefreshEffects(); };
            Trays[0].Window.AddHeaderButton(new MachineryButton("移入", () => ModEntry.Execute(HangDisplayed), 30, 14)
                .SetDescription("把原生展示柜中未固定的证书、许可证、灯牌移入；暗灯移到右侧桌面。也可手动拖入。"));
            Trays[0].Window.AddHeaderButton(new MachineryButton("取下", () => ModEntry.Execute(TakeDown), 30, 14)
                .SetDescription("将陈列物品取回原生展示柜；放不下的原地保留。停用模组前请先取下并保存。"));
            Trays[0].Window.AddHeaderButton(new MachineryButton("完成", () => { editorOpen = false; Trays[0].Window.Hide(); RefreshEffects(); Refresh(); }, 30, 14)
                .SetDescription("关闭管理窗口并显示悬挂样式。物品和效果保留。"));
            Trays[0].Window.Hide();
        }

        internal void OpenEditor()
        {
            editorOpen = true;
            var window = Trays[0].Window;
            window.Show(); window.Center(); window.ToFront();
            Refresh();
        }

        internal void Refresh()
        {
            visitorsToggle.SetOn(ModEntry.ExpectedVisitorsEnabled);
            bool visible = Entry.showcaseWindow?.handler != null && Entry.showcaseWindow.IsVisible() &&
                Entry.showcaseWindow.handler.gameObject.activeInHierarchy && !(MapUIManager.Instance?.IsOutside ?? false);
            if (editorOpen && !Trays[0].Window.IsVisible()) editorOpen = false;
            if (!visible) editorOpen = false;
            foreach (var tray in Trays)
            {
                bool show = visible && (tray.Region == DisplayFixturePolicy.Region.Lamps || editorOpen);
                if (show && !tray.Window.IsVisible()) tray.Window.Show();
                else if (!show && tray.Window.IsVisible()) tray.Window.Hide();
                tray.Grid.overrideLockInsert = Root.IsInsertLocked();
                tray.Grid.overrideLockRemove = Root.IsRemoveLocked();
            }
            var canvas = RenderHandler.current?.windowUICanvasObject?.GetComponent<RectTransform>();
            if (canvas == null) return;
            float width = canvas.rect.width, height = canvas.rect.height;
            presentation.Refresh(visible, editorOpen, width, height);
            if (!visible) return;
            // The cabinet's native window has no title bar: attach a small button
            // just above its frame rather than enabling any new window chrome.
            manage.handler.rectTransform.anchoredPosition = new Vector2(Mathf.Max(0, (Entry.showcaseWindow?.widthPixels ?? Root.widthPixels) - manage.widthPixels), manage.heightPixels + 3);
            var lamps = Trays[1].Window;
            var location = DisplayFixtureLayout.Lamps(width, height, lamps.widthPixels, lamps.heightPixels);
            lamps.rectTransform.anchoredPosition = new Vector2(location.X, -location.Y);
        }

        internal void DestroyWindows()
        {
            presentation.Destroy();
            if (manage.handler != null) UnityEngine.Object.Destroy(manage.handler.gameObject);
            foreach (var tray in Trays)
            {
                // No Destroy(GameItem), no scaled or substituted sprites.
                if (tray.Window.handler != null) tray.Window.Destroy();
            }
        }
    }

    [HarmonyPatch(typeof(GameGridInventory), "get_childItems")]
    private static class EffectsPatch
    {
        private static void Postfix(GameGridInventory __instance, ref List<GameItem> __result)
        {
            var binding = current;
            if (binding == null || !ReferenceEquals(__instance, binding.Root)) return;
            foreach (var tray in binding.Trays) __result.AddRange(tray.Grid.childItems);
            __result = __result.Distinct().ToList();
        }
    }

    [HarmonyPatch(typeof(GameGridInventory), "get_children")]
    private static class GraphPatch
    {
        private static void Postfix(GameGridInventory __instance, ref List<GraphNodeStorage> __result)
        {
            var binding = current;
            if (binding == null || !ReferenceEquals(__instance, binding.Root)) return;
            // The graph has one edge per real item. childItems is the effect-facing view;
            // children keeps the two actual inventories for native saving and item discovery.
            foreach (var tray in binding.Trays) if (!__result.Contains(tray.Grid)) __result.Add(tray.Grid);
        }
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.EncodeNodes))]
    private static class SavePatch
    {
        private static void Prefix(GraphNodeStorage firstNode, out Dictionary<int, int>? __state)
        {
            __state = null;
            var binding = current;
            if (binding == null || firstNode is not GameItem bag || bag.identifier != "showcase_save_bag") return;
            // Mirror native EncodeNode's breadth-first inventory numbering. Other mods'
            // nodes are left unchanged; only our runtime grid indices become the native case.
            __state = DisplayFixturePolicy.InventoryMap(bag, binding.Root, binding.Trays.Select(t => t.Grid));
            if (__state.Count != binding.Trays.Length)
                throw new InvalidOperationException("陈列区保存图结构发生变化，已停止编码，避免保存不完整的库存关联。");
        }
        private static void Postfix(GraphNodeStorage firstNode, List<SaveItemNode> __result, Dictionary<int, int>? __state)
        {
            if (__state == null || __state.Count == 0) return;
            var roots = __result.Where(n => n.identifier == "showcase_save_bag").ToArray();
            if (roots.Length != 1) throw new InvalidOperationException("无法唯一确认原生展示柜保存根，已停止保存转换。");
            var binding = current ?? throw new InvalidOperationException("保存期间陈列区被卸载，已停止转换。");
            DisplayFixturePolicy.PrepareNativeSave(__result, roots[0], __state,
                binding.Root.inventoryShape.width, binding.Root.inventoryShape.height);
        }
    }
}
