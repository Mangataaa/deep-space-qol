namespace DeepSpaceQoL;

/// <summary>Optional rules audited against Gunsmith Expansion 2.1.1; no third-party binary dependency.</summary>
internal static class GunsmithExpansionCompatibility
{
    internal const string ModId = "gunsmith_expansion";
    internal const string SupportedVersion = "2.1.1";
    internal const string ConversionKit = "gsx_conversion_kit";

    // Indices address GunModdingService's native role/tag arrays, never the compacted slot array.
    // Expansion guns intentionally omit stock, magazine or rail panels.
    private static readonly Dictionary<string, int[]> Layouts = new(StringComparer.Ordinal)
    {
        ["gsx_pocket_pistol"] = new[] { 1, 3, 4, 5, 6, 7 },
        ["gsx_slug_gun"] = new[] { 1, 3, 4, 5, 6, 7 },
        ["gsx_service_10"] = new[] { 1, 3, 4, 5, 6, 7 },
        ["gsx_compact_smg"] = new[] { 1, 3, 4, 5, 6, 7 },
        ["gsx_hand_cannon"] = new[] { 1, 3, 4, 5, 6, 7 },
        ["gsx_carbine"] = new[] { 2, 3, 4, 5, 6, 7 },
        ["gsx_malorian"] = new[] { 1, 3, 4, 5, 6, 7 },
        ["gsx_gilded_pistol"] = new[] { 1, 3, 4, 5, 6, 7 },
        ["gsx_snub_revolver"] = new[] { 1, 3, 4, 5, 6, 7 },
        ["gsx_fries"] = new[] { 1, 2, 3, 4, 5, 6, 7 },
        ["gsx_five_seven"] = new[] { 1, 2, 3, 4, 5, 6, 7 },
        ["gsx_ll_pistol"] = new[] { 1, 3, 4, 5, 6, 7 },
        ["gsx_riot_gun"] = new[] { 1, 3, 4, 5, 6, 7 }
    };
    private static readonly HashSet<string> SlideGuns = new(StringComparer.Ordinal)
    {
        "printed_gun", "handmade_pistol", "heavy_handmade_pistol", "glock_receiver", "gsx_service_10",
        "gsx_malorian", "gsx_ll_pistol", "gsx_fries", "gsx_compact_smg", "gsx_five_seven"
    };
    private static readonly HashSet<string> VanillaGuns = new(StringComparer.Ordinal)
    { "printed_gun", "handmade_pistol", "heavy_handmade_pistol", "glock_receiver", "smg", "shotgun", "revolver", "stun_gun" };

    internal static bool IsActive => ModLoader.Mods.Any(m => m.IsEnabled &&
        m.Manifest?.ID == ModId && m.Manifest.ModVersion == SupportedVersion);

    internal static int[]? SlotKinds(string identifier) => identifier != null && Layouts.TryGetValue(identifier, out var kinds)
        ? (int[])kinds.Clone() : null;

    internal static string[] RequiredParts(string gun) => gun == "gsx_ll_pistol" || gun == "gsx_riot_gun"
        ? new[] { ConversionKit } : Array.Empty<string>();

    internal static bool AllowsPart(string gun, int kind, GameItem part)
    {
        if (kind == 4 && part.IsTag("MOD_SUPPRESSOR_TAG") &&
            (gun == "gsx_hand_cannon" || gun == "gsx_gilded_pistol" || gun == "gsx_snub_revolver")) return false;
        if (kind == 3 && (gun == "gsx_fries" || gun == "gsx_malorian" || gun == "gsx_compact_smg")) return false;
        // ReservedMods.ReturnUnfit removes these after loading, even if a native predicate accepts them.
        if (gun == "gsx_carbine" && part.identifier == "gl") return false;
        if (part.identifier == "gsx_polished_slide" && !SlideGuns.Contains(gun) &&
            (VanillaGuns.Contains(gun) || gun.StartsWith("gsx_", StringComparison.Ordinal))) return false;
        return true;
    }
}
