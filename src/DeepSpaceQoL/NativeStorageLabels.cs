using DeepSpaceQoL.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DeepSpaceQoL;

/// <summary>Category checkboxes embedded in the original blue label printer, independent of names.</summary>
internal static class NativeStorageLabels
{
    private static PrinterBinding? binding;

    public static void Dispose() { binding?.Dispose(); binding = null; }

    public static void Refresh()
    {
        if (binding != null && !binding.IsAlive) Dispose();
    }

    public static void OpenFor(GameItem owner, int index)
    {
        var printer = LabelerBlueUIManager.Instance;
        if (!ModEntry.Enabled || printer == null || !StorageCatalog.PagesOf(owner).Any(p => p.Index == index)) return;
        if (StorageCatalog.TabsOf(owner) == null) printer.OpenUI(owner);
        else
        {
            printer.OpenUI(StorageExpansionHelper.GetTabName(owner, index) ?? "",
                text => StorageExpansionHelper.SetTabName(owner, index, text),
                () => StorageExpansionHelper.SetTabName(owner, index, null), StorageExpansionHelper.TAB_NAME_MAX_LENGTH);
            Attach(printer, owner, index);
        }
    }

    private static void Attach(LabelerBlueUIManager printer, GameItem owner, int index)
    {
        Dispose();
        if (!ModEntry.Enabled || printer == null || CustomUIManager.Instance == null ||
            !StorageCatalog.PagesOf(owner).Any(p => p.Index == index)) return;
        try { binding = new PrinterBinding(printer, owner, index); }
        catch (Exception ex)
        {
            Dispose();
            ModEntry.Log?.Error(ex);
            ControlPanel.Status("标签选择界面未能创建，请保留日志；原生改名仍可使用。");
        }
    }

    private sealed class PrinterBinding
    {
        private readonly LabelerBlueUIManager printer;
        private readonly GameItem owner;
        private readonly int index;
        private readonly GameGridInventory inventory;
        private readonly RectTransform frame;
        private readonly Vector2 originalSize, originalPosition;
        private readonly (RectTransform rect, Vector2 size, Vector2 position, Vector3 world)[] originalChildren;
        private readonly RectTransform root;
        private readonly CustomUIElement summary;
        private readonly GameObject scroll;
        private readonly float topDistance, scrollHeight;
        private readonly string[] choices;
        private readonly Dictionary<string, CustomUIElement> toggles = new(StringComparer.OrdinalIgnoreCase);
        private bool expanded = true;
        public bool IsAlive => printer != null && printer.panel != null && printer.panel.activeInHierarchy &&
            frame != null && root != null && ModEntry.Enabled &&
            StorageCatalog.PagesOf(owner).Any(p => p.Index == index && ReferenceEquals(p.Inventory, inventory));

        internal PrinterBinding(LabelerBlueUIManager printer, GameItem owner, int index)
        {
            this.printer = printer; this.owner = owner; this.index = index;
            inventory = StorageCatalog.PagesOf(owner).Single(p => p.Index == index).Inventory;
            frame = FindFrame(printer);
            originalSize = frame.sizeDelta; originalPosition = frame.anchoredPosition;
            originalChildren = Enumerable.Range(0, frame.childCount).Select(i => frame.GetChild(i) as RectTransform)
                .Where(r => r != null).Select(r => (r!, r!.sizeDelta, r.anchoredPosition, r.position)).ToArray();
            var corners = new Vector3[4];
            printer.inputField.GetComponent<RectTransform>().GetWorldCorners(corners);
            float inputBottom = corners.Min(p => frame.InverseTransformPoint(p).y);
            topDistance = frame.rect.yMax - inputBottom + 10f;
            var canvas = frame.GetComponentInParent<Canvas>()?.transform as RectTransform;
            float available = canvas != null ? Mathf.Abs(frame.InverseTransformVector(canvas.TransformVector(new Vector3(0, canvas.rect.height, 0))).y) : 720;
            scrollHeight = Mathf.Clamp(available - topDistance - 210f, 80f, 190f);
            root = new GameObject("DeepSpaceQoL printer category checklist", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(frame, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
            root.pivot = new Vector2(0.5f, 1f);
            root.anchoredPosition = new Vector2(0, -topDistance);
            root.sizeDelta = new Vector2(Mathf.Max(140, frame.rect.width - 28), 1);
            try
            {
                var builder = CustomUIManager.Instance.BuildInto(root, false, ModEntry.Id + ".printer").UseMachineryStyle();
                root.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;
                builder.AddButton("物品分类（可多选）", () => { expanded = !expanded; UpdateLayout(); }, "summary");
                summary = builder.Last;
                choices = TypeHelper.MAJOR_TYPES_LIST.Concat(TypeHelper.ALL_TYPES_LIST).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var rule = StorageRules.Policy(owner).Page(index);
                var tokens = new HashSet<string>(CategoryMatcher.ExplicitTokens(rule.Categories), StringComparer.OrdinalIgnoreCase);
                builder.BeginScroll(scrollHeight);
                var scrollTransform = builder.CurrentContainer;
                var scrollRect = scrollTransform.GetComponentInParent<ScrollRect>();
                scroll = scrollRect != null ? scrollRect.gameObject : scrollTransform.gameObject;
                builder.AddToggle("全部类别", tokens.Contains("*"), value => ChangeToken("*", value));
                toggles["*"] = builder.Last;
                foreach (string category in choices)
                {
                    string token = "tag:" + category;
                    string display = TypeHelper.GetTypeDisplayName(category);
                    if (string.IsNullOrWhiteSpace(display)) display = category;
                    builder.AddToggle(display, tokens.Contains(token), value => ChangeToken(token, value));
                    builder.Last.SetTooltip(category);
                    toggles[token] = builder.Last;
                }
                var extra = tokens.Where(t => t != "*" && !choices.Any(c => string.Equals(t, "tag:" + c, StringComparison.OrdinalIgnoreCase))).ToArray();
                foreach (string token in extra)
                {
                    builder.AddToggle("保留原规则：" + token, true, value => ChangeToken(token, value));
                    toggles[token] = builder.Last;
                }
                builder.End();
                builder.AddToggle("仅允许手动放入勾选分类", rule.Restrict, value => Change(p => p.Page(index).Restrict = value));
                builder.Last.SetTooltip("任一勾选分类匹配即可；剧情强制放置、博士购物溢出与读档不受此限制。");
                builder.BeginRow();
                builder.AddToggle("锁定此页", rule.SaleLocked, value => Change(p => p.Page(index).SaleLocked = value));
                builder.Last.SetTooltip("锁定后，一键收纳不放入也不取出，自动整理、上货和装瓶跳过此页；仍可手动操作。");
                builder.Last.SetFlexible(1, 0);
                builder.AddToggle("锁定整箱", StorageRules.Policy(owner).ContainerLocked, value => Change(p => p.ContainerLocked = value));
                builder.Last.SetTooltip("锁定所有页及内部箱子；自动收纳不进不出，仍可手动操作。");
                builder.Last.SetFlexible(1, 0);
                builder.End().AddLabel("勾选立即生效 · 名称与分类分别保存", "saved");
                UpdateSummary();
                UpdateLayout();
                ModEntry.Log?.Log($"原生标签打印机：{owner.identifier} #{owner.uniqueId} / 页 {index + 1}，{choices.Length} 个原生分类。");
            }
            catch
            {
                root.gameObject.SetActive(false); Object.Destroy(root.gameObject); RestoreGeometry(); throw;
            }
        }

        private static RectTransform FindFrame(LabelerBlueUIManager printer)
        {
            var input = printer.inputField.GetComponent<RectTransform>();
            for (var p = input.parent as RectTransform; p != null; p = p.parent as RectTransform)
            {
                if (p.rect.width >= input.rect.width + 20 && p.rect.width <= Mathf.Max(900, input.rect.width * 4) &&
                    p.rect.height >= input.rect.height + 45 && p.GetComponent<Image>() != null) return p;
                if (p.gameObject == printer.panel) break;
            }
            var panel = printer.panel.transform as RectTransform;
            if (panel != null && panel.rect.width > input.rect.width && panel.rect.width <= Mathf.Max(900, input.rect.width * 4)) return panel;
            throw new InvalidOperationException("无法确认原生打印机的窗口边框。");
        }

        private void ChangeToken(string token, bool selected)
        {
            Change(p =>
            {
                var tokens = new HashSet<string>(CategoryMatcher.ExplicitTokens(p.Page(index).Categories), StringComparer.OrdinalIgnoreCase);
                if (selected) tokens.Add(token); else tokens.Remove(token);
                p.Page(index).Categories = string.Join(",", tokens.OrderBy(t => t, StringComparer.OrdinalIgnoreCase));
            });
        }

        private void Change(Action<StoragePolicy> edit)
        {
            if (!IsAlive) { ControlPanel.Status("当前储物页已变化，请重新打开标签打印机。"); return; }
            try
            {
                var policy = StorageRules.Policy(owner);
                if (!policy.IsValid()) throw new InvalidOperationException("旧规则无法读取，已保留原始数据和锁定状态。");
                edit(policy);
                StorageRules.Save(owner, policy);
                UpdateSummary();
                NativeInventoryButtons.Refresh();
            }
            catch (Exception ex) { ModEntry.Log?.Error(ex); ControlPanel.Status("分类设置未完成：" + ex.Message); }
            var current = new HashSet<string>(CategoryMatcher.ExplicitTokens(StorageRules.Policy(owner).Page(index).Categories), StringComparer.OrdinalIgnoreCase);
            foreach (var pair in toggles) pair.Value.SetBool(current.Contains(pair.Key));
        }

        private void UpdateSummary()
        {
            var tokens = CategoryMatcher.ExplicitTokens(StorageRules.Policy(owner).Page(index).Categories);
            string names = string.Join(" / ", choices.Where(c => tokens.Contains("tag:" + c, StringComparer.OrdinalIgnoreCase)).Select(TypeHelper.GetTypeDisplayName));
            if (tokens.Contains("*")) names = "全部类别";
            else if (string.IsNullOrEmpty(names)) names = tokens.Length == 0 ? "通用收纳" : "保留的高级规则";
            if (names.Length > 27) names = names.Substring(0, 26) + "…";
            summary.SetText("分类（可多选）：" + names + (expanded ? " [收起]" : " [展开]"));
        }

        private void UpdateLayout()
        {
            RestoreGeometry();
            scroll.SetActive(expanded);
            float contentHeight = 150f + (expanded ? scrollHeight + 8 : 0);
            float desiredHeight = Mathf.Max(frame.rect.height, topDistance + contentHeight + 12);
            float growth = desiredHeight - frame.rect.height;
            frame.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, desiredHeight);
            frame.anchoredPosition -= new Vector2(0, growth * (1 - frame.pivot.y));
            foreach (var child in originalChildren)
            {
                if (child.rect == null) continue;
                child.rect.sizeDelta = child.size - new Vector2(0, (child.rect.anchorMax.y - child.rect.anchorMin.y) * growth);
                child.rect.position = child.world;
            }
            root.sizeDelta = new Vector2(root.sizeDelta.x, contentHeight);
            root.anchoredPosition = new Vector2(0, -topDistance);
            var canvas = frame.GetComponentInParent<Canvas>()?.transform as RectTransform;
            if (canvas != null)
            {
                var corners = new Vector3[4]; frame.GetWorldCorners(corners);
                float low = corners.Min(p => canvas.InverseTransformPoint(p).y), high = corners.Max(p => canvas.InverseTransformPoint(p).y);
                float shift = low < canvas.rect.yMin + 12 ? canvas.rect.yMin + 12 - low : 0;
                if (high + shift > canvas.rect.yMax - 12) shift = canvas.rect.yMax - 12 - high;
                frame.position += canvas.TransformVector(new Vector3(0, shift, 0));
            }
            UpdateSummary();
            CustomUIBuilder.RebuildLayout(root);
        }

        private void RestoreGeometry()
        {
            if (frame == null) return;
            frame.sizeDelta = originalSize; frame.anchoredPosition = originalPosition;
            foreach (var child in originalChildren)
                if (child.rect != null) { child.rect.sizeDelta = child.size; child.rect.anchoredPosition = child.position; }
        }

        internal void Dispose()
        {
            if (root != null) { root.gameObject.SetActive(false); Object.Destroy(root.gameObject); }
            RestoreGeometry();
        }
    }

    [HarmonyPatch(typeof(LabelerBlueUIManager), nameof(LabelerBlueUIManager.OpenUI), new[] { typeof(string), typeof(Action<string>), typeof(Action), typeof(int) })]
    private static class StringOpenPatch { private static void Prefix() => Dispose(); }

    [HarmonyPatch(typeof(LabelerBlueUIManager), nameof(LabelerBlueUIManager.OpenUI), new[] { typeof(GameItem) })]
    private static class ItemOpenPatch
    {
        private static void Prefix() => Dispose();
        private static void Postfix(LabelerBlueUIManager __instance, GameItem gameItem)
        {
            if (!ModEntry.Enabled || gameItem == null) return;
            int index = StorageCatalog.TabsOf(gameItem)?.selected ?? 0;
            Attach(__instance, gameItem, index);
        }
    }

    [HarmonyPatch(typeof(StorageExpansionHelper), "OpenRename")]
    private static class RenamePatch
    {
        private static void Postfix(GameItem bay, int index)
        { if (ModEntry.Enabled && LabelerBlueUIManager.Instance != null) Attach(LabelerBlueUIManager.Instance, bay, index); }
    }

    [HarmonyPatch(typeof(LabelerBlueUIManager), nameof(LabelerBlueUIManager.CloseUI))]
    private static class ClosePatch { private static void Postfix() => Dispose(); }
}
