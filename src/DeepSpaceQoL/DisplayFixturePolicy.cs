namespace DeepSpaceQoL;

internal static class DisplayFixturePolicy
{
    internal const string RegionTag = "DEEPSPACE_QOL_DISPLAY_V1";
    internal const string LayoutTag = "DEEPSPACE_QOL_DISPLAY_LAYOUT_V1";
    internal enum Region { None, Signs, Lamps }
    private static readonly HashSet<string> Signs = new(StringComparer.Ordinal)
    {
        "sign_water", "sign_food", "sign_medical", "sign_household", "sign_material", "sign_ruined_machine",
        "sign_firearm", "sign_firearm_heavy", "sign_berry", "sign_expedition", "sign_luxury",
        "cert1", "cert2", "cert3", "cert4", "business_permit", "permit_gun_1", "permit_gun_2", "permit_gun_3"
    };
    internal static Region For(GameItem? item) => item?.identifier == "black_lamp" ? Region.Lamps :
        item?.identifier != null && Signs.Contains(item.identifier) ? Region.Signs : Region.None;
    internal static string Key(Region region) => region == Region.Signs ? "signs" : region == Region.Lamps ? "lamps" : "";
    internal static bool IsSaved(GameItem item, Region region) => region != Region.None &&
        item.state?.dict != null && item.state.dict.TryGetValue(RegionTag, out var tag) && tag.IsEnabled() && tag.GetString() == Key(region);

    /// <summary>The native showcase save bag has one inventory when decoded without mods.
    /// Flatten only our two runtime inventories to that existing native inventory index.</summary>
    internal static void FlattenSavedRoot(SaveItemNode root, IReadOnlyDictionary<int, int> mapping)
    {
        if (root.childItems.Count != root.childItemInventoryNode.Count)
            throw new InvalidOperationException("陈列区保存关联数量不一致，已停止保存转换。");
        for (int i = 0; i < root.childItemInventoryNode.Count; i++)
            if (mapping.TryGetValue(root.childItemInventoryNode[i], out int target)) root.childItemInventoryNode[i] = target;
    }

    internal static void PrepareNativeSave(List<SaveItemNode> nodes, SaveItemNode root,
        IReadOnlyDictionary<int, int> mapping, int nativeWidth, int nativeHeight)
    {
        if (root.childItems.Count != root.childItemInventoryNode.Count)
            throw new InvalidOperationException("陈列区保存关联数量不一致，已停止保存转换。");
        var byId = nodes.ToDictionary(n => n.uuid);
        for (int i = 0; i < root.childItems.Count; i++)
        {
            if (!mapping.ContainsKey(root.childItemInventoryNode[i])) continue;
            var node = byId[root.childItems[i]];
            var shape = node.itemShape;
            var modified = node.itemModifiedShape;
            // Save the real tray positions in an optional native tag, and use ordinary
            // case coordinates in the vanilla fields. Without the mod they may overlap
            // a full case, but are visible/recoverable rather than clipped off-grid.
            string layout = FormattableString.Invariant($"1:{shape.minX},{shape.minY},{modified.minX},{modified.minY}");
            node.itemState.dict[LayoutTag] = new TagState(LayoutTag, LayoutTag) { valueEnabled = true, valueString = layout };
            shape.SetPosition(Math.Max(0, Math.Min(shape.minX, nativeWidth - shape.globalWidth)),
                Math.Max(0, Math.Min(shape.minY, nativeHeight - shape.globalHeight)));
            modified.SetPosition(Math.Max(0, Math.Min(modified.minX, nativeWidth - modified.globalWidth)),
                Math.Max(0, Math.Min(modified.minY, nativeHeight - modified.globalHeight)));
        }
        FlattenSavedRoot(root, mapping);
    }

    internal static bool TryRestoreLayout(GameItem item, out GridShape shape, out GridShape modified)
    {
        shape = item.shape; modified = item.modifiedShape;
        if (item.state?.dict == null || !item.state.dict.TryGetValue(LayoutTag, out var tag) || !tag.IsEnabled()) return true;
        string raw = tag.GetString() ?? "";
        if (!raw.StartsWith("1:", StringComparison.Ordinal)) return false;
        string[] fields = raw.Substring(2).Split(',');
        if (fields.Length != 4) return false;
        var coordinates = new int[4];
        for (int i = 0; i < fields.Length; i++)
            if (!int.TryParse(fields[i], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out coordinates[i]) ||
                coordinates[i] < 0 || coordinates[i] > 256) return false;
        shape = new GridShapeBuilder(item.shape).SetPosition(coordinates[0], coordinates[1]).Build();
        modified = new GridShapeBuilder(item.modifiedShape).SetPosition(coordinates[2], coordinates[3]).Build();
        return true;
    }

    internal static Dictionary<int, int> InventoryMap(GraphNodeStorage bag, GameInventory native, IEnumerable<GameInventory> extras)
    {
        var indices = new Dictionary<GraphNodeStorage, int>();
        var queue = new Queue<GraphNodeStorage>(bag.children);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node is GameItem || indices.ContainsKey(node)) continue;
            indices.Add(node, indices.Count);
            foreach (var child in node.children) queue.Enqueue(child);
        }
        var mapping = new Dictionary<int, int>();
        if (indices.TryGetValue(native, out int nativeIndex))
            foreach (var extra in extras)
                if (indices.TryGetValue(extra, out int index) && index != nativeIndex) mapping[index] = nativeIndex;
        return mapping;
    }
}
