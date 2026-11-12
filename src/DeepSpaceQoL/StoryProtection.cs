namespace DeepSpaceQoL;

/// <summary>Never depend on a user sale lock for native story-critical belongings.</summary>
internal static class StoryProtection
{
    internal static bool IsLouProperty(GameItem item) => item != null &&
        (item.IsTag("LOU_TOY_TAG") || item.IsTag("LOU_TOY_BOX_TAG") || item.IsTag("LOU_LOCKER_TAG") ||
         item.identifier == "lou_locker" || item.identifier == "lou_old_orders");

    internal static bool IsProtected(GameItem item) => item != null &&
        (item.IsTag("IMPORTANT_TAG") || IsLouProperty(item));

    // An IMPORTANT non-saleable shell must not disable in-place filling of the
    // audited bottle-printer output. Lou's explicit
    // story flags always win, even on an unexpected printer-shaped container.
    internal static bool ProtectsContents(GameItem item) => IsLouProperty(item) ||
        (IsProtected(item) && item.identifier != "bottle_printer");

    internal static bool ContainsProtected(GameItem item) => IsProtected(item) ||
        GraphUtils.FindAllChildrenType<GameItem>(item).Any(IsProtected);
}
