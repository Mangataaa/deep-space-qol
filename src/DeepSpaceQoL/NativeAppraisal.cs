using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DeepSpaceQoL;

/// <summary>A fill-only button beside the shared native appraisal price panel.</summary>
internal static class NativeAppraisal
{
    private static Binding? binding;

    internal static void Refresh()
    {
        if (!ModEntry.Enabled) { Dispose(); return; }
        var manager = InspectionUIManager.Instance;
        if (binding != null && !binding.Matches(manager)) Dispose();
        if (!HasActiveContext(manager))
        {
            binding?.SetVisible(false);
            return;
        }
        if (binding == null && CustomUIManager.Instance != null)
            binding = new Binding(manager!);
        binding?.Refresh();
    }

    internal static void Dispose()
    {
        binding?.Dispose();
        binding = null;
    }

    internal static bool HasActiveContext(InspectionUIManager? manager)
    {
        if (!ModEntry.Enabled || manager == null || manager.panel == null ||
            !manager.panel.activeInHierarchy || manager.appraisalPanel == null ||
            !manager.appraisalPanel.activeInHierarchy || manager.apprisalServiceInputField == null ||
            !manager.apprisalServiceInputField.gameObject.activeInHierarchy ||
            !manager.apprisalServiceInputField.IsInteractable() ||
            manager.currentInspectingItem == null || !manager.currentInspectingItem.IsTag("APPRAISAL_ITEM")) return false;
        var client = PlayerStore.Instance?.currentClientInstance?.GetClientBlueprint();
        return client != null && client.clientIntent == StoreClient.ClientIntent.APPRAISAL_SERVICE;
    }

    internal static string FillPrice()
    {
        var manager = InspectionUIManager.Instance;
        if (!HasActiveContext(manager)) return "当前没有可填写价格的鉴定委托，请先打开客户物品的鉴定页。";
        var item = manager!.currentInspectingItem;
        if (!AppraisalValuePolicy.TryGetPrice(item, out int value))
            return "当前物品没有有效的原生鉴定价，已保留原输入。";

        // The native Update rewrites this field from APPRAISAL_DEFAULT_VALUE each frame.
        // Only update the editable proposal, never APPRAISAL_CLIENT_VALUE or item value/quality.
        item.ModifyTag("APPRAISAL_DEFAULT_VALUE", state => state.Enable().SetInt(value));
        manager.apprisalServiceInputField.SetTextWithoutNotify(value.ToString(CultureInfo.InvariantCulture));
        manager.apprisalServiceInputField.ForceLabelUpdate();
        ModEntry.Log?.Log($"鉴定填价：{item.identifier} #{item.uniqueId} = {value}；等待玩家提交。");
        return $"鉴定价格已填入：{value} 信用点。请核对后点击原生提交按钮。";
    }

    private sealed class Binding
    {
        private readonly InspectionUIManager manager;
        private readonly TMP_InputField input;
        private readonly RectTransform frame;
        private readonly RectTransform root;
        private readonly CustomUIElement button;
        private readonly Vector3[] corners = new Vector3[4];

        internal Binding(InspectionUIManager manager)
        {
            this.manager = manager;
            input = manager.apprisalServiceInputField;
            frame = manager.appraisalPanel.transform as RectTransform ??
                throw new InvalidOperationException("无法确认原生鉴定价格区的窗口。");
            root = new GameObject("DeepSpaceQoL appraisal fill-only toolbar", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(frame, false);
            root.anchorMin = root.anchorMax = root.pivot = Vector2.zero;
            root.sizeDelta = new Vector2(84, 30);
            var layout = root.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            try
            {
                var builder = CustomUIManager.Instance.BuildInto(root, false, ModEntry.Id + ".appraisal").UseMachineryStyle();
                builder.AddButton("鉴定", () =>
                {
                    if (!Matches(InspectionUIManager.Instance) || !HasActiveContext(manager)) return;
                    ModEntry.Execute(FillPrice);
                }, "fill_price");
                button = builder.Last;
                button.SetTooltip("按游戏真实鉴定规则填入当前物品的价格。只填价，不提交、不修改真假或品质；最终提交由你确认。");
                button.SetPreferredSize(84, 30).SetFlexible(1, 0);
                CustomUIBuilder.RebuildLayout(root);
                Position();
            }
            catch
            {
                root.gameObject.SetActive(false);
                Object.Destroy(root.gameObject);
                throw;
            }
        }

        internal bool Matches(InspectionUIManager? current) => current != null && manager != null &&
            ReferenceEquals(current, manager) && ReferenceEquals(input, current.apprisalServiceInputField) &&
            frame != null && ReferenceEquals(frame, current.appraisalPanel?.transform) && root != null;

        internal void Refresh()
        {
            SetVisible(true);
            button.SetInteractable(HasActiveContext(manager));
            Position();
        }

        internal void SetVisible(bool visible)
        {
            if (root != null && root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        }

        private void Position()
        {
            // Keep the native price input, +/- and submit controls unchanged. The added control
            // sits just outside the price panel, vertically aligned with its price input.
            var inputRect = input.transform as RectTransform;
            if (inputRect == null || frame == null) return;
            inputRect.GetWorldCorners(corners);
            float centerY = (frame.InverseTransformPoint(corners[0]).y + frame.InverseTransformPoint(corners[1]).y) * 0.5f;
            float x = frame.rect.xMax + 6f;
            float y = centerY - root.rect.height * 0.5f;
            root.localPosition = new Vector3(x, y, 0);

            // For narrow screens choose the other outside edge instead of clipping off-screen.
            var canvas = frame.GetComponentInParent<Canvas>()?.transform as RectTransform;
            if (canvas == null) return;
            root.GetWorldCorners(corners);
            float right = corners.Max(p => canvas.InverseTransformPoint(p).x);
            if (right > canvas.rect.xMax - 4f)
                root.localPosition = new Vector3(frame.rect.xMin - root.rect.width - 6f, y, 0);
            root.SetAsLastSibling();
        }

        internal void Dispose()
        {
            if (root == null) return;
            root.gameObject.SetActive(false);
            Object.Destroy(root.gameObject);
        }
    }
}
