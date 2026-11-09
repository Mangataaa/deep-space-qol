namespace DeepSpaceQoL;

/// <summary>Permissions to mutate liquid in place. Pinning/removal permissions concern movement.</summary>
internal static class BottlingAccess
{
    private static readonly HashSet<string> BottleIds = new(StringComparer.Ordinal)
    { "mini_bottle", "small_bottled_water", "bottled_water", "large_bottled_water", "water_jug" };

    internal static bool IsBottle(GameItem? item) => item != null && item.unitCount == 1 &&
        BottleIds.Contains(item.identifier) && item.IsTag(WaterHelper.LIQUID_CONTAINER_TAG) &&
        item.IsTag(WaterHelper.LIQUID_CONTAINER_CAPACITY) && item.IsTag("CONTAINER_TEMP_TAG") &&
        !item.IsTag("WINE_CONTAINER_TAG") && !item.IsTag(WaterHelper.FILTER_CONTAINER_TAG) &&
        !item.IsTag(WaterHelper.LIQUID_CONTAINER_DROPPER_TAG);

    internal static bool CanAccess(GameItem? item, out string reason)
    {
        reason = "";
        if (item == null || item.parentInventory == null || item.unitCount != 1 ||
            item is GameItemElement element && element.IsDestroyed())
        { reason = "物品已不存在或不是可操作的单件容器。"; return false; }
        if (!GeneralHelper.IsItemEffectivelyOwned(item))
        { reason = "只能为自有物品装瓶，不能改动顾客商品或非自有设备内的瓶子。"; return false; }
        if (!StorageRules.CanAutoAccess(item.parentInventory) ||
            StorageRules.Policy(item).IsSaleProtected())
        { reason = "水源或瓶子位于锁定的储物页、箱子或原生封锁区域。"; return false; }
        if (GeneralHelper.IsItemLocked(item) || LockHelper.IsCurrentlyLocked(item) || item.IsTag("ITEM_BEHIND_LOCK_TAG") ||
            GraphUtils.FindParentsType<GameItem>(item.parentInventory, passthrough: _ => true)
                .Any(owner => GeneralHelper.IsItemLocked(owner) || owner.IsTag("ITEM_BEHIND_LOCK_TAG") ||
                    owner is GameItemElement parentElement && parentElement.IsDestroyed()))
        { reason = "物品处于原生封锁状态。"; return false; }
        if (BottlingCatalog.IsOnTradeCounter(item) && NegociationUIManager.IsTradeLocked())
        { reason = "交易暂时锁定；为保护台上报价，未改动交易台上的液体。"; return false; }
        if (!BottlingCatalog.Contains(item))
        { reason = "只可操作店内库存、储物页、交易台自有瓶和水瓶打印机成品区。"; return false; }
        return true;
    }

    internal static bool CanUseSource(GameItem? source, out string reason)
    {
        if (source == null || !BottlingService.IsSource(source))
        { reason = "请选择储水箱、水壶或大水桶作为水源。"; return false; }
        if (!CanAccess(source, out reason)) return false;
        // These are native MayTarget source gates, independent of movement and pinning.
        if (source.forceDisableUse || !source.canUseOutsideCombat || !WaterHelper.CanUseWaterContainer(source))
        { reason = "水源当前不允许原生液体交互。"; return false; }
        return true;
    }

    internal static bool CanUseTarget(GameItem source, GameItem? target, out string reason)
    {
        if (target == null || ReferenceEquals(source, target) || !IsBottle(target) ||
            WaterHelper.GetTotalCapacity(target) >= WaterHelper.GetTotalCapacity(source))
        { reason = "目标不是容量更小的原生水瓶。"; return false; }
        if (!CanAccess(target, out reason)) return false;
        if (!WaterHelper.CanUseWaterContainer(target))
        { reason = "目标当前不允许原生液体交互。"; return false; }
        return true;
    }

    internal static bool CanTransfer(GameItem source, GameItem target, out string reason)
    {
        if (!CanUseSource(source, out reason) || !CanUseTarget(source, target, out reason)) return false;
        // Calls the actual game predicates, including per-item/per-inventory veto delegates.
        // Neither predicate checks PINNED_TAG or MayRemove; pinned bottles stay in their slots.
        if (!source.MayTarget(target) || !source.CanTarget(target))
        { reason = "原生液体交互规则拒绝此水源或瓶子。"; return false; }
        return true;
    }

    internal static void RefreshTradeQuote(GameItem source, GameItem target)
    {
        if (!BottlingCatalog.IsOnTradeCounter(source) && !BottlingCatalog.IsOnTradeCounter(target)) return;
        var ui = NegociationUIManager.Instance;
        if (ui == null) return;
        // The existing open path cancels stale over-budget confirmation without accepting a sale.
        // When already open it only focuses/queues a refresh; no matched sound or reopen is needed.
        if (ui.IsOpen) ui.OpenUI();
        ui.UpdateUI();
        // Never call PlayerStore.OnWeightedAreaChanged/OnAddedToWeightedArea here: these replay
        // customer placement, feature exposure, security and dialogue callbacks for unmoved goods.
    }
}
