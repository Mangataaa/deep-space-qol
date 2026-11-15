using Object = UnityEngine.Object;

namespace DeepSpaceQoL;

/// <summary>Add an owned fourth control only while a real native printer window is visible.</summary>
internal static class NativeWaterJugPrinter
{
    private static readonly Dictionary<PixelWindow, Binding> Windows = new();

    internal static void Refresh()
    {
        if (!ModEntry.Enabled) { Dispose(); return; }
        var windows = WindowsHandler.current?.visibleWindows;
        var visible = new Dictionary<PixelWindow, GameItem>();
        if (windows != null)
            foreach (var window in windows.ToArray())
            {
                if (window?.handler == null || !window.IsVisible() || !window.handler.gameObject.activeInHierarchy) continue;
                var owners = window.parentItems;
                if (owners == null || owners.Count != 1) continue;
                var printer = owners[0];
                if (printer == null || !ReferenceEquals(printer.contentWindow, window) ||
                    !WaterJugPrinting.TryParts(printer, out _, out _, out _) ||
                    !GeneralHelper.IsItemEffectivelyOwned(printer)) continue;
                visible[window] = printer;
            }
        foreach (var pair in Windows.ToArray())
            if (!visible.TryGetValue(pair.Key, out var printer) || !ReferenceEquals(pair.Value.Printer, printer))
            { pair.Value.Dispose(); Windows.Remove(pair.Key); }
        foreach (var pair in visible)
        {
            if (!Windows.TryGetValue(pair.Key, out var binding))
            { binding = new Binding(pair.Value); Windows.Add(pair.Key, binding); }
            binding.Refresh();
        }
    }

    internal static void Dispose()
    {
        foreach (var binding in Windows.Values) binding.Dispose();
        Windows.Clear();
    }

    private sealed class Binding
    {
        internal readonly GameItem Printer;
        private readonly MachineryStack stack;
        private readonly MachineryButton button;
        private bool? enabled;

        internal Binding(GameItem printer)
        {
            Printer = printer;
            if (!WaterJugPrinting.TryParts(printer, out _, out _, out var controls))
                throw new InvalidOperationException("水瓶打印机界面已变化。");
            stack = controls!;
            button = new MachineryButton("打印水壶（6L）", () => ModEntry.Execute(() =>
            {
                if (Printer.contentWindow?.handler == null || !Printer.contentWindow.IsVisible()) return "打印机窗口已关闭。";
                string result = WaterJugPrinting.Print(Printer);
                Refresh();
                return result;
            })).SetDescription("深空管家：打印原生 6L 空水壶，耗电 " + WaterJugPrinting.PowerCost +
                "。需先解锁大瓶；使用原成品区，放不下时不扣电。");
            button.handler.gameObject.name = "DeepSpaceQoL print water jug";
            stack.Add(button);
            printer.contentWindow!.Validate();
        }

        internal void Refresh()
        {
            bool value = ModEntry.Enabled && MachineBottlePrinter.IsLargeUnlocked(Printer);
            if (value == enabled) return;
            enabled = value;
            if (value) button.Enable(); else button.Disable();
        }

        internal void Dispose()
        {
            if (button.handler == null) return;
            if (stack.handler != null) stack.Detach(button);
            button.handler.gameObject.SetActive(false);
            Object.Destroy(button.handler.gameObject);
            if (Printer.contentWindow?.handler != null) Printer.contentWindow.Validate();
        }
    }
}
