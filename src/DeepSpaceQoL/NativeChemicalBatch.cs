using System.Globalization;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DeepSpaceQoL;

/// <summary>A native machine control plus a compact preview/target picker. No automatic sale or save.</summary>
internal static class NativeChemicalBatch
{
    private const string WindowId = ModEntry.Id + ".chem-batch";
    private const string TargetsId = WindowId + ".targets";
    private static readonly Dictionary<PixelWindow, Binding> Windows = new();
    private static CustomUIWindow? window, targetsWindow;
    private static GameItem? currentMachine;

    internal static void Refresh()
    {
        if (!ModEntry.Enabled) { Dispose(); return; }
        var visible = new Dictionary<PixelWindow, GameItem>();
        var windows = WindowsHandler.current?.visibleWindows;
        if (windows != null)
            foreach (var w in windows.ToArray())
            {
                if (w?.handler == null || !w.IsVisible() || !w.handler.gameObject.activeInHierarchy) continue;
                var owners = w.parentItems;
                if (owners == null || owners.Count != 1) continue;
                var machine = owners[0];
                if (ReferenceEquals(machine?.contentWindow, w) && ChemicalBatchService.TryParts(machine, out _, out _, out _) &&
                    GeneralHelper.IsItemEffectivelyOwned(machine)) visible[w] = machine;
            }
        foreach (var pair in Windows.ToArray())
            if (!visible.TryGetValue(pair.Key, out var machine) || !ReferenceEquals(pair.Value.Machine, machine))
            { pair.Value.Dispose(); Windows.Remove(pair.Key); }
        foreach (var pair in visible)
        {
            if (!Windows.TryGetValue(pair.Key, out var binding))
            { binding = new Binding(pair.Value); Windows.Add(pair.Key, binding); }
            binding.Refresh();
        }
    }

    internal static void UpdateStatus(string status)
    {
        if (window?.IsAlive == true) window.Get("status")?.SetText(status);
    }

    internal static void Close()
    {
        targetsWindow?.Close(); targetsWindow = null;
        window?.Close(); window = null;
        currentMachine = null;
    }

    internal static void Dispose()
    {
        Close();
        foreach (var binding in Windows.Values) binding.Dispose();
        Windows.Clear();
    }

    private static GameGridInventory? ResolveTarget(GameItem machine)
    {
        if (!machine.IsTag(ChemicalBatchService.TargetOwnerTag) ||
            machine.GetTagReadonly(ChemicalBatchService.TargetOwnerTag).GetInt() < 0)
            return ChemicalBatchService.TryParts(machine, out _, out var output, out _) ? output : null;
        int owner = machine.GetTagReadonly(ChemicalBatchService.TargetOwnerTag).GetInt();
        int index = machine.IsTag(ChemicalBatchService.TargetPageTag) ? machine.GetTagReadonly(ChemicalBatchService.TargetPageTag).GetInt() : -1;
        return StorageCatalog.GetStoragePages().FirstOrDefault(p => p.Owner?.uniqueId == owner && p.Index == index)?.Inventory;
    }

    private static string TargetName(GameItem machine, GameGridInventory? target)
    {
        if (target == null) return "绑定页已不存在，请重新选择";
        ChemicalBatchService.TryParts(machine, out _, out var output, out _);
        if (ReferenceEquals(output, target)) return "当前机器成品区";
        var page = StorageCatalog.Find(target);
        return page?.Name ?? "输出页已变化";
    }

    private static void Open(GameItem machine)
    {
        if (!ModEntry.Ready(out string reason, true) || !ChemicalBatchService.CanUseMachine(machine, out reason))
        { ControlPanel.Status(reason); return; }
        if (CustomUIManager.Instance == null) return;
        Close();
        currentMachine = machine;
        var b = CustomUIManager.Instance.CreateWindow(WindowId, "批量制药", CustomUIManager.LAYER_OVERLAY,
            style: CustomUIManager.Style.Machinery);
        window = b.Handle;
        float height = 1920f * Screen.height / Math.Max(1, Screen.width);
        b.SetSize(650, Math.Min(610, Math.Max(320, height - 60))).Center().SetDraggable(true).SetCloseOnEscape();
        b.BeginScrollFlexible();
        var recipe = ChemicalBatchService.SelectedRecipe(machine);
        ChemicalBatchService.TryParts(machine, out var input, out _, out _);
        var source = input!.childItem;
        int total = source == null ? 0 : WaterHelper.GetTotalVolume(source);
        int maximum = ChemicalBatchService.AvailableCount(source, recipe);
        b.AddHeader(recipe == null ? "未选择配方" : recipe.displayName);
        b.AddLabel(recipe == null ? "请先在机器原生“配置”中选择配方。" :
            $"输入：{source?.GetDisplayName(true) ?? "无容器"}\n总含量 {WaterScannerUIManager.FormatAmount(total)}；每份 {WaterScannerUIManager.FormatAmount(recipe.requiredVolume)}；原料可制 {maximum} 份（无固定批次数量上限）。");
        if (source != null && recipe != null && total > 0)
        {
            int active = source.GetTagReadonly(Liquid.GetCurrentPartFromId(recipe.activeIngredientId)).GetInt();
            double purity = (double)active / total * (100 - MachineChemFinisher.GetLoss(machine));
            b.AddLabel($"预计成品纯度约 {purity:F2}%（包含机器 {MachineChemFinisher.GetLoss(machine)}% 的物理污染；逐份按原生规则评级）。");
            if (recipe.isRequireDry && WaterHelper.IsWet(source))
                b.AddLabel("原料尚未干燥，不能生成：请先完成原生脱水／干燥步骤。");
        }
        var selected = ResolveTarget(machine);
        b.AddHeader("生成位置");
        b.AddButton(TargetName(machine, selected) + "  ▼", () => OpenTargets(machine));
        b.Last?.SetPreferredSize(0, 42).SetTooltip(TargetName(machine, selected));
        if (selected != null && !ChemicalBatchService.CanUseTarget(machine, selected))
            b.AddLabel("绑定输出页原生封锁或不可访问，请重新选择。");
        if (selected != null)
        {
            ChemicalBatchService.TryParts(machine, out _, out var machineOutput, out _);
            if (ReferenceEquals(selected, machineOutput))
                b.AddLabel("当前使用机器自带成品区，空间有限。想一次生成更多，请在上方改选一个有空位的储物页；不会自动转存到其他页。");
            else if (StorageRules.IsLocked(selected))
                b.AddLabel("已明确选择受保护储物页：本次制药允许向此页放入成品；原有箱锁／页锁仍保护自动收纳和上货。");
        }
        string amount = Math.Max(1, maximum).ToString(CultureInfo.InvariantCulture);
        b.AddInput("生成数量（默认按含量全部生成）", amount, text => amount = text, "amount", text => amount = text);
        if (b.Last?.input != null) { b.Last.input.characterLimit = 10; b.Last.input.contentType = TMPro.TMP_InputField.ContentType.IntegerNumber; }
        b.AddButton("按当前全部含量填写数量", () =>
        {
            if (!ReferenceEquals(currentMachine, machine) || ChemicalBatchService.IsRunning ||
                !ChemicalBatchService.TryParts(machine, out var latestInput, out _, out _)) return;
            int available = ChemicalBatchService.AvailableCount(latestInput!.childItem, ChemicalBatchService.SelectedRecipe(machine));
            amount = available.ToString(CultureInfo.InvariantCulture);
            window?.Get("amount")?.SetString(amount);
            UpdateStatus($"已按当前原料填写 {available} 份；输出空间不足时会停，尚未消耗原料。");
        });
        b.AddLabel("一份一帧；仅放入明确选中的页，满了就停。可主动选用 QoL 锁定页；分类、原生物理锁和剧情保护继续生效，不会把药塞进页内机器或箱子。");
        b.AddLabel(ChemicalBatchService.IsRunning ? ChemicalBatchService.CurrentStatus : "确认配方、数量和输出页后开始。", "status");
        b.AddButton("开始批量生成", () => ModEntry.OpenManagement(() =>
        {
            if (!ReferenceEquals(currentMachine, machine)) return "操作窗口已变化，请重新打开。";
            if (!int.TryParse(amount?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count <= 0)
            { const string invalid = "生成数量必须是大于 0 的整数。"; UpdateStatus(invalid); return invalid; }
            var target = ResolveTarget(machine);
            string message = target == null ? "绑定输出页已不存在，请重新选择。" : ChemicalBatchService.Start(machine, target, count);
            UpdateStatus(message);
            return message;
        }));
        b.AddButton("停止批量生成", () =>
        {
            ChemicalBatchService.Cancel();
            UpdateStatus(ChemicalBatchService.CurrentStatus);
            NativeFeedback.Show(ChemicalBatchService.CurrentStatus);
        });
        b.AddButton("关闭（进行中的任务继续；Esc 可停止）", Close);
        b.End().Show();
    }

    private static void OpenTargets(GameItem machine)
    {
        if (!ReferenceEquals(currentMachine, machine) || CustomUIManager.Instance == null) return;
        targetsWindow?.Close();
        var b = CustomUIManager.Instance.CreateWindow(TargetsId, "选择出药位置", CustomUIManager.LAYER_OVERLAY,
            style: CustomUIManager.Style.Machinery);
        targetsWindow = b.Handle;
        float height = 1920f * Screen.height / Math.Max(1, Screen.width);
        b.SetSize(660, Math.Min(620, Math.Max(320, height - 80))).Center().SetDraggable(true).SetCloseOnEscape();
        b.AddLabel("选择后与这台机器一起随原生存档保存。允许主动选择 QoL 锁定箱／页；原生物理锁、剧情箱和顾客容器不列入。名称中包括箱名和页名。");
        b.BeginScrollFlexible();
        b.AddButton("当前机器成品区", () => SelectTarget(machine, null));
        foreach (var page in StorageCatalog.GetStoragePages().Where(p => ChemicalBatchService.CanUseTarget(machine, p.Inventory)))
        {
            var chosen = page;
            b.AddButton((StorageRules.IsLocked(page.Inventory) ? "[保护锁·允许指定] " : "") + page.Name, () => SelectTarget(machine, chosen));
            b.Last?.SetPreferredSize(0, 42).SetTooltip(page.Name);
        }
        b.End().AddButton("取消", () => { targetsWindow?.Close(); targetsWindow = null; }).Show();
    }

    private static void SelectTarget(GameItem machine, StoragePage? page)
    {
        if (!ReferenceEquals(currentMachine, machine) || !ChemicalBatchService.CanUseMachine(machine, out _) ||
            ChemicalBatchService.IsRunning || page != null && !ChemicalBatchService.CanUseTarget(machine, page.Inventory))
        { NativeFeedback.Show("目标已变化或批量任务仍在运行，请停止任务后重试。"); return; }
        machine.ModifyTag(ChemicalBatchService.TargetOwnerTag, tag => tag.Enable().SetInt(page?.Owner?.uniqueId ?? -1));
        machine.ModifyTag(ChemicalBatchService.TargetPageTag, tag => tag.Enable().SetInt(page?.Index ?? -1));
        Open(machine);
    }

    private sealed class Binding
    {
        internal readonly GameItem Machine;
        private readonly MachineryStack controls;
        private readonly MachineryButton button;
        private string lastText = "";
        internal Binding(GameItem machine)
        {
            Machine = machine;
            ChemicalBatchService.TryParts(machine, out _, out _, out var stack);
            controls = stack!;
            button = new MachineryButton("批量生成", () => Open(machine))
                .SetDescription("按输入容器含量批量生成原生药品，可绑定一个储物页作为输出；扣料、机器污染、干燥和评级照原生规则。");
            controls.Add(button);
            machine.contentWindow.Validate();
        }
        internal void Refresh()
        {
            string text = ChemicalBatchService.IsRunning ? "批量生成（进行中）" : "批量生成";
            if (text == lastText) return;
            lastText = text;
            button.SetText(text);
        }
        internal void Dispose()
        {
            if (button.handler == null) return;
            if (controls.handler != null) controls.Detach(button);
            button.handler.gameObject.SetActive(false);
            Object.Destroy(button.handler.gameObject);
            if (Machine.contentWindow?.handler != null) Machine.contentWindow.Validate();
        }
    }
}
