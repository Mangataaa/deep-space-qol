namespace DeepSpaceQoL;

/// <summary>The native proportional pour with an explicit microlitre quota. Caller owns permission and rollback.</summary>
internal static class ChemicalDoseTransfer
{
    internal static void Move(GameItem source, GameItem target, int volume, Action<GameItem, GameItem, bool> changed)
    {
        if (ReferenceEquals(source, target) || !source.IsTag(WaterHelper.LIQUID_CONTAINER_TAG) ||
            !target.IsTag(WaterHelper.LIQUID_CONTAINER_TAG) || target.IsTag(WaterHelper.FILTER_CONTAINER_TAG))
            throw new InvalidOperationException("配比只允许普通容器间精确加药；过滤必须单独进行。");
        int[] beforeSource = WaterHelper.GetLiquidParts(source), beforeTarget = WaterHelper.GetLiquidParts(target);
        if (!ChemicalMixModel.Valid(beforeSource) || !ChemicalMixModel.Valid(beforeTarget) || volume <= 0 ||
            volume > ChemicalMixModel.Total(beforeSource) || volume > WaterHelper.GetFreeCapacity(target))
            throw new InvalidOperationException("药剂含量或目标剩余容量已经变化。");
        int[] portion = WaterHelper.SplitProportionally(beforeSource, volume);
        for (int i = 0; i < portion.Length; i++)
        {
            string tag = Liquid.Liquids[i].currentPart;
            int left = beforeSource[i] - portion[i], into = beforeTarget[i] + portion[i];
            ChemicalHelper.HandleReceiveLiquidTemp(target, portion[i], source);
            target.ModifyTag(tag, s => s.Enable().SetInt(into), skipValidate: true);
            source.ModifyTag(tag, s => s.Enable().SetInt(left), skipValidate: true);
        }
        source.SyncModifiedState(); target.SyncModifiedState();
        changed(target, source, ChemicalMixModel.Total(beforeTarget) == 0);
        source.SyncModifiedState(); target.SyncModifiedState();
        if (!WaterHelper.GetLiquidParts(source).SequenceEqual(beforeSource.Select((p, i) => p - portion[i])) ||
            !WaterHelper.GetLiquidParts(target).SequenceEqual(beforeTarget.Select((p, i) => p + portion[i])))
            throw new InvalidOperationException("液体回调改变了本次配比。");
    }
}
