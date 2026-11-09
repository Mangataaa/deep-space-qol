namespace DeepSpaceQoL;

/// <summary>Native buyer preferences plus pre-placement protection from security callbacks.</summary>
internal static class BuyerStockPolicy
{
    internal enum CounterIssue { None, SecurityRisk, DuplicateVariety }

    internal static bool IsSecurity(StoreClient client) => client.isSecurity ||
        string.Equals(client.clientFaction, StoreClient.FACTION_SECURITY, StringComparison.Ordinal) || client.IsTag("SECURITY");

    internal static bool HasSecurityRisk(GameItem item) => IsFlagged(item) ||
        GraphUtils.FindAllChildrenType<GameItem>(item).Any(IsFlagged);

    private static bool IsFlagged(GameItem item) => item.IsTag("CONTRABAND_ITEM_TAG") ||
        item.IsTag("CONTRABAND") || item.IsTag("STOLEN_TAG");

    internal static bool Accepts(StoreClient client, GameItem item)
    {
        // Security reacts when an item enters the counter, before any sale or later rollback.
        // Never use detection chance, heat, concealment or a buyer override to allow a stolen item.
        if (IsSecurity(client) && HasSecurityRisk(item)) return false;
        return client.IsClientBuyingThisItem(item);
    }

    internal static IEnumerable<GameItem> AcceptedStagedItems(StoreClient client, IEnumerable<GameItem> items) =>
        items.Where(item => GeneralHelper.IsItemOwned(item) && Accepts(client, item));

    internal static CounterIssue CheckCounter(StoreClient client, IEnumerable<GameItem> items)
    {
        var owned = items.Where(GeneralHelper.IsItemOwned).ToArray();
        if (IsSecurity(client) && owned.Any(HasSecurityRisk)) return CounterIssue.SecurityRisk;
        if (client.isRequireVariety)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in owned)
                if (Accepts(client, item) && !ids.Add(item.identifier)) return CounterIssue.DuplicateVariety;
        }
        return CounterIssue.None;
    }

    internal static string Explain(CounterIssue issue) => issue switch
    {
        CounterIssue.SecurityRisk => "当前是治安部顾客，称重区已有违禁品或赃物（含容器内部）。请先手动收回这些物品，本轮未继续上货。",
        CounterIssue.DuplicateVariety => "当前顾客要求物品多样性，称重区已有同品种的多件商品。请先手动保留每种一件，再继续上货。",
        _ => ""
    };
}
