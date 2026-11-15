namespace DeepSpaceQoL;

/// <summary>One native empty jug, paid only after a complete placement in the printer output.</summary>
internal static class WaterJugPrinting
{
    internal const string PrinterId = "bottle_printer";
    internal const string JugId = "water_jug";
    internal const int PowerCost = MachineBottlePrinter.LARGE_PRINT_COST * 3;
    internal const int CapacityParts = 6_000_000;

    internal enum Result { Printed, UpgradeRequired, NoPower, NoSpace, PlacementFailed, PowerFailed }

    // Native operations are passed as one same-frame transaction. This also allows failure
    // cases to be checked offline without creating Unity renderers or touching a save.
    internal static Result PrintOne(bool unlocked, Func<bool> canPower, Func<GameItem> create,
        Func<GameItem, bool> canFit, Func<GameItem, bool> place, Func<bool> drawPower, Action<GameItem> discard,
        Func<GameItem, bool> isEmpty)
    {
        if (!unlocked) return Result.UpgradeRequired;
        if (!canPower()) return Result.NoPower;
        var jug = create();
        bool keep = false;
        try
        {
            // Reject a changed/third-party factory instead of printing an unexpected or full item.
            if (!IsRecipeJug(jug) || !isEmpty(jug)) return Result.PlacementFailed;
            if (!canFit(jug)) return Result.NoSpace;
            if (!place(jug)) return Result.PlacementFailed;
            if (!drawPower()) return Result.PowerFailed;
            keep = true;
            return Result.Printed;
        }
        finally { if (!keep) discard(jug); }
    }

    internal static bool IsRecipeJug(GameItem? item) => item != null && item.identifier == JugId &&
        item.unitCount == 1 && item.IsTag(WaterHelper.LIQUID_CONTAINER_TAG) &&
        WaterHelper.GetTotalCapacity(item) == CapacityParts;

    internal static bool TryParts(GameItem? printer, out GameSlotInventory? power,
        out GameGridInventory? output, out MachineryStack? control)
    {
        power = null; output = null; control = null;
        if (printer == null || printer.identifier != PrinterId || printer is GameCharacterItem ||
            printer is GameItemElement element && element.IsDestroyed() ||
            printer.contentWindow?.childElement is not MachineryPanelRow row) return false;
        var input = row.GetPanel("power")?.content;
        power = (input as GameSlotInventory) ?? (input as MachineryIntake)?.content as GameSlotInventory;
        var result = row.GetPanel("output")?.content;
        output = (result as GameGridInventory) ?? (result as MachineryIntake)?.content as GameGridInventory;
        control = row.GetPanel("control")?.content as MachineryStack;
        return power != null && output != null && control != null &&
            ReferenceEquals(power.GetParentItem(), printer) && ReferenceEquals(output.GetParentItem(), printer);
    }

    internal static string Print(GameItem printer)
    {
        if (!ModEntry.Enabled) return "深空管家已停用。";
        if (!TryParts(printer, out var power, out var output, out _) ||
            !GeneralHelper.IsItemEffectivelyOwned(printer)) return "请打开自有水瓶打印机。";
        var source = power!.childItem;
        var result = PrintOne(MachineBottlePrinter.IsLargeUnlocked(printer),
            () => source != null && ReferenceEquals(power.childItem, source) && PowerHelper.CanDrawPowerSource(source, PowerCost),
            () => DirectoryMaster.Item(JugId), jug => GraphUtils.CanAccept(output!, jug),
            jug => GraphUtils.TryAcceptAll(output!, jug) == 0 &&
                ReferenceEquals(jug.parentInventory, output) && output!.childItems.Contains(jug),
            () => ReferenceEquals(power.childItem, source) && PowerHelper.DrawPowerSource(source!, PowerCost),
            jug => jug.Destroy(), WaterHelper.IsEmpty);
        return result switch
        {
            Result.Printed => "已打印 1 个 6L 空水壶，消耗 " + PowerCost + " 电量。可继续使用一键装瓶加水。",
            Result.UpgradeRequired => "请先将水瓶打印机升级至解锁大瓶（累计 9 次升级）。",
            Result.NoPower => "打印水壶需要 " + PowerCost + " 电量，请检查打印机电源。",
            Result.NoSpace => "成品区放不下完整水壶，请先腾出空间；未扣电。",
            Result.PowerFailed => "电源状态已改变，打印取消；未保留成品。",
            _ => "成品区未能接收完整水壶，打印取消；未扣电。"
        };
    }
}
