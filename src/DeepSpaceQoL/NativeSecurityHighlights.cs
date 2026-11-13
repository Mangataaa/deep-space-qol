using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DeepSpaceQoL;

/// <summary>Read-only inspection cues. All graphics belong to the mod, never to item state.</summary>
internal static class NativeSecurityHighlights
{
    private static readonly Dictionary<GameItemElement, RiskOverlay> Overlays = new();
    private static bool enabled;

    public static void Init()
    {
        if (enabled) return;
        enabled = true;
        ModHook.OnCreateTooltipLate += AppendTooltip;
    }

    public static void Refresh()
    {
        if (!enabled || !ModEntry.Enabled || !IsInspectionActive())
        {
            ClearOverlays();
            return;
        }
        var root = RenderHandler.current?.windowRoot;
        if (root == null) { ClearOverlays(); return; }

        // Active render nodes cover the shop, open bags and open cabinet pages without
        // opening any container, calling a search action or making invisible pages visible.
        var visible = new HashSet<GameItemElement>();
        foreach (var node in root.GetComponentsInChildren<TreeNodeRender>(false))
        {
            if (node.element is not GameItemElement item || !ReferenceEquals(item.handler, node) ||
                item.isDebugMenu || item.unitCount <= 0 || item.middle == null ||
                item.background == null || !item.background.image.enabled) continue;
            var risk = SecurityHighlightPolicy.Evaluate(item);
            if (risk.All == SecurityHighlightPolicy.RiskKind.None) continue;
            visible.Add(item);
            if (!Overlays.TryGetValue(item, out var overlay) || !overlay.IsAlive)
            {
                overlay?.Destroy();
                overlay = new RiskOverlay(item);
                Overlays[item] = overlay;
            }
            overlay.Refresh(risk);
        }
        foreach (var pair in Overlays.ToArray())
            if (!visible.Contains(pair.Key))
            {
                pair.Value.Destroy();
                Overlays.Remove(pair.Key);
            }
    }

    public static void Dispose()
    {
        if (enabled) ModHook.OnCreateTooltipLate -= AppendTooltip;
        enabled = false;
        ClearOverlays();
    }

    private static bool IsInspectionActive() =>
        EmporiumEntry.Instance?.invElement != null && PlayerStore.HasInstance &&
        !(MapUIManager.Instance?.IsOutside ?? false) &&
        !(GameMaster.current?.InCombat() ?? false) &&
        SecurityHighlightPolicy.IsInspectionClient(PlayerStore.Instance?.currentClientInstance?.GetClientBlueprint());

    private static void ClearOverlays()
    {
        foreach (var overlay in Overlays.Values) overlay.Destroy();
        Overlays.Clear();
    }

    private static void AppendTooltip(RichTextBuilder builder, GameItem item)
    {
        if (!enabled || !ModEntry.Enabled || !IsInspectionActive() || item == null) return;
        var risk = SecurityHighlightPolicy.Evaluate(item);
        if (risk.All == SecurityHighlightPolicy.RiskKind.None) return;
        builder.AddLine(SecurityHighlightPolicy.Describe(risk), ignoreEmpty: true,
            color: RenderHandler.ColorPalette.LightRed, bold: true);
        builder.AddLine("提示物品属性；原生隐藏、上锁及实际搜查判定保持不变。", ignoreEmpty: true,
            color: RenderHandler.ColorPalette.LightGray);
    }

    private sealed class RiskOverlay
    {
        private readonly RectTransform root;
        private readonly Image[] edges;
        private readonly RectTransform badge;
        private readonly Image badgeImage;
        private readonly TreeNodeRender marker;

        internal bool IsAlive => root != null && marker != null;

        internal RiskOverlay(GameItemElement item)
        {
            root = NewRect("DeepSpaceQoL inspection risk", item.middle.rectTransform);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            edges = Enumerable.Range(0, 4).Select(i =>
            {
                var edge = NewRect("Risk border " + i, root).gameObject.AddComponent<Image>();
                edge.raycastTarget = false;
                return edge;
            }).ToArray();
            badge = NewRect("Risk marker background", root);
            badgeImage = badge.gameObject.AddComponent<Image>();
            badgeImage.raycastTarget = false;
            marker = TreeNodeRender.InstantiateText();
            marker.gameObject.name = "DeepSpaceQoL inspection marker";
            marker.rectTransform.SetParent(badge, false);
            marker.rectTransform.anchorMin = Vector2.zero;
            marker.rectTransform.anchorMax = Vector2.one;
            marker.rectTransform.offsetMin = marker.rectTransform.offsetMax = Vector2.zero;
            marker.text.text = "!";
            marker.text.enabled = true;
            marker.text.color = Color.white;
            marker.text.fontStyle = FontStyles.Bold;
            marker.text.alignment = TextAlignmentOptions.Center;
            marker.text.enableAutoSizing = false;
            marker.text.enableWordWrapping = false;
            marker.text.margin = Vector4.zero;
            marker.text.overflowMode = TextOverflowModes.Overflow;
            marker.text.raycastTarget = false;
        }

        internal void Refresh(SecurityHighlightPolicy.RiskInfo risk)
        {
            bool contraband = (risk.All & SecurityHighlightPolicy.RiskKind.Contraband) != 0;
            Color color = contraband ? new Color32(240, 89, 75, 255) : new Color32(255, 184, 66, 255);
            int scale = Math.Max(1, (UISettings.current?.gridSize ?? 16) / 16);
            float thickness = 2 * scale;
            for (int i = 0; i < edges.Length; i++)
            {
                var rect = edges[i].rectTransform;
                bool horizontal = i < 2;
                rect.anchorMin = horizontal ? new Vector2(0, i == 0 ? 1 : 0) : new Vector2(i == 2 ? 0 : 1, 0);
                rect.anchorMax = horizontal ? new Vector2(1, i == 0 ? 1 : 0) : new Vector2(i == 2 ? 0 : 1, 1);
                rect.pivot = horizontal ? new Vector2(0.5f, i == 0 ? 1 : 0) : new Vector2(i == 2 ? 0 : 1, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = horizontal ? new Vector2(0, thickness) : new Vector2(thickness, 0);
                edges[i].color = color;
            }
            badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(0, 1);
            badge.anchoredPosition = new Vector2(thickness, -thickness);
            badge.sizeDelta = new Vector2(7 * scale, 10 * scale);
            badgeImage.color = contraband ? new Color32(140, 37, 38, 255) : new Color32(126, 76, 15, 255);
            marker.text.fontSize = 10 * scale;
            root.SetAsLastSibling();
        }

        internal void Destroy()
        {
            if (root == null) return;
            root.gameObject.SetActive(false);
            Object.Destroy(root.gameObject);
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }
    }
}

/// <summary>Pure, data-only access to the installed game's inspection flags.</summary>
internal static class SecurityHighlightPolicy
{
    [Flags]
    internal enum RiskKind { None = 0, Contraband = 1, Stolen = 2 }

    internal readonly struct RiskInfo
    {
        internal readonly RiskKind Direct, Contained;
        internal RiskKind All => Direct | Contained;
        internal RiskInfo(RiskKind direct, RiskKind contained) { Direct = direct; Contained = contained; }
    }

    internal static bool IsInspectionClient(StoreClient? client) =>
        client?.clientIntent == StoreClient.ClientIntent.INSPECTION;

    internal static RiskKind DirectRisk(GameItem? item)
    {
        // Match native IsFlaggableItem without GetTagReadonly: that native helper
        // creates an absent tag in modifiedState, despite its read-only name.
        // Drawing a cue must not add tags or consume stolen-detection RNG.
        if (item == null) return RiskKind.None;
        bool contraband = ContrabandHelper.IsContraband(item);
        bool stolen = StolenHelper.IsStolenItem(item);
        if (!contraband && !stolen) return RiskKind.None;
        if (!stolen && item.modifiedState.dict.TryGetValue("CONTRABAND_LEVEL", out var level) &&
            string.Equals(level.GetString(), "CONTRABAND_LEVEL_NONE", StringComparison.Ordinal)) return RiskKind.None;
        return (contraband ? RiskKind.Contraband : RiskKind.None) | (stolen ? RiskKind.Stolen : RiskKind.None);
    }

    internal static RiskInfo Evaluate(GameItem? item)
    {
        if (item == null) return new RiskInfo(RiskKind.None, RiskKind.None);
        var direct = DirectRisk(item);
        var contained = RiskKind.None;
        var seen = new HashSet<GraphNodeStorage> { item };
        var queue = new Queue<GraphNodeStorage>();
        queue.Enqueue(item);
        while (queue.Count > 0)
        {
            var children = queue.Dequeue().children;
            if (children == null) continue;
            foreach (var child in children)
            {
                if (child == null || !seen.Add(child)) continue;
                if (child is GameItem nested) contained |= DirectRisk(nested);
                queue.Enqueue(child);
            }
        }
        return new RiskInfo(direct, contained);
    }

    internal static string Describe(RiskInfo risk)
    {
        static string Names(RiskKind kind) => kind switch
        {
            RiskKind.Contraband => "违禁品",
            RiskKind.Stolen => "赃物",
            _ => "违禁品及赃物"
        };
        var parts = new List<string>();
        if (risk.Direct != RiskKind.None) parts.Add(Names(risk.Direct));
        if (risk.Contained != RiskKind.None) parts.Add("内部含" + Names(risk.Contained));
        return parts.Count == 0 ? "" : "搜查提示：" + string.Join("；", parts) + "。";
    }
}
