using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DeepSpaceQoL;

/// <summary>Transient, click-through feedback for actions invoked without the advanced panel.</summary>
internal static class NativeFeedback
{
    private static RectTransform? root;
    private static CustomUIElement? label;
    private static float hideAt;

    public static void Show(string message)
    {
        if (!ModEntry.Enabled || CustomUIManager.Instance == null) return;
        try
        {
            var layer = CustomUIManager.Instance.GetLayerRoot(CustomUIManager.LAYER_OVERLAY);
            if (root == null)
            {
                root = new GameObject("DeepSpaceQoL operation feedback", typeof(RectTransform)).GetComponent<RectTransform>();
                root.SetParent(layer, false);
                root.anchorMin = root.anchorMax = new Vector2(0.5f, 1);
                root.pivot = new Vector2(0.5f, 1);
                root.anchoredPosition = new Vector2(0, -14);
                var background = root.gameObject.AddComponent<Image>();
                background.sprite = MachineryStyle.BevelFrame; background.type = Image.Type.Sliced;
                var group = root.gameObject.AddComponent<CanvasGroup>();
                group.interactable = false; group.blocksRaycasts = false;
                var builder = CustomUIManager.Instance.BuildInto(root, false, ModEntry.Id + ".feedback").UseMachineryStyle();
                root.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(14, 14, 10, 10);
                root.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = true;
                builder.AddLabel(message);
                label = builder.Last;
                if (label.text != null) { label.text.richText = false; label.text.enableWordWrapping = true; }
            }
            label!.SetText(message);
            float width = Mathf.Max(200, Mathf.Min(760, layer.rect.width - 32));
            float textHeight = label.text != null ? label.text.GetPreferredValues(message, width - 28, float.PositiveInfinity).y : 60;
            root.sizeDelta = new Vector2(width, Mathf.Clamp(Mathf.Ceil(textHeight) + 20, 48, 200));
            root.gameObject.SetActive(true);
            root.SetAsLastSibling();
            CustomUIBuilder.RebuildLayout(root);
            hideAt = Time.unscaledTime + 8;
        }
        catch (Exception error) { ModEntry.Log?.Warning("屏幕提示未能显示：" + error.Message); }
    }

    public static void Refresh()
    { if (root != null && Time.unscaledTime >= hideAt) root.gameObject.SetActive(false); }

    public static void Dispose()
    {
        if (root != null) { root.gameObject.SetActive(false); Object.Destroy(root.gameObject); }
        root = null; label = null; hideAt = 0;
    }
}
