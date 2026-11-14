namespace DeepSpaceQoL;

/// <summary>Floating windows live on LetterboxManager.DockRoot, outside windowRoot.</summary>
internal static class VisibleItemViews
{
    internal static IEnumerable<TreeNodeRender> Roots()
    {
        var seen = new HashSet<TreeNodeRender>();
        var main = RenderHandler.current?.windowRoot;
        if (main != null && main.gameObject.activeInHierarchy && seen.Add(main)) yield return main;
        var windows = WindowsHandler.current?.visibleWindows;
        if (windows == null) yield break;
        foreach (var window in windows.ToArray())
        {
            var root = window?.handler;
            if (root == null || !window!.IsVisible() || !root.gameObject.activeInHierarchy || !seen.Add(root)) continue;
            // Main-root descendants were already enumerated. Include reparented dock windows.
            if (main != null && root.transform.IsChildOf(main.transform)) continue;
            yield return root;
        }
    }

    internal static IEnumerable<GameItemElement> Items()
    {
        var seen = new HashSet<GameItemElement>();
        foreach (var root in Roots())
            foreach (var node in root.GetComponentsInChildren<TreeNodeRender>(false))
            {
                if (node.element is not GameItemElement item || !ReferenceEquals(node, item.handler) ||
                    item.isDebugMenu || item.IsDestroyed() || item.unitCount <= 0 || item.middle == null ||
                    !item.middle.gameObject.activeInHierarchy || item.background?.image == null ||
                    !item.background.image.enabled || !seen.Add(item)) continue;
                yield return item;
            }
    }
}
