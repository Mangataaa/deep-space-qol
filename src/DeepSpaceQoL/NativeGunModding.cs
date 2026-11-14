using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DeepSpaceQoL;

/// <summary>A native-styled action beside the gun commission's original hand-in button.</summary>
internal static class NativeGunModding
{
    private static NegociationUIManager? owner;
    private static NegotiationTabletFrame? frame;
    private static RectTransform? root;
    private static TabletButton? button;

    internal static void Refresh()
    {
        var manager = NegociationUIManager.Instance;
        if (manager == null || !ReferenceEquals(owner, manager) || frame == null || root == null)
        {
            Dispose();
            if (manager == null || manager.uiObject == null || !manager.IsOpen || !GunModdingService.HasOrder()) return;
            var candidate = manager.uiObject.GetComponentInChildren<NegotiationTabletFrame>(true);
            if (candidate == null || candidate.panel == null || candidate.buttonHost == null) return;
            owner = manager; frame = candidate;
            root = new GameObject("DeepSpaceQoL gun commission action", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(frame.panel, false); root.anchorMin = root.anchorMax = root.pivot = new Vector2(0, 1);
            root.sizeDelta = new Vector2(132, 42); root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            button = TabletUI.BigButton(frame, root, 0, 0, "一键改装", () => ModEntry.Execute(GunModdingService.Start), 132);
        }
        bool show = ModEntry.Enabled && manager.IsOpen && GunModdingService.HasOrder();
        root.gameObject.SetActive(show);
        if (!show) return;
        // The native hand-in button begins at x=153 in this host. Use its vacant left margin.
        // Parent outside buttonHost: the game clears that host when refreshing commission rows.
        var host = frame.buttonHost;
        root.localPosition = frame.panel.InverseTransformPoint(host.TransformPoint(new Vector3(host.rect.xMin + 8, host.rect.yMax, 0)));
        root.SetAsLastSibling();
        button!.SetLabel(GunModdingService.IsRunning ? "匹配中…" : "一键改装");
        button.SetInteractable(!StowService.IsRunning && !NegociationUIManager.IsTradeLocked() && GunOrderData.ResolveOrderGun() != null);
    }

    internal static void Dispose()
    {
        if (root != null) { root.gameObject.SetActive(false); Object.Destroy(root.gameObject); }
        root = null; frame = null; button = null; owner = null;
    }
}
