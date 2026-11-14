using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DeepSpaceQoL;

public sealed class ModEntry : IMod
{
    public const string Id = "deep_space_qol";
    internal static ModLog? Log;
    internal static bool Enabled;
    private static bool busy;
    private static ConfigEntry<KeyCode>? panelKey, stockKey, stowKey;
    private static ConfigEntry<bool>? expectedVisitors;
    internal static bool ExpectedVisitorsEnabled => expectedVisitors?.Value ?? false;
    internal static void SetExpectedVisitors(bool value) => expectedVisitors?.Set(value);

    public void Init(ModManifest manifest)
    {
        Log = new ModLog(manifest);
        var config = ModHelper.GetConfig(Id);
        panelKey = config.DefineEnum("panel_key", KeyCode.F6, "操作", () => "控制面板快捷键");
        stockKey = config.DefineEnum("stock_key", KeyCode.None, "操作", () => "一键上货快捷键（默认未绑定）");
        stowKey = config.DefineEnum("stow_key", KeyCode.None, "操作", () => "一键收纳快捷键（默认未绑定）");
        expectedVisitors = config.Define("daily_expected_visitors", false, "顾客", () => "每日逐项判定预期普通顾客",
            () => "按各来源原概率分别判定，突破原普通顾客配额；不足原名额则随机补足。仅影响下一次每日排程，不重抽今天，不改变剧情来客。");
        Log.Log("深空管家 0.5.0：可选预期普通顾客逐项判定、委托枪械一键改装、悬挂避让日历。保留卢剧情保护与储物锁定。");
    }

    public void OnEnable()
    {
        Enabled = true;
        NativeBottlingMenu.Init();
        ModHook.OnGameLoadedLate += OnLoaded;
        ModHelper.AddGameOptionSection(Id, "qol", () => "深空管家");
        AddBind("panel", "打开深空管家", panelKey!);
        AddBind("stock", "一键上货", stockKey!);
        AddBind("stow", "一键收纳", stowKey!);
        ModHelper.AddGameOptionToggle(Id, "qol", "daily_expected_visitors", () => "每日逐项判定预期普通顾客（次日起）",
            () => ExpectedVisitorsEnabled, SetExpectedVisitors,
            () => "默认关闭。开启后每项预期普通顾客按原概率独立判定，不受原普通名额上限截断；原名额不足时继续随机补足，剧情日程不改。");
        ModHelper.AddDebugFunction(Id, ModHelper.DEBUG_CATEGORY_STORE, "深空管家", ControlPanel.Open);
        // OnEnable executes before the loader registers this mod as running.
        ModHelper.RunOnMainThread(Id, () =>
        {
            if (!Enabled) return;
            NativeInventoryButtons.Init();
            NativeSecurityHighlights.Init();
            NativeBerryQuality.Init();
            ModHelper.StartCoroutine(Id, InputLoop());
        });
    }

    private static void AddBind(string name, string label, ConfigEntry<KeyCode> setting) =>
        ModHelper.AddGameOptionKeyBind(Id, "qol", name, () => label, () => setting.Value, value =>
        { setting.Set(value); });

    public void OnDisable()
    {
        StowService.Cancel();
        GunModdingService.Cancel();
        NativeGunModding.Dispose();
        NativeDailyVisitors.Dispose();
        NativeDisplayFixtures.Dispose();
        Enabled = false;
        ModHook.OnGameLoadedLate -= OnLoaded;
        ModHelper.StopAllCoroutines(Id);
        ModHelper.RemoveDebugFunctions(Id);
        ModHelper.RemoveGameOptions(Id);
        NativeStorageLabels.Dispose();
        NativeInventoryButtons.Dispose();
        NativeBottlingMenu.Dispose();
        NativeFeedback.Dispose();
        NativeAppraisal.Dispose();
        NativeAuthenticity.Dispose();
        NativeSecurityHighlights.Dispose();
        NativeBerryQuality.Dispose();
        NativeAugCustomer.Dispose();
        ControlPanel.Close();
        foreach (var owner in StorageCatalog.GetPages().Select(p => p.Owner).Where(p => p != null).Distinct())
            StorageExpansionHelper.InitTabNames(owner!);
        StorageRules.ClearCache();
        Log?.Log("深空管家已停用；储物规则随存档保留。");
    }

    private static void OnLoaded()
    {
        StowService.Cancel();
        GunModdingService.Cancel();
        NativeGunModding.Dispose();
        NativeDailyVisitors.Dispose();
        NativeDisplayFixtures.ResetForLoad();
        NativeStorageLabels.Dispose();
        NativeInventoryButtons.Dispose();
        NativeBottlingMenu.Dispose();
        NativeFeedback.Dispose();
        NativeAppraisal.Dispose();
        NativeAuthenticity.Dispose();
        NativeSecurityHighlights.Dispose();
        NativeBerryQuality.Dispose();
        NativeAugCustomer.Dispose();
        NativeBottlingMenu.Init();
        ControlPanel.Close();
        StorageRules.ClearCache();
        foreach (var owner in StorageCatalog.GetPages().Select(p => p.Owner).Where(p => p != null).Distinct())
            StorageRules.RefreshCaptions(owner!);
        NativeInventoryButtons.Init();
        NativeSecurityHighlights.Init();
        NativeBerryQuality.Init();
    }

    private static IEnumerator InputLoop()
    {
        float nextUiRefresh = 0;
        while (Enabled)
        {
            yield return null;
            if (Time.unscaledTime >= nextUiRefresh)
            {
                nextUiRefresh = Time.unscaledTime + 0.3f;
                RefreshUi(NativeInventoryButtons.Refresh, "库存按钮");
                RefreshUi(NativeStorageLabels.Refresh, "储物标签");
                RefreshUi(NativeFeedback.Refresh, "操作提示");
                RefreshUi(NativeAppraisal.Refresh, "价格鉴定");
                RefreshUi(NativeAuthenticity.Refresh, "真伪鉴定");
                RefreshUi(NativeSecurityHighlights.Refresh, "搜查提示");
                RefreshUi(NativeBerryQuality.Refresh, "莓果品质");
                RefreshUi(NativeAugCustomer.Refresh, "改造体姓名");
                RefreshUi(NativeDisplayFixtures.Refresh, "独立陈列区");
                RefreshUi(NativeDailyVisitors.Refresh, "预期顾客规则");
                RefreshUi(NativeGunModding.Refresh, "一键改装");
            }
            if (Typing()) continue;
            if (Down(panelKey)) ControlPanel.Toggle();
            else if (Down(stockKey)) Execute(StockingService.Run);
            else if (Down(stowKey)) Execute(() => StowService.Start());
        }
    }
    private static readonly Dictionary<string, float> RefreshWarnings = new();
    private static void RefreshUi(Action refresh, string feature)
    {
        try { refresh(); }
        catch (Exception ex)
        {
            if (!RefreshWarnings.TryGetValue(feature, out float next) || Time.unscaledTime >= next)
            {
                RefreshWarnings[feature] = Time.unscaledTime + 30;
                Log?.Warning(feature + "刷新失败：" + ex);
            }
        }
    }
    private static bool Down(ConfigEntry<KeyCode>? key) => key != null && key.Value != KeyCode.None && Input.GetKeyDown(key.Value);
    private static bool Typing()
    {
        var selected = EventSystem.current?.currentSelectedGameObject;
        return selected != null && (selected.GetComponent<TMP_InputField>() != null || selected.GetComponent<UnityEngine.UI.InputField>() != null);
    }

    internal static bool Ready(out string reason, bool allowPinMode = false)
    {
        reason = "";
        if (!Enabled || !PlayerStore.HasInstance || PlayerStore.Instance.saveSlotId < 0 || EmporiumEntry.Instance?.invElement == null)
        { reason = "请先载入存档并进入店铺。"; return false; }
        if ((MapUIManager.Instance?.IsOutside ?? false) || (GameMaster.current?.InCombat() ?? false))
        { reason = "请回到店铺后操作。"; return false; }
        if (ItemMouseDragHandler.current?.IsDraggingItem ?? false)
        { reason = "请先放下正在拖拽的物品。"; return false; }
        if (ItemMultiSelectHandler.current?.HasSelection ?? false)
        { reason = "请先取消物品多选。"; return false; }
        if (!allowPinMode && ItemPinHelper.IsPinMode)
        { reason = "请先退出固定模式，再执行自动操作。"; return false; }
        return true;
    }

    internal static void Execute(Func<string> action) => ExecuteCore(action, false);
    internal static void OpenManagement(Func<string> action) => ExecuteCore(action, true);

    private static void ExecuteCore(Func<string> action, bool allowPinMode)
    {
        if (busy || StowService.IsRunning || GunModdingService.IsRunning)
        {
            ControlPanel.Status(StowService.IsRunning ? StowService.CurrentStatus : GunModdingService.IsRunning ? GunModdingService.CurrentStatus : "正在执行上一项操作。");
            return;
        }
        if (!Ready(out string reason, allowPinMode)) { ControlPanel.Status(reason); return; }
        busy = true;
        try { ControlPanel.Status(action()); }
        catch (Exception ex)
        { Log?.Error(ex); ControlPanel.Status("操作停止：" + ex.Message + " 请查看游戏日志。"); }
        finally { busy = false; }
    }
}
