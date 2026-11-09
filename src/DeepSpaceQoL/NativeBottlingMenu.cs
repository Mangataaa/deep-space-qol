using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DeepSpaceQoL;

/// <summary>Adds one native item-menu row without replacing or cloning the vanilla buttons.</summary>
internal static class NativeBottlingMenu
{
    private const string Label = "一键装瓶";
    private const string Help = "从此水源原地装瓶，支持固定水罐、交易台自有水瓶及水瓶打印机成品瓶；锁定内容跳过，不同水质不混装。";
    private static readonly Dictionary<ItemContextHandler, MenuState> States = new();
    private static bool active;

    internal static void Init() => active = true;

    internal static void Dispose()
    {
        active = false;
        foreach (var pair in States.ToArray())
        {
            try
            {
                if (pair.Key != null && pair.Value.IsAttached) pair.Key.Stop();
            }
            catch (Exception error) { ModEntry.Log?.Error(error); }
            try { pair.Value.Release(); }
            catch (Exception error) { ModEntry.Log?.Error(error); }
        }
        States.Clear();
    }

    private static bool TryReady(GameItem? source, out string reason)
    {
        reason = "";
        if (!active || !ModEntry.Enabled) { reason = "深空管家已停用。"; return false; }
        if (!ModEntry.Ready(out reason)) return false;
        return BottlingService.CanUseSource(source, out reason);
    }

    private static void UpdateConditions(ItemContextHandler owner, GameItem? source, PixelWindow? window)
    {
        if (!active || !ModEntry.Enabled || source == null || !BottlingService.IsSource(source) || window?.handler == null)
        {
            if (States.TryGetValue(owner, out var existing)) existing.Restore();
            return;
        }

        MenuState? state = null;
        try
        {
            if (States.TryGetValue(owner, out state) && !ReferenceEquals(state.Window, window))
            {
                state.Release();
                States.Remove(owner);
                state = null;
            }
            if (state == null)
            {
                state = new MenuState(window);
                States.Add(owner, state);
            }
            state.Attach();
            state.IsEnabled = TryReady(source, out string reason);
            state.SetTooltip(state.IsEnabled ? Help : reason + "\n" + Help);
            state.Button.SetText(Label, 10000, state.IsEnabled
                ? RenderHandler.ColorPalette.LightGray : RenderHandler.ColorPalette.DarkGray);
        }
        catch (Exception error)
        {
            // Menu augmentation must never prevent the ordinary item actions from opening.
            try { state?.Restore(); } catch (Exception restoreError) { ModEntry.Log?.Error(restoreError); }
            ModEntry.Log?.Error(error);
        }
    }

    private sealed class MenuState
    {
        internal readonly PixelWindow Window;
        internal readonly RichTextElement Button;
        private readonly GridPixelElement wrapper;
        private readonly List<CustomUITooltipTrigger> tooltips = new();
        private PixelElement? original;
        internal bool IsEnabled;
        internal bool IsAttached => original != null && ReferenceEquals(Window.childElement, wrapper);

        internal MenuState(PixelWindow window)
        {
            Window = window;
            // The vanilla grid is readonly 1x8. A temporary outer row preserves its identity,
            // row order, spacing, button objects, field references and any existing extensions.
            wrapper = new GridPixelElement(1, 2);
            wrapper.handler.gameObject.name = "DeepSpaceQoL_BottlingContextRows";
            wrapper.handler.gameObject.SetActive(false);
            Button = new RichTextElement();
            Button.handler.gameObject.name = "DeepSpaceQoL_BottleWater";
            Button.SetText(Label);
            wrapper.AttachPos(Button, 0, 1);
            foreach (var transform in wrapper.handler.GetComponentsInChildren<Transform>(true))
                transform.gameObject.tag = "ContextMenuNode";
            var objects = new HashSet<GameObject>();
            foreach (var graphic in Button.handler.GetComponentsInChildren<Graphic>(true))
            {
                if (!graphic.raycastTarget || !objects.Add(graphic.gameObject)) continue;
                var tooltip = graphic.gameObject.AddComponent<CustomUITooltipTrigger>();
                tooltip.tooltipText = Help;
                tooltips.Add(tooltip);
            }
        }

        internal void SetTooltip(string text)
        {
            foreach (var tooltip in tooltips) if (tooltip != null) tooltip.tooltipText = text;
        }

        internal void Attach()
        {
            if (IsAttached) return;
            if (original != null)
                throw new InvalidOperationException("装瓶菜单的原生父级已被其他组件替换。");
            original = Window.childElement ?? throw new InvalidOperationException("物品菜单尚未初始化。");
            var native = original;
            if (!Window.Detach(native)) { original = null; throw new InvalidOperationException("无法暂时分离物品菜单。"); }
            try
            {
                if (!wrapper.AttachPos(native, 0, 0)) throw new InvalidOperationException("无法挂接原生物品菜单。");
                Window.Attach(wrapper);
                Button.handler.gameObject.SetActive(true);
                wrapper.handler.gameObject.SetActive(true);
                wrapper.Validate();
                Window.Validate();
            }
            catch
            {
                Restore();
                throw;
            }
        }

        internal bool Restore()
        {
            IsEnabled = false;
            if (Button.handler != null) Button.handler.gameObject.SetActive(false);
            if (original == null) return true;
            // Never overwrite a parent installed by another mod while this menu was open.
            if (Window.handler == null || Window.childElement != null && !ReferenceEquals(Window.childElement, wrapper)) return false;
            var native = original;
            if (ReferenceEquals(Window.childElement, wrapper)) Window.Detach(wrapper);
            if (ReferenceEquals(wrapper.GetElement(0, 0), native)) wrapper.Detach(native);
            Window.Attach(native);
            original = null;
            wrapper.handler.gameObject.SetActive(false);
            return true;
        }

        internal void Release()
        {
            // Do not destroy the wrapper if it still owns somebody else's native controls.
            if (!Restore()) return;
            if (wrapper.handler != null) UnityEngine.Object.Destroy(wrapper.handler.gameObject);
        }
    }

    [HarmonyPatch(typeof(ItemContextHandler), "UpdateConditions")]
    private static class ConditionsPatch
    {
        private static void Postfix(ItemContextHandler __instance, GameItem ___currentItem, PixelWindow ___contextWindow) =>
            UpdateConditions(__instance, ___currentItem, ___contextWindow);
    }

    [HarmonyPatch(typeof(ItemContextHandler), "UpdateSelectButtonColor")]
    private static class HoverPatch
    {
        private static void Postfix(ItemContextHandler __instance, RichTextElement __0, GameItem ___currentItem,
            RenderHandler.ColorPalette ___textColor, RenderHandler.ColorPalette ___textDisabledColor,
            RenderHandler.ColorPalette ___textSelectColor, RenderHandler.ColorPalette ___textSelectDisabledColor)
        {
            if (!States.TryGetValue(__instance, out var state) || !state.IsAttached || ___currentItem == null) return;
            bool enabled = active && ModEntry.Enabled && state.IsEnabled;
            bool selected = ReferenceEquals(__0, state.Button);
            var color = enabled ? (selected ? ___textSelectColor : ___textColor)
                : (selected ? ___textSelectDisabledColor : ___textDisabledColor);
            state.Button.SetText(Label, 10000, color);
        }
    }

    [HarmonyPatch(typeof(ItemContextHandler), "EqualizeButtonWidths")]
    private static class WidthPatch
    {
        private static void Postfix(ItemContextHandler __instance, RichTextElement ___buttonOpen, RichTextElement ___buttonUse,
            RichTextElement ___buttonEquip, RichTextElement ___buttonActivate, RichTextElement ___buttonConfigure,
            RichTextElement ___buttonToggle, RichTextElement ___buttonUnload, RichTextElement ___buttonPin)
        {
            if (!States.TryGetValue(__instance, out var state) || !state.IsAttached) return;
            var buttons = new[] { ___buttonOpen, ___buttonUse, ___buttonEquip, ___buttonActivate, ___buttonConfigure,
                ___buttonToggle, ___buttonUnload, ___buttonPin, state.Button }
                .Where(button => button?.handler != null && button.handler.gameObject.activeSelf).ToArray();
            state.Button.SetFixedSize(-1, -1);
            int width = buttons.Max(button => button.widthPixels);
            foreach (var button in buttons) button.SetFixedSize(width, -1);
        }
    }

    [HarmonyPatch(typeof(ItemContextHandler), "TriggerButton")]
    private static class TriggerPatch
    {
        private static bool Prefix(ItemContextHandler __instance, RichTextElement __0, GameItem ___currentItem,
            GameGridInventory ___currentInventory)
        {
            if (!States.TryGetValue(__instance, out var state) || !ReferenceEquals(__0, state.Button)) return true;
            if (!state.IsAttached || ___currentInventory != null || !active || !ModEntry.Enabled) return false;
            // Capture the native field at the moment of this click; no per-opening action closure.
            var source = ___currentItem;
            if (!TryReady(source, out string reason))
            {
                state.IsEnabled = false;
                state.SetTooltip(reason + "\n" + Help);
                ControlPanel.Status(reason);
                return false;
            }
            __instance.Stop();
            ModEntry.Execute(() => BottlingService.Run(source));
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemContextHandler), "OnEventReset")]
    private static class ResetPatch
    {
        private static void Postfix(ItemContextHandler __instance)
        {
            if (States.TryGetValue(__instance, out var state)) state.Restore();
        }
    }

    [HarmonyPatch(typeof(ItemContextHandler), "OnEventInit")]
    private static class ReinitializePatch
    {
        private static void Prefix(ItemContextHandler __instance)
        {
            if (!States.TryGetValue(__instance, out var state)) return;
            state.Release();
            States.Remove(__instance);
        }
    }
}
