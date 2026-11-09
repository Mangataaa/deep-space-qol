using DeepSpaceQoL.Core;
using Newtonsoft.Json;

namespace DeepSpaceQoL;

internal static class StorageRules
{
    private const string PolicyTag = "DEEPSPACE_QOL_STORAGE_V1";
    private static readonly Dictionary<GameItem, (string raw, StoragePolicy policy)> Cache = new();

    public static void ClearCache() => Cache.Clear();

    public static StoragePolicy Policy(GameItem owner)
    {
        string raw = owner.IsTag(PolicyTag) ? owner.GetTagReadonly(PolicyTag).GetString() ?? "" : "";
        if (Cache.TryGetValue(owner, out var entry) && entry.raw == raw) return entry.policy;
        StoragePolicy policy;
        try
        {
            policy = string.IsNullOrEmpty(raw) ? new StoragePolicy() : JsonConvert.DeserializeObject<StoragePolicy>(raw) ?? throw new InvalidOperationException("储物规则为空");
            if (!policy.IsValid())
                throw new InvalidOperationException("储物规则版本或内容不兼容");
        }
        catch (Exception ex)
        {
            // Preserve the unreadable data. Never silently discard a sale lock.
            policy = new StoragePolicy { Version = -1, ContainerLocked = true };
            ModEntry.Log?.Warning($"{owner.identifier} 的储物规则无法读取，自动操作已锁定：{ex.Message}");
        }
        Cache[owner] = (raw, policy);
        return policy;
    }

    public static void Save(GameItem owner, StoragePolicy policy)
    {
        if (policy.Version != 1) throw new InvalidOperationException("规则数据不兼容，已保留原数据；请先恢复兼容版本。");
        string raw = JsonConvert.SerializeObject(policy);
        owner.ModifyTag(PolicyTag, s => s.Enable().SetString(raw));
        Cache[owner] = (raw, policy);
        RefreshCaptions(owner);
    }

    public static void RefreshCaptions(GameItem owner)
    {
        var tabs = StorageCatalog.TabsOf(owner);
        if (tabs == null) return;
        var policy = Policy(owner);
        for (int i = 0; i < tabs.count; i++)
        {
            var rule = policy.Page(i);
            string name = StorageExpansionHelper.GetTabName(owner, i) ?? (!string.IsNullOrWhiteSpace(rule.Label) ? rule.Label : "页 " + (i + 1));
            string categories = string.Join("/", CategoryMatcher.ExplicitTokens(rule.Categories).Where(t => t.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Substring(4)).Where(t => TypeHelper.ALL_TYPES_LIST.Contains(t)).Select(TypeHelper.GetTypeDisplayName).Take(2));
            tabs.SetCaption(i, (policy.ContainerLocked || rule.SaleLocked ? "[锁] " : "") + name +
                (string.IsNullOrEmpty(categories) || string.Equals(name, categories, StringComparison.Ordinal) ? "" : " [" + categories + "]"));
        }
        owner.contentWindow?.Validate();
    }

    public static bool Matches(PageRule rule, GameItem item) =>
        CategoryMatcher.Matches(rule.Categories, item.identifier, Tags(item));

    private static IEnumerable<string> Tags(GameItem item)
    {
        foreach (var type in item.GetGameItemType()) yield return type;
        if (item.modifiedState != null)
            foreach (var tag in item.modifiedState.dict)
                if (tag.Value.IsEnabled()) yield return tag.Key;
    }

    public static bool Allows(GameInventory inventory, GameItem item)
    {
        if (ReferenceEquals(item.parentInventory, inventory)) return true;
        var page = StorageCatalog.Find(inventory);
        if (page == null || !page.Rule.Restrict || !CategoryMatcher.HasCategories(page.Rule)) return true;
        return Matches(page.Rule, item);
    }

    public static bool IsLocked(GameInventory inventory)
    {
        // Every ancestor inventory must be checked: a locked tab protects nested boxes too.
        var nodes = new List<GameInventory> { inventory };
        nodes.AddRange(GraphUtils.FindParentsType<GameInventory>(inventory, passthrough: _ => true));
        foreach (var ancestor in nodes)
        {
            var page = StorageCatalog.Find(ancestor);
            if (page?.Owner == null) continue;
            var policy = Policy(page.Owner);
            if (policy.IsSaleProtected(page.Index)) return true;
        }
        foreach (var owner in GraphUtils.FindParentsType<GameItem>(inventory, passthrough: _ => true))
            if (Policy(owner).ContainerLocked) return true;
        return false;
    }

    /// <summary>One-click access in either direction; manual placement/category rules remain separate.</summary>
    public static bool CanAutoAccess(GameInventory inventory) => inventory != null &&
        !IsLocked(inventory) && !StorageCatalog.IsNativeAccessLocked(inventory);

    /// <summary>Portable packages move whole only when no contained item/window has a protection lock.</summary>
    internal static bool HasProtectedContents(GameItem item) =>
        Policy(item).IsSaleProtected() || LockHelper.IsCurrentlyLocked(item) || item.IsTag("ITEM_BEHIND_LOCK_TAG") ||
        GraphUtils.FindAllChildrenType<PixelWindow>(item).Any(w => w.isLocked) ||
        GraphUtils.FindAllChildrenType<GameInventory>(item).Any(i => i.IsInsertLocked() || i.IsRemoveLocked()) ||
        GraphUtils.FindAllChildrenType<GameItem>(item).Any(i => Policy(i).IsSaleProtected() ||
            LockHelper.IsCurrentlyLocked(i) || i.IsTag("ITEM_BEHIND_LOCK_TAG"));

    public static bool CanAutomate(GameItem item)
    {
        if (item == null || item.parentInventory == null || item.unitCount <= 0) return false;
        if (item is GameItemElement element && element.IsDestroyed()) return false;
        if (!GeneralHelper.IsItemOwned(item) || ItemPinHelper.IsPinned(item) || !item.MayRemove()) return false;
        if (!CanAutoAccess(item.parentInventory)) return false;
        var policy = Policy(item);
        // Selling a whole box must never bypass protection on one of its tabs.
        return !policy.IsSaleProtected();
    }
}
