using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DeepSpaceQoL.Core;
using UnityEngine;

namespace DeepSpaceQoL;

/// <summary>Stages original owned items on the native weighted sale counter. Never completes a sale.</summary>
public static class StockingService
{
    private sealed class StockItem
    {
        public GameItem Item = null!;
        public GameInventory Source = null!;
        public GridShape OriginalShape = null!;
        public List<ItemFeature> OriginalFeatures = null!;
        public readonly List<GridShape> Shapes = new();
        public long Quote;
    }

    public static string Run()
    {
        var store = PlayerStore.Instance;
        var entry = EmporiumEntry.Instance;
        var client = store?.currentClientInstance?.GetClientBlueprint();
        if (store == null || entry == null || client == null || !store.isClientArrived)
            return "尚无到店买家。";
        if (MapUIManager.Instance != null && MapUIManager.Instance.IsOutside)
            return "请回到店内再上货。";
        if (!client.IsClientBuying() || client.isBusinessComplete || NegociationUIManager.IsTradeLocked())
            return "当前顾客不是可交易买家，或交易暂时锁定。";
        if (!client.useClientBudget)
            return "当前顾客没有明确预算，请手动上货。";

        var counter = entry.backInvinvElement;
        if (counter?.inventoryShape == null) return "出售台尚未就绪。";
        var counterIssue = BuyerStockPolicy.CheckCounter(client, counter.childItems);
        if (counterIssue != BuyerStockPolicy.CounterIssue.None) return BuyerStockPolicy.Explain(counterIssue);
        long initialTotal = StagedValue(counter, client);
        long remaining = (long)client.GetBudget() - initialTotal;
        if (remaining <= 0)
            return initialTotal > client.GetBudget() ? "台上商品已超过买家预算，请先取下一部分。" : "台上商品已贴合买家剩余预算，请确认出售。";

        var stocks = new List<StockItem>();
        int unappraised = 0;
        var seen = new HashSet<GameItem>();
        foreach (var inventory in StorageCatalog.GetSources())
        {
            if (inventory == null || inventory == counter) continue;
            foreach (var item in inventory.childItems.ToArray())
            {
                if (item == null || !seen.Add(item) || item.parentInventory != inventory ||
                    !StorageRules.CanAutomate(item) || item.modifiedShape == null || item.unitCount < 1 ||
                    item.MaxNumRemove() < item.unitCount || !BuyerStockPolicy.Accepts(client, item)) continue;
                // Selling a filled container would also sell its contents, including protected contents.
                if (!StowService.IsMovableStock(item) || StorageCatalog.IsStorage(item) || item.IsTag("SYSTEM_TAG") || item.IsGameItemType("MACHINE") ||
                    item.IsTag("WATER_TANK_TAG") || item.IsTag("STORAGE_COORDINATOR_TAG") ||
                    (item.identifier ?? "").StartsWith("storage_bay", StringComparison.Ordinal) ||
                    GraphUtils.FindAllChildrenType<GameItem>(item).Count != 0) continue;
                if (client.CanClientExposeAnyFeatureDeep(item)) { unappraised++; continue; }
                long value = PreviewValue(item, client);
                if (value <= 0 || value > remaining) continue;
                stocks.Add(new StockItem
                {
                    Item = item, Source = inventory, OriginalShape = item.modifiedShape.Clone(),
                    OriginalFeatures = new List<ItemFeature>(item.itemFeatures), Quote = value
                });
            }
        }
        if (stocks.Count == 0)
            return unappraised > 0 ? "没有可自动上货的商品；部分物品需要先鉴定，请手动检查。" : "没有符合需求且未锁定、价格不超过剩余预算的商品。";

        var bounds = counter.inventoryShape;
        int width = bounds.globalWidth, height = bounds.globalHeight;
        var occupied = new HashSet<int>();
        for (int y = bounds.minY; y <= bounds.maxY; y++)
            for (int x = bounds.minX; x <= bounds.maxX; x++)
                if (bounds.Get(x, y) != 0) occupied.Add((y - bounds.minY) * width + x - bounds.minX);
        foreach (var staged in counter.childItems)
            foreach (int cell in Cells(staged.modifiedShape, bounds)) occupied.Add(cell);

        var candidates = new List<StockPlanner.Candidate>();
        bool geometryLimited = false;
        var geometryWatch = Stopwatch.StartNew();
        int inspected = 0;
        foreach (var stock in stocks.OrderByDescending(s => s.Quote))
        {
            if (inspected++ >= 256 || geometryWatch.ElapsedMilliseconds > 1800)
            {
                geometryLimited = true;
                break;
            }
            var options = new List<StockPlanner.Option>();
            var masks = new HashSet<string>(StringComparer.Ordinal);
            for (int orientation = 0; orientation < 4; orientation++)
            {
                for (int flip = 0; flip < 2; flip++)
                {
                    var template = new GridShapeBuilder(stock.Item.modifiedShape).SetOrientation(orientation).SetFlipped(flip != 0);
                    for (int y = bounds.minY; y <= bounds.maxY - template.globalHeight + 1; y++)
                        for (int x = bounds.minX; x <= bounds.maxX - template.globalWidth + 1; x++)
                        {
                            var shape = new GridShapeBuilder(template).SetPosition(x, y);
                            var slot = counter.TryInventorySlot(stock.Item, stock.Item.unitCount, shape);
                            if (slot == null || slot.numTransfer < stock.Item.unitCount || slot.targetItem != null) continue;
                            int[] cells = Cells(shape, bounds).ToArray();
                            if (cells.Length == 0 || cells.Any(occupied.Contains)) continue;
                            if (!masks.Add(string.Join(",", cells))) continue;
                            int token = stock.Shapes.Count;
                            stock.Shapes.Add(shape);
                            options.Add(new StockPlanner.Option { Token = token, Cells = cells });
                        }
                }
            }
            if (options.Count > 0)
                candidates.Add(new StockPlanner.Candidate
                {
                    Token = stocks.IndexOf(stock), VarietyKey = stock.Item.identifier ?? "", Value = stock.Quote, Options = options
                });
        }
        var variety = BuyerStockPolicy.AcceptedStagedItems(client, counter.childItems)
            .Select(i => i.identifier).ToArray();
        var plan = StockPlanner.Solve(width * height, occupied, candidates, remaining, client.isRequireVariety, variety);
        int moved = 0;
        bool priceChanged = false;
        foreach (var choice in plan.Choices)
        {
            if (store.currentClientInstance?.GetClientBlueprint() != client || client.isBusinessComplete ||
                NegociationUIManager.IsTradeLocked()) break;
            counterIssue = BuyerStockPolicy.CheckCounter(client, counter.childItems);
            if (counterIssue != BuyerStockPolicy.CounterIssue.None) break;
            var stock = stocks[choice.CandidateToken];
            var item = stock.Item;
            if (item.parentInventory != stock.Source || !StorageRules.CanAutomate(item) || !BuyerStockPolicy.Accepts(client, item)) continue;
            if (client.isRequireVariety && BuyerStockPolicy.AcceptedStagedItems(client, counter.childItems)
                    .Any(i => i.identifier == item.identifier)) continue;
            if (stock.Quote > (long)client.GetBudget() - StagedValue(counter, client)) continue;
            var slot = counter.TryInventorySlot(item, item.unitCount, stock.Shapes[choice.OptionToken]);
            if (slot == null || slot.numTransfer < item.unitCount || slot.targetItem != null) continue;
            stock.OriginalShape = item.modifiedShape.Clone();
            stock.OriginalFeatures = new List<ItemFeature>(item.itemFeatures);
            int amount = item.unitCount;
            try
            {
                int accepted = slot.TryAcceptOnce(amount);
                if (accepted != amount || item.parentInventory != counter)
                {
                    Restore(stock, counter);
                    continue;
                }
                if (!BuyerStockPolicy.Accepts(client, item) || client.CanClientExposeAnyFeatureDeep(item) ||
                    BuyerStockPolicy.CheckCounter(client, counter.childItems) != BuyerStockPolicy.CounterIssue.None ||
                    StagedValue(counter, client) > client.GetBudget())
                {
                    Restore(stock, counter);
                    priceChanged = true;
                    continue;
                }
                moved++;
            }
            catch
            {
                Restore(stock, counter);
                throw;
            }
        }
        store.OnWeightedAreaChanged();
        long now = StagedValue(counter, client);
        long left = Math.Max(0L, (long)client.GetBudget() - now);
        if (counterIssue != BuyerStockPolicy.CounterIssue.None)
            return $"本轮此前已上货 {moved} 件。" + BuyerStockPolicy.Explain(counterIssue);
        if (moved == 0)
            return priceChanged ? "顾客需求或报价变化，已退回相关商品；请手动检查后再上货。" : "出售台空间不足或没有可放下的预算组合；可先出售台上商品后再按一次。";
        return $"已上货 {moved} 件，台上可售报价 {now}，预算余量 {left}。请手动确认出售" +
               (left == 0 ? "。" : "，出售后可再次上货。") +
               (plan.SearchLimited || geometryLimited ? " 本次采用限时搜索所得方案。" : "") +
               (priceChanged ? " 部分需求或报价变化的商品已退回。" : "");
    }

    private static IEnumerable<int> Cells(GridShape shape, GridShape bounds)
    {
        if (shape == null) yield break;
        for (int y = Math.Max(shape.minY, bounds.minY); y <= Math.Min(shape.maxY, bounds.maxY); y++)
            for (int x = Math.Max(shape.minX, bounds.minX); x <= Math.Min(shape.maxX, bounds.maxX); x++)
                if (shape.Get(x, y) == 1) yield return (y - bounds.minY) * bounds.globalWidth + x - bounds.minX;
    }

    private static long StagedValue(GameGridInventory counter, StoreClient client)
    {
        long sum = 0;
        foreach (var item in BuyerStockPolicy.AcceptedStagedItems(client, counter.childItems))
            sum = checked(sum + Math.Max(0L, item.GetNegociatedValue()));
        return sum;
    }

    // Preview only swaps a feature-list reference in a try/finally. It never invokes item/client actions.
    // A quote changed by a custom buyer callback is checked again after native placement and rolled back.
    private static long PreviewValue(GameItem item, StoreClient client)
    {
        var original = item.itemFeatures;
        item.itemFeatures = new List<ItemFeature>(original);
        try
        {
            void Add(ItemFeature f)
            {
                if (!item.itemFeatures.Any(existing => existing.identifier == f.identifier)) item.itemFeatures.Add(f);
            }
            if (PlayerStore.Instance.IsCurrentClientAcceptMarkup()) Add(ItemFeatureList.RetailMarkUp());
            if (client.perfumeInfluence == 1) Add(ItemFeatureList.NeuroActivePerfumeSelling());
            else if (client.perfumeInfluence == 2) Add(ItemFeatureList.PheromonePerfumeSelling());
            if (HealthData.HasAnyAddiction() && PlayerStore.Instance.healthData.GetTotalAddictionPenalty() != 0)
                Add(ItemFeatureList.AddictionSellPenalty());
            if (StartingPerk.IsAwfulNegotiator()) Add(ItemFeatureList.AwfulNegotiatorSellPenalty());
            if (client.clientFaction == "FACTION_UPPER_LEVEL" && StoreReputation.IsPerkUnlocked("UL_TIPS"))
                Add(ItemFeatureList.Pourboire());
            if (client.isRequireVariety) Add(ItemFeatureList.VarietyBuyerBonus());
            if (client.IsFaction("FACTION_TOURIST")) Add(ItemFeatureList.TouristTax());
            if (client.identifier == "foodBankBuyer") Add(ItemFeatureList.DiscountSell50());
            if (item.IsTag("CONTRABAND_ITEM_TAG"))
            {
                switch (item.GetTagReadonly("CONTRABAND_LEVEL").GetString())
                {
                    case "CONTRABAND_LEVEL_LOW": Add(ItemFeatureList.ContrabandLowMarkUp()); break;
                    case "CONTRABAND_LEVEL_MID": Add(ItemFeatureList.ContrabandMidMarkUp()); break;
                    case "CONTRABAND_LEVEL_HIGH": Add(ItemFeatureList.ContrabandHighMarkUp()); break;
                    case "CONTRABAND_LEVEL_CRITICAL": Add(ItemFeatureList.ContrabandCriticalMarkUp()); break;
                }
            }
            if (item.IsGameItemType("RESTRICTED_1")) Add(ItemFeatureList.RestrictedLowMarkUp());
            else if (item.IsGameItemType("RESTRICTED_2")) Add(ItemFeatureList.RestrictedMidMarkUp());
            else if (item.IsGameItemType("RESTRICTED_3")) Add(ItemFeatureList.RestrictedHighMarkUp());
            return item.GetCurrentValue(useRetailMarkup: true, withChild: true, includeEvents: true);
        }
        finally { item.itemFeatures = original; }
    }

    private static void Restore(StockItem stock, GameGridInventory counter)
    {
        var item = stock.Item;
        if (item.parentInventory == stock.Source)
        {
            item.itemFeatures = new List<ItemFeature>(stock.OriginalFeatures);
            return;
        }
        // Exact original location was vacated by this same synchronous operation.
        var restore = stock.Source.TryInventorySlot(item, item.unitCount, stock.OriginalShape);
        if (restore != null && restore.numTransfer >= item.unitCount && restore.targetItem == null)
        {
            int amount = item.unitCount;
            if (restore.TryAcceptOnce(amount) == amount && item.parentInventory == stock.Source)
            {
                item.itemFeatures = new List<ItemFeature>(stock.OriginalFeatures);
                item.Validate();
                return;
            }
        }
        // Do not discard an item when an unrelated callback changes the source inventory.
        if (item.parentInventory == null && stock.Source.CheckInventorySlotStillValid(item, stock.OriginalShape, item.modifiedState))
        {
            item.modifiedShape = stock.OriginalShape.Clone();
            if (stock.Source.UncheckedAccept(item))
            {
                item.itemFeatures = new List<ItemFeature>(stock.OriginalFeatures);
                item.Validate();
                return;
            }
        }
        throw new InvalidOperationException(item.parentInventory == null
            ? "商品恢复失败且尚未重新入库；请不要保存，保留日志并重新载入存档。"
            : "商品仍在当前容器但无法恢复原位置；请检查出售台后再继续。");
    }
}
