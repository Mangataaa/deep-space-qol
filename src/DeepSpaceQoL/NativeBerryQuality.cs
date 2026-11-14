using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DeepSpaceQoL;

/// <summary>Read-only fruit condition information; never changes the item or its native artwork.</summary>
internal static class NativeBerryQuality
{
    private static readonly Dictionary<GameItemElement, QualityBadge> Badges = new();
    private static bool active;
    private static RectTransform? inspectionHint;
    private static CustomUIElement? inspectionText;

    internal readonly struct QualityInfo
    {
        internal readonly int? Condition;
        internal readonly int VisualVariant;
        internal readonly string Name, Badge;
        internal readonly int? WineContribution;
        internal readonly RenderHandler.ColorPalette Color;

        internal QualityInfo(int? condition, int visualVariant, string name, string badge,
            int? wineContribution, RenderHandler.ColorPalette color)
        {
            Condition = condition; VisualVariant = visualVariant; Name = name; Badge = badge;
            WineContribution = wineContribution; Color = color;
        }

        internal bool IsKnown => WineContribution.HasValue;
        internal string Description => IsKnown
            ? $"荧光莓品质：{Name}（品质 {Signed(Condition!.Value)}；单果酿造贡献 {Signed(WineContribution!.Value)}）"
            : Condition.HasValue ? $"荧光莓品质：未知档位（{Condition.Value}）" : "荧光莓品质：品质数据缺失";
        private static string Signed(int value) => value > 0 ? "+" + value : value.ToString();
    }

    /// <summary>Use existing modified tags only. GameItem.GetTagReadonly creates missing tags, so avoid it here.</summary>
    internal static bool TryDescribe(GameItem? item, out QualityInfo quality)
    {
        quality = default;
        var tags = item?.modifiedState?.dict;
        if (tags == null || !tags.TryGetValue("BERRY_TAG", out var berry) || !berry.IsEnabled()) return false;
        int? condition = tags.TryGetValue("BERRY_CONDITION_INT", out var conditionTag) && conditionTag.IsEnabled()
            ? conditionTag.GetInt() : null;
        int variant = tags.TryGetValue("BERRY_VISUAL_VARIANT_INT", out var variantTag) && variantTag.IsEnabled()
            ? variantTag.GetInt() : 0;
        quality = condition switch
        {
            -2 => new QualityInfo(condition, variant, "霉变", "霉", -1, RenderHandler.ColorPalette.LightRed),
            -1 => new QualityInfo(condition, variant, variant == 2 ? "过熟" : "欠熟", variant == 2 ? "过" : "欠", 0, RenderHandler.ColorPalette.LightOrange),
            1 => new QualityInfo(condition, variant, "正常", "良", 1, RenderHandler.ColorPalette.LightGreen),
            2 => new QualityInfo(condition, variant, "优质小果", "优", 2, RenderHandler.ColorPalette.LightCyan),
            _ => new QualityInfo(condition, variant, "未知", "?", null, RenderHandler.ColorPalette.LightGray)
        };
        return true;
    }

    internal static void Init()
    {
        if (active) return;
        active = true;
        ModHook.OnCreateTooltipLate += AddTooltip;
    }

    internal static void Refresh()
    {
        if (!active || !ModEntry.Enabled) return;
        var visible = new HashSet<GameItemElement>();
        foreach (var item in VisibleItemViews.Items())
        {
            if (!TryDescribe(item, out var quality)) continue;
            visible.Add(item);
            if (!Badges.TryGetValue(item, out var badge) || !badge.Matches(item))
            {
                badge?.Destroy();
                badge = new QualityBadge(item);
                Badges[item] = badge;
            }
            badge.Refresh(item, quality);
        }
        foreach (var pair in Badges.ToArray())
            if (!visible.Contains(pair.Key) || pair.Key.handler == null)
            {
                pair.Value.Destroy();
                Badges.Remove(pair.Key);
            }
        RefreshInspectionHint();
    }

    private static void AddTooltip(RichTextBuilder builder, GameItem item)
    {
        if (!active || !ModEntry.Enabled || builder == null || !TryDescribe(item, out var quality)) return;
        builder.AddLine(quality.Description, true, quality.Color);
    }

    private static void RefreshInspectionHint()
    {
        var manager = InspectionUIManager.Instance;
        bool show = manager != null && manager.panel != null && manager.panel.activeInHierarchy &&
            manager.insBerryUILarge != null && manager.insBerryUILarge.gameObject.activeInHierarchy &&
            TryDescribe(manager.currentInspectingItem, out _);
        if (!show)
        {
            if (inspectionHint != null) inspectionHint.gameObject.SetActive(false);
            return;
        }
        if (CustomUIManager.Instance == null || !TryDescribe(manager!.currentInspectingItem, out var quality)) return;
        if (inspectionHint == null)
        {
            var layer = CustomUIManager.Instance.GetLayerRoot(CustomUIManager.LAYER_OVERLAY);
            inspectionHint = new GameObject("DeepSpaceQoL berry inspection quality", typeof(RectTransform)).GetComponent<RectTransform>();
            inspectionHint.SetParent(layer, false);
            inspectionHint.anchorMin = inspectionHint.anchorMax = new Vector2(0.5f, 1);
            inspectionHint.pivot = new Vector2(0.5f, 1);
            inspectionHint.anchoredPosition = new Vector2(0, -64);
            var background = inspectionHint.gameObject.AddComponent<Image>();
            background.sprite = MachineryStyle.BevelFrame;
            background.type = Image.Type.Sliced;
            background.raycastTarget = false;
            var group = inspectionHint.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false; group.blocksRaycasts = false;
            var builder = CustomUIManager.Instance.BuildInto(inspectionHint, false, ModEntry.Id + ".berry-quality").UseMachineryStyle();
            var layout = inspectionHint.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 8, 8);
            layout.childForceExpandWidth = true;
            builder.AddLabel(quality.Description);
            inspectionText = builder.Last;
            if (inspectionText.text != null)
            {
                inspectionText.text.richText = false;
                inspectionText.text.enableWordWrapping = true;
                inspectionText.text.raycastTarget = false;
                inspectionText.text.alignment = TextAlignmentOptions.Center;
            }
        }
        inspectionText!.SetText(quality.Description);
        var parent = inspectionHint.parent as RectTransform;
        float width = Mathf.Max(160, Mathf.Min(590, (parent?.rect.width ?? 640) - 28));
        float height = inspectionText.text != null
            ? inspectionText.text.GetPreferredValues(quality.Description, width - 24, float.PositiveInfinity).y + 16 : 48;
        inspectionHint.sizeDelta = new Vector2(width, Mathf.Clamp(height, 36, 100));
        inspectionHint.gameObject.SetActive(true);
        inspectionHint.SetAsLastSibling();
        CustomUIBuilder.RebuildLayout(inspectionHint);
    }

    internal static void Dispose()
    {
        active = false;
        ModHook.OnCreateTooltipLate -= AddTooltip;
        foreach (var badge in Badges.Values) badge.Destroy();
        Badges.Clear();
        if (inspectionHint != null)
        {
            inspectionHint.gameObject.SetActive(false);
            Object.Destroy(inspectionHint.gameObject);
        }
        inspectionHint = null; inspectionText = null;
    }

    private sealed class QualityBadge
    {
        private readonly TreeNodeRender background, label;
        private readonly TreeNodeRender host;
        internal bool Matches(GameItemElement item) => background != null && label != null &&
            host != null && ReferenceEquals(item.middle, host) && background.transform.parent == host.transform;

        internal QualityBadge(GameItemElement item)
        {
            host = item.middle;
            background = TreeNodeRender.Instantiate().SetParent(item.middle);
            background.gameObject.name = "DeepSpaceQoL berry quality badge";
            background.image.raycastTarget = false;
            background.image.sprite = null;
            background.image.color = new Color32(24, 32, 35, 238);
            background.rectTransform.anchorMin = background.rectTransform.anchorMax = Vector2.one;
            background.rectTransform.pivot = Vector2.one;
            background.rectTransform.anchoredPosition = new Vector2(-1, -1);
            label = TreeNodeRender.InstantiateText().SetParent(background);
            label.gameObject.name = "DeepSpaceQoL berry quality letter";
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            label.rectTransform.sizeDelta = Vector2.zero; label.rectTransform.anchoredPosition = Vector2.zero;
            label.text.raycastTarget = false;
            label.text.enabled = true;
            label.text.richText = true;
            label.text.alignment = TextAlignmentOptions.Center;
            label.text.enableAutoSizing = false;
            label.text.enableWordWrapping = false;
            label.text.fontStyle = FontStyles.Normal;
            label.text.margin = Vector4.zero;
            label.text.overflowMode = TextOverflowModes.Overflow;
        }

        internal void Refresh(GameItemElement item, QualityInfo quality)
        {
            if (background == null || label == null) return;
            float width = Mathf.Min(14, item.middle.rectTransform.rect.width - 2);
            float height = Mathf.Min(13, item.middle.rectTransform.rect.height - 2);
            bool show = width >= 8 && height >= 8;
            background.gameObject.SetActive(show);
            if (!show) return;
            background.rectTransform.sizeDelta = new Vector2(width, height);
            label.text.fontSize = Mathf.Min(10, height - 1);
            label.text.text = RichTextBuilder.Color(quality.Badge, quality.Color);
            background.rectTransform.SetAsLastSibling();
        }

        internal void Destroy()
        {
            if (background == null) return;
            background.gameObject.SetActive(false);
            Object.Destroy(background.gameObject);
        }
    }
}
