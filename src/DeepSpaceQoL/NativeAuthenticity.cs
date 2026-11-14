using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DeepSpaceQoL;

internal static class NativeAuthenticity
{
    private static InspectionUIManager? owner;
    private static RectTransform? root;

    internal static void Refresh()
    {
        var manager = InspectionUIManager.Instance;
        if (owner != manager || root == null && owner != null) Dispose();
        bool show = ModEntry.Enabled && manager != null && manager.panel != null && manager.panel.activeInHierarchy &&
            AuthenticityPolicy.TypeOf(manager.currentInspectingItem) != AuthenticityPolicy.Kind.Unsupported;
        if (!show) { if (root != null) root.gameObject.SetActive(false); return; }
        if (root == null && CustomUIManager.Instance != null)
        {
            owner = manager;
            root = new GameObject("DeepSpaceQoL authenticity button", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(manager!.panel!.transform, false);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0);
            root.anchoredPosition = new Vector2(0, 18);
            root.sizeDelta = new Vector2(116, 32);
            root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var builder = CustomUIManager.Instance.BuildInto(root, false, ModEntry.Id + ".authenticity").UseMachineryStyle();
            builder.AddButton("真伪鉴定", () => ModEntry.Execute(Inspect), "inspect_authenticity");
            builder.Last.SetTooltip("检查香烟、票券或注射器。有具体问题且允许对质时自动进入原生对质；正品显示鉴定结果。");
            builder.Last.SetPreferredSize(116, 32).SetFlexible(1, 0);
            CustomUIBuilder.RebuildLayout(root);
        }
        if (root != null) { root.gameObject.SetActive(true); root.SetAsLastSibling(); }
    }

    internal static string Inspect()
    {
        var manager = InspectionUIManager.Instance;
        var item = manager?.currentInspectingItem;
        if (manager == null || manager.panel == null || !manager.panel.activeInHierarchy || item == null)
            return "请先用放大镜打开物品检查页。";
        var result = AuthenticityPolicy.Evaluate(item);
        if (result.Status != AuthenticityPolicy.Verdict.Problem || result.IssueIndex < 0) return result.Description;
        if (GeneralHelper.IsItemOwned(item)) return result.Description + "这是自有物品，无需对质。";
        if (AuthenticityPolicy.Has(item, "ALREADY_CONFRONTED_TAG")) return result.Description + "该物品已经对质过。";
        if (!result.ClaimedGenuine) return result.Description + "问题已经公开，无需重复对质。";
        if (InspectionUIManager.IsConfrontLocked() || PlayerStore.Instance?.currentClientInstance == null ||
            DialogUIManager.Instance == null || manager.confrontPanel == null || !manager.confrontPanel.activeInHierarchy)
            return result.Description + "当前原生对质不可用。";
        // Reject a stale inspection window left open after its seller/item has changed.
        var entry = EmporiumEntry.Instance;
        if (entry == null || (!entry.frontInvinvElement.childItems.Contains(item) && !entry.backInvinvElement.childItems.Contains(item)))
            return result.Description + "物品不在当前客户交易区，未触发对质。";
        var toggles = new[] { manager.toggle1, manager.toggle2, manager.toggle3, manager.toggle4, manager.toggle5 };
        if (toggles.Any(t => t == null) || !toggles[result.IssueIndex].gameObject.activeInHierarchy)
            return result.Description + "原生问题选项不可用。";
        manager.ResetAllConfrontToggle();
        toggles[result.IssueIndex].isOn = true;
        manager.OnConfront();
        return result.Description + "已进入原生对质。";
    }

    internal static void Dispose()
    {
        if (root != null) { root.gameObject.SetActive(false); Object.Destroy(root.gameObject); }
        root = null; owner = null;
    }
}
