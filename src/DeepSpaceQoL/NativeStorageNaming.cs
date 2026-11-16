using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DeepSpaceQoL;

/// <summary>Stores a display-only name on the existing policy and prints it on each visible box.</summary>
internal static class NativeStorageNaming
{
    private const string WindowId = ModEntry.Id + ".storage-name";
    private const int MaxLength = 32;
    private static CustomUIWindow? window;
    private static GameItem? editing;
    private static readonly Dictionary<GameItemElement, NameBadge> Badges = new();

    internal static void Open(GameItem owner)
    {
        if (!ModEntry.Enabled || owner == null || !StorageCatalog.IsStorage(owner) || CustomUIManager.Instance == null) return;
        Close();
        editing = owner;
        var b = CustomUIManager.Instance.CreateWindow(WindowId, "命名储物箱", CustomUIManager.LAYER_OVERLAY,
            style: CustomUIManager.Style.Machinery);
        window = b.Handle;
        b.SetSize(520, 220).Center().SetDraggable(true).SetCloseOnEscape();
        string value = StorageRules.Policy(owner).ContainerName ?? "";
        b.AddLabel("名称会直接显示在箱体正面，不会修改物品或原生页签名称。最多 32 个字符。", "hint");
        b.AddInput("储物箱名称", value, text => value = Sanitize(text), "name", text => value = Sanitize(text));
        b.AddButton("保存", () =>
        {
            if (editing == null || !StorageCatalog.IsStorage(editing)) { Close(); NativeFeedback.Show("储物箱已不存在，请重新打开。"); return; }
            var policy = StorageRules.Policy(editing);
            if (!policy.IsValid()) { Close(); NativeFeedback.Show("储物规则版本不兼容，名称未保存。"); return; }
            policy.ContainerName = Sanitize(value);
            StorageRules.Save(editing, policy);
            Close();
            Refresh();
            NativeFeedback.Show(string.IsNullOrEmpty(policy.ContainerName) ? "已清除储物箱名称。" : "已保存储物箱名称：" + policy.ContainerName);
        }).AddButton("取消", Close).Show();
    }

    internal static void Close()
    {
        window?.Close();
        window = null;
        editing = null;
    }

    internal static void Refresh()
    {
        if (!ModEntry.Enabled) return;
        var visible = new HashSet<GameItemElement>();
        foreach (var item in VisibleItemViews.Items())
        {
            GameItem owner = item;
            if (!StorageCatalog.IsStorage(owner)) continue;
            string name = StorageRules.Policy(owner).ContainerName ?? "";
            if (string.IsNullOrWhiteSpace(name)) continue;
            visible.Add(item);
            if (!Badges.TryGetValue(item, out var badge) || !badge.Matches(item))
            {
                badge?.Destroy();
                badge = new NameBadge(item);
                Badges[item] = badge;
            }
            badge.Refresh(name);
        }
        foreach (var pair in Badges.ToArray())
            if (!visible.Contains(pair.Key) || pair.Key.handler == null)
            {
                pair.Value.Destroy();
                Badges.Remove(pair.Key);
            }
    }

    internal static void Dispose()
    {
        foreach (var badge in Badges.Values) badge.Destroy();
        Badges.Clear();
        Close();
    }

    private static string Sanitize(string? value)
    {
        value = (value ?? "").Replace('\n', ' ').Replace('\r', ' ').Trim();
        return value.Length <= MaxLength ? value : value.Substring(0, MaxLength);
    }

    // Use the item's current footprint, not a fixed floating strip outside its mask. Kept data-only
    // so size/zoom/rotation edge cases can be verified without instantiating a Unity scene.
    internal static (float Width, float Height, float FontMax, bool Visible) MeasureName(float width, float height)
    {
        if (float.IsNaN(width) || float.IsInfinity(width) || float.IsNaN(height) || float.IsInfinity(height) ||
            width < 12 || height < 12) return (0, 0, 0, false);
        float usableWidth = Math.Max(0, width - 6), usableHeight = Math.Min(height - 4, height * .55f);
        float fontMax = Math.Min(14, Math.Min(usableHeight * .72f, usableWidth * .32f));
        return (usableWidth, usableHeight, fontMax, fontMax >= 5);
    }

    private sealed class NameBadge
    {
        private readonly TreeNodeRender label;
        private readonly TreeNodeRender host;

        internal NameBadge(GameItemElement item)
        {
            host = item.middle;
            // The native text prefab can have its TMP component disabled. Instantiating it is not
            // enough: native stack labels and the berry badge explicitly enable theirs too.
            label = TreeNodeRender.InstantiateText().SetParent(host);
            label.gameObject.name = "DeepSpaceQoL storage name";
            label.text.raycastTarget = false;
            label.text.enabled = true;
            label.text.richText = false;
            label.text.alignment = TextAlignmentOptions.Center;
            label.text.enableAutoSizing = true;
            label.text.enableWordWrapping = false;
            label.text.overflowMode = TextOverflowModes.Ellipsis;
            label.text.fontStyle = FontStyles.Normal;
            label.text.fontSizeMin = 5;
            label.text.margin = Vector4.zero;
            label.text.color = new Color32(245, 245, 240, 255);
            label.text.outlineColor = new Color32(24, 22, 30, 255);
            label.text.outlineWidth = .12f;
        }

        internal bool Matches(GameItemElement item) => host != null && ReferenceEquals(host, item.middle) &&
            label != null && label.transform.parent == host.transform;

        internal void Refresh(string text)
        {
            if (host == null || label == null) return;
            var layout = MeasureName(host.rectTransform.rect.width, host.rectTransform.rect.height);
            label.gameObject.SetActive(layout.Visible);
            if (!layout.Visible) return;
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(.5f, .45f);
            label.rectTransform.pivot = new Vector2(.5f, .5f);
            label.rectTransform.anchoredPosition = Vector2.zero;
            label.rectTransform.sizeDelta = new Vector2(layout.Width, layout.Height);
            label.text.fontSizeMax = layout.FontMax;
            label.text.fontSize = layout.FontMax;
            label.text.text = text;
            label.text.enabled = true;
            label.rectTransform.SetAsLastSibling();
        }

        internal void Destroy()
        {
            if (label != null) Object.Destroy(label.gameObject);
        }
    }
}
