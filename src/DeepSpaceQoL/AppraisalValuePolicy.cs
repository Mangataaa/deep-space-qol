namespace DeepSpaceQoL;

/// <summary>Reads the same registered valuation used by the game's appraisal submission.</summary>
internal static class AppraisalValuePolicy
{
    internal static bool TryGetPrice(GameItem? item, out int price)
    {
        price = 0;
        if (item == null || !item.IsTag("APPRAISAL_ITEM")) return false;
        // Avoid the native unknown-type diagnostic path, and allow Mod API registered types.
        var type = AppraisalItemRegistry.Find(item);
        if (type?.getCorrectValue == null) return false;
        int value = AppraisalHelper.GetCorrectBaseValue(item);
        // Zero means no supported valuation in the native contract. Never fill an invented price.
        if (value <= 0) return false;
        price = value;
        return true;
    }
}
