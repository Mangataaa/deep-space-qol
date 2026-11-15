using HarmonyLib;

namespace DeepSpaceQoL;

/// <summary>Bypass only the two requested nightly-service cooldown queries. Saved counters stay intact.</summary>
internal static class NativeServiceCooldowns
{
    internal static bool IsRequestedService(StoreService? service) => service?.id is
        StoreService.CRIMINEL_NETWORK or StoreService.HYPERTECH_SMUGGLER_SERVICE;

    internal static bool EffectiveCooldown(StoreService? service, bool nativeResult, bool enabled, bool removeCooldowns) =>
        nativeResult && !(enabled && removeCooldowns && IsRequestedService(service));

    [HarmonyPatch(typeof(StoreService), nameof(StoreService.IsOnCooldown))]
    private static class NightlyServiceCooldown
    {
        private static void Postfix(StoreService __instance, ref bool __result) =>
            __result = EffectiveCooldown(__instance, __result, ModEntry.Enabled, ModEntry.RemoveServiceCooldowns);
    }
}
