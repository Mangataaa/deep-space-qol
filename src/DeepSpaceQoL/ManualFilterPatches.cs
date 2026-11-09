using System.Reflection;
using HarmonyLib;

namespace DeepSpaceQoL;

/// <summary>Scope only player input. Scripted placement, doctor overflow, loading and other mods stay native.</summary>
[HarmonyPatch]
internal static class ManualInputScope
{
    [ThreadStatic] private static int depth;
    internal static bool Active => depth > 0 && ModEntry.Enabled;
    internal static void Enter() => depth++;
    internal static void Exit() => depth = Math.Max(0, depth - 1);

    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(ItemMouseDragHandler), "Drag");
        yield return AccessTools.Method(typeof(ItemMouseDragHandler), "EndDrag");
        yield return AccessTools.Method(typeof(ItemMultiSelectHandler), "UpdateDropTarget");
        yield return AccessTools.Method(typeof(ItemMultiSelectHandler), "EndGroupDrag");
        yield return AccessTools.Method(typeof(ItemQuickTransferHandler), "OnEventRelease");
        yield return AccessTools.Method(typeof(ItemQuickTransferAltHandler), "OnEventRelease");
        yield return AccessTools.Method(typeof(ItemQuickTransferAltHandler1), "OnEventRelease");
    }
    private static void Prefix(out bool __state) { __state = ModEntry.Enabled; if (__state) Enter(); }
    private static Exception? Finalizer(Exception? __exception, bool __state)
    { if (__state) Exit(); return __exception; }
}

[HarmonyPatch(typeof(ItemContextHandler), "TriggerButton")]
internal static class ManualUnloadScope
{
    private static void Prefix(RichTextElement __0, RichTextElement ___buttonUnload, out bool __state)
    {
        // Activate/Use can produce new bottles or force story placement. Only Unload is a manual insertion.
        __state = ModEntry.Enabled && ___buttonUnload != null && ReferenceEquals(__0, ___buttonUnload);
        if (__state) ManualInputScope.Enter();
    }
    private static Exception? Finalizer(Exception? __exception, bool __state)
    { if (__state) ManualInputScope.Exit(); return __exception; }
}

[HarmonyPatch(typeof(GameGridInventory), nameof(GameGridInventory.MayHaveValidInventorySlot))]
internal static class ManualInventoryFilter
{
    private static bool Prefix(GameGridInventory __instance, GameItem item, ref bool __result)
    {
        if (!ManualInputScope.Active || item?.parentInventory == null || StorageRules.Allows(__instance, item)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(SlotMarker), nameof(SlotMarker.TryAcceptOnce))]
internal static class ManualStackFilter
{
    private static bool Prefix(SlotMarker __instance, ref int __result)
    {
        var inventory = __instance.inventory ?? __instance.targetItem?.parentInventory;
        if (!ManualInputScope.Active || inventory == null || __instance.item?.parentInventory == null || StorageRules.Allows(inventory, __instance.item)) return true;
        __result = 0;
        return false;
    }
}

[HarmonyPatch(typeof(StorageExpansionHelper), nameof(StorageExpansionHelper.InitTabNames))]
internal static class CaptionPatch
{
    private static void Postfix(GameItem bay)
    { if (ModEntry.Enabled && bay != null) StorageRules.RefreshCaptions(bay); }
}

[HarmonyPatch(typeof(StorageExpansionHelper), nameof(StorageExpansionHelper.SetTabName))]
internal static class StorageNamePatch
{
    private static void Postfix(GameItem bay, int index, string name)
    {
        if (!ModEntry.Enabled || bay == null || StorageCatalog.TabsOf(bay) == null) return;
        try
        {
            var policy = StorageRules.Policy(bay);
            if (!policy.IsValid()) return;
            // Synchronize legacy captions so clearing a native name cannot resurrect an old one.
            policy.Page(index).Label = name ?? "";
            StorageRules.Save(bay, policy);
        }
        catch (Exception ex) { ModEntry.Log?.Error(ex); }
    }
}
