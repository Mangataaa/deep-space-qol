using DeepSpaceQoL.Core;

namespace DeepSpaceQoL;

/// <summary>Stages one existing whole gun for a purchase commission; never hands in or modifies it.</summary>
internal static class GunOrderStockingService
{
    internal static bool MatchesOrder(StoreClient client, GameItem gun)
    {
        var order = client.gunOrderData;
        if (client.clientIntent != StoreClient.ClientIntent.GUNSMITH || client.isBusinessComplete || order == null || order.isServiceOrder ||
            gun == null || !gun.IsTag("ADV_GUN_TAG") || GunHelper.IsServiceGun(gun) ||
            !order.mandatoryRequirements.All(r => r.IsMet(gun))) return false;
        if (BuyerStockPolicy.IsSecurity(client) && BuyerStockPolicy.HasSecurityRisk(gun)) return false;
        var required = GunsmithExpansionCompatibility.IsActive ? GunsmithExpansionCompatibility.RequiredParts(gun.identifier) : Array.Empty<string>();
        var children = GraphUtils.FindAllChildrenType<GameItem>(gun);
        return required.All(id => children.Any(part => part.identifier == id));
    }

    internal static bool IsCandidate(StoreClient client, GameItem gun, GameInventory source) =>
        GunModdingService.IsStockInventory(source) && ReferenceEquals(gun?.parentInventory, source) &&
        StorageRules.CanAutomate(gun!) && StorageRules.CanAutoAccess(source) &&
        !StorageRules.HasProtectedContents(gun!) && gun!.unitCount == 1 && gun.modifiedShape != null &&
        gun.MaxNumRemove() == 1 && MatchesOrder(client, gun) &&
        GraphUtils.FindAllChildrenType<GameItem>(gun).All(part => part.IsTag("WEAPON_MOD_TAG") && part.IsGameItemType("GUN_MOD") &&
            GeneralHelper.IsItemOwned(part) && !ItemPinHelper.IsPinned(part)) &&
        !client.CanClientExposeAnyFeatureDeep(gun);

    internal static string Run(StoreClient client)
    {
        if (client.isBusinessComplete || client.gunOrderData == null || NegociationUIManager.IsTradeLocked())
            return "当前枪械委托不可交易，或交易暂时锁定。";
        if (client.gunOrderData.isServiceOrder)
            return "这是客户自带枪的改装／维修委托；请使用台上的原委托枪和“一键改装”，不会用库存枪替换。";
        if (StowService.IsRunning || GunModdingService.IsRunning)
            return "请等待当前收纳或改装完成后再上货。";
        var store = PlayerStore.Instance; var entry = EmporiumEntry.Instance; var order = client.gunOrderData;
        if (store == null || entry?.backInvinvElement?.inventoryShape == null || !store.isClientArrived ||
            !ReferenceEquals(store.currentClientInstance?.GetClientBlueprint(), client)) return "当前没有到店的定制枪买家。";
        var counter = entry.backInvinvElement;
        var issue = BuyerStockPolicy.CheckCounter(client, counter.childItems);
        if (issue != BuyerStockPolicy.CounterIssue.None) return BuyerStockPolicy.Explain(issue);
        if (counter.childItems.Any(i => i.IsTag("ADV_GUN_TAG")))
            return order.IsAccepted() ? "交易区已有满足当前委托的枪械，请检查后手动交付。"
                : "交易区已有枪械，已保留你的选择。请先收回或使用“一键改装”，再继续上货。";

        // Commission rewards are not a shopping budget. Rank actual accepted bonuses first,
        // preserving cheaper stock when rewards tie; best-of-kind orders use their native reward.
        var candidates = new List<(GameItem Gun, GameInventory Source, long Reward, long Value, int Bonus)>();
        var seen = new HashSet<GameItem>();
        foreach (var source in StorageCatalog.GetSources())
        {
            if (ReferenceEquals(source, counter)) continue;
            foreach (var gun in source.childItems.ToArray())
                if (seen.Add(gun) && IsCandidate(client, gun, source))
                    candidates.Add((gun, source, order.GetTotalReward(gun), Math.Max(0L, gun.GetCurrentValue(false, true, false)), order.CountBonusMet(gun)));
        }
        if (candidates.Count == 0)
            return "未找到满足全部必需条件且可自动取用的库存整枪。请检查页锁／箱锁、所有条件及转换套件；也可先手动放枪，再用“一键改装”。";
        foreach (var candidate in candidates.OrderByDescending(c => c.Reward).ThenByDescending(c => c.Bonus).ThenBy(c => c.Value).ThenBy(c => c.Gun.uniqueId))
        {
            if (!ReferenceEquals(store.currentClientInstance?.GetClientBlueprint(), client) || !ReferenceEquals(client.gunOrderData, order) ||
                client.isBusinessComplete || NegociationUIManager.IsTradeLocked() || !IsCandidate(client, candidate.Gun, candidate.Source))
                return "委托或库存状态发生变化，已停止上货。";
            if (BuyerStockPolicy.CheckCounter(client, counter.childItems) != BuyerStockPolicy.CounterIssue.None ||
                counter.childItems.Any(i => i.IsTag("ADV_GUN_TAG"))) return "交易区状态已变化，已停止上货。";
            var slot = StowService.FindWholeSlot(counter, candidate.Gun);
            if (slot == null) continue;
            return Stage(client, candidate.Gun, candidate.Source, counter, slot);
        }
        return "有符合委托的库存枪，但交易区没有足够空间放下整枪，请先腾出空间。";
    }

    private static string Stage(StoreClient client, GameItem gun, GameInventory source, GameGridInventory counter, SlotMarker slot)
    {
        var order = client.gunOrderData; var store = PlayerStore.Instance;
        var selection = store.currentNegociatedItem; var shape = gun.modifiedShape.Clone();
        var members = GraphUtils.FindAllChildrenType<GameItem>(gun).ToArray();
        var snapshots = new[] { gun }.Concat(members).Select(item => (Item: item, State: new ItemStateSnapshot(item))).ToArray();
        var result = WholeItemTransferTransaction.Run(new[] { gun }, () =>
        {
            if (!IsCandidate(client, gun, source) || !ReferenceEquals(store.currentClientInstance?.GetClientBlueprint(), client) ||
                NegociationUIManager.IsTradeLocked() || !ReferenceEquals(client.gunOrderData, order)) throw new InvalidOperationException("委托或库存已变化。");
            if (slot.numTransfer != 1 || slot.targetItem != null || slot.TryAcceptOnce(1) != 1 || !ReferenceEquals(gun.parentInventory, counter))
                throw new InvalidOperationException("游戏拒绝上架整枪。");
            store.currentNegociatedItem = gun;
            if (!ReferenceEquals(store.currentClientInstance?.GetClientBlueprint(), client) || !ReferenceEquals(client.gunOrderData, order) ||
                client.isBusinessComplete || !ReferenceEquals(GunOrderData.ResolveOrderGun(), gun) || !order.IsAccepted() || !MatchesOrder(client, gun) ||
                !GraphUtils.FindAllChildrenType<GameItem>(gun).SequenceEqual(members)) throw new InvalidOperationException("游戏或扩展的最终交付检查未通过。");
        }, item =>
        {
            if (item.parentInventory != null && !item.parentInventory.Expel(item)) throw new InvalidOperationException("无法收回未完成的上架。");
        }, item =>
        {
            foreach (var snapshot in snapshots) snapshot.State.Restore(snapshot.Item);
            item.ValidateShapeState();
            item.modifiedShape = shape.Clone();
            if (!source.UncheckedAccept(item)) throw new InvalidOperationException("无法恢复库存枪的原位置。");
        }, () =>
        {
            store.currentNegociatedItem = selection;
            if (!ReferenceEquals(gun.parentInventory, source) || !GraphUtils.FindAllChildrenType<GameItem>(gun).SequenceEqual(members) ||
                !source.CheckInventorySlotStillValid(gun, gun.modifiedShape, gun.modifiedState))
                throw new InvalidOperationException("库存枪恢复检查失败。");
        });
        // Native OnWeightedAreaChanged clears the commission UI without rebuilding it.
        // Refresh after the atomic move/rollback, so a UI exception cannot undo a successful placement.
        try { store.OnWeightedAreaChanged(); NegociationUIManager.Instance?.UpdateUI(); }
        catch (Exception error) { ModEntry.Log?.Error(error); }
        if (result.Succeeded) return $"已整枪上架，满足全部必需条件，附加条件 {order.CountBonusMet(gun)}/{order.bonusRequirements.Count}；配件保持原样，请检查后手动交付。";
        if (result.Restored) return "上架未通过最终检查，已恢复库存原位：" + result.Error!.Message;
        ModEntry.Log?.Error(result.RestoreError!);
        return "库存枪恢复时遇到异常，请勿保存并重新载入存档：" + result.RestoreError!.Message;
    }
}
