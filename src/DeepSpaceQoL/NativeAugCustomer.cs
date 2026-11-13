using HarmonyLib;
using TMPro;

namespace DeepSpaceQoL;

/// <summary>Presentation-only hints: never change the customer's identity or scan/story flags.</summary>
internal static class NativeAugCustomer
{
    internal const string Suffix = "  <color=#D65A45>【改造体】</color>";
    private static readonly Dictionary<TextMeshProUGUI, (string Original, string Applied)> Labels = new();

    internal static bool ShouldMark(StoreClient? client) => client?.isAug == true;

    internal static string FormatName(string name, StoreClient? client) =>
        ShouldMark(client) && !name.EndsWith(Suffix, StringComparison.Ordinal) ? name + Suffix : name;

    public static void Refresh()
    {
        if (!ModEntry.Enabled || EmporiumEntry.Instance?.invElement == null || !PlayerStore.HasInstance)
        {
            Dispose();
            return;
        }
        var client = PlayerStore.Instance?.currentClientInstance?.GetClientBlueprint();
        // The game rebuilds its negotiation header on demand; the postfix below updates it immediately.
        // This also covers enabling the mod while a dialogue or tablet is already visible.
        var manager = NegociationUIManager.Instance;
        if (manager != null && manager.IsOpen && manager.uiObject != null)
        {
            var frame = manager.uiObject.GetComponentInChildren<NegotiationTabletFrame>(true);
            if (frame != null) Apply(frame.clientName, client);
        }
        var dialogue = DialogUIManager.Instance;
        if (dialogue != null && dialogue.clientNameBubble != null && dialogue.clientNameBubble.activeInHierarchy)
            Apply(dialogue.clientNameTMP, client);
        foreach (var label in Labels.Keys.Where(x => x == null).ToArray()) Labels.Remove(label);
    }

    private static void Apply(TextMeshProUGUI? label, StoreClient? client)
    {
        if (label == null) return;
        string text = label.text;
        if (Labels.TryGetValue(label, out var previous) && text == previous.Applied)
            text = previous.Original;
        if (!ModEntry.Enabled || !ShouldMark(client))
        {
            Restore(label);
            return;
        }
        // Only annotate the real current customer's name. Leave other speakers and other mods' text alone.
        if (string.IsNullOrEmpty(text) || text != client!.displayName)
        {
            Restore(label);
            return;
        }
        string decorated = FormatName(text, client);
        Labels[label] = (text, decorated);
        if (label.text != decorated) label.text = decorated;
    }

    private static void Restore(TextMeshProUGUI label)
    {
        if (!Labels.TryGetValue(label, out var previous)) return;
        if (label != null && label.text == previous.Applied) label.text = previous.Original;
        Labels.Remove(label!);
    }

    public static void Dispose()
    {
        foreach (var label in Labels.Keys.ToArray()) Restore(label);
        Labels.Clear();
    }

    [HarmonyPatch(typeof(NegociationUIManager), "RefreshClientHeader")]
    private static class NegotiationHeader
    {
        private static void Postfix(StoreClient storeClient, NegotiationTabletFrame ___frame)
        {
            if (ModEntry.Enabled && ___frame != null) Apply(___frame.clientName, storeClient);
        }
    }

    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.DisplayClientText))]
    private static class DialogueName
    {
        private static void Postfix(DialogUIManager __instance)
        {
            if (ModEntry.Enabled) Apply(__instance.clientNameTMP, PlayerStore.Instance?.currentClientInstance?.GetClientBlueprint());
        }
    }

    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.PlayClientSingleText))]
    private static class SingleLineName
    {
        private static void Postfix(DialogUIManager __instance)
        {
            if (ModEntry.Enabled) Apply(__instance.clientNameTMP, PlayerStore.Instance?.currentClientInstance?.GetClientBlueprint());
        }
    }
}
