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

    public void Init(ModManifest manifest)
    {
        Log = new ModLog(manifest);
        var config = ModHelper.GetConfig(Id);
        panelKey = config.DefineEnum("panel_key", KeyCode.F6, "操作", () => "控制面板快捷键");
        stockKey = config.DefineEnum("stock_key", KeyCode.None, "操作", () => "一键上货快捷键（默认未绑定）");
        stowKey = config.DefineEnum("stow_key", KeyCode.None, "操作", () => "一键收纳快捷键（默认未绑定）");
        Log.Log("深空管家 0.3.1：修复普通重要储物容器内容被误排除；固定柜体不影响未锁定内容上货。保留卢剧情保护、鉴定及品质提示。");
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
        NativeStorageLabels.Dispose();
        NativeInventoryButtons.Dispose();
        NativeBottlingMenu.Dispose();
        NativeFeedback.Dispose();
        NativeAppraisal.Dispose();
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
                try
                {
                    NativeInventoryButtons.Refresh();
                    NativeStorageLabels.Refresh();
                    NativeFeedback.Refresh();
                    NativeAppraisal.Refresh();
                    NativeSecurityHighlights.Refresh();
                    NativeBerryQuality.Refresh();
                    NativeAugCustomer.Refresh();
                }
                catch (Exception ex) { Log?.Warning("原生界面刷新失败：" + ex.Message); }
            }
            if (Typing()) continue;
            if (Down(panelKey)) ControlPanel.Toggle();
            else if (Down(stockKey)) Execute(StockingService.Run);
            else if (Down(stowKey)) Execute(() => StowService.Run());
        }
    }
    private static bool Down(ConfigEntry<KeyCode>? key) => key != null && key.Value != KeyCode.None && Input.GetKeyDown(key.Value);
    private static bool Typing()
    {
        var selected = EventSystem.current?.currentSelectedGameObject;
        return selected != null && (selected.GetComponent<TMP_InputField>() != null || selected.GetComponent<UnityEngine.UI.InputField>() != null);
    }

    internal static bool Ready(out string reason)
    {
        reason = "";
        if (!Enabled || PlayerStore.Instance == null || PlayerStore.Instance.saveSlotId < 0 || EmporiumEntry.Instance?.invElement == null)
        { reason = "请先载入存档并进入店铺。"; return false; }
        if ((MapUIManager.Instance?.IsOutside ?? false) || (GameMaster.current?.InCombat() ?? false))
        { reason = "请回到店铺后操作。"; return false; }
        if ((ItemMouseDragHandler.current?.IsDraggingItem ?? false) || (ItemMultiSelectHandler.current?.HasSelection ?? false) || ItemPinHelper.IsPinMode)
        { reason = "请先结束拖拽、多选或固定模式。"; return false; }
        return true;
    }

    internal static void Execute(Func<string> action)
    {
        if (busy) { ControlPanel.Status("正在执行上一项操作。"); return; }
        if (!Ready(out string reason)) { ControlPanel.Status(reason); return; }
        busy = true;
        try { ControlPanel.Status(action()); }
        catch (Exception ex)
        { Log?.Error(ex); ControlPanel.Status("操作停止：" + ex.Message + " 请查看游戏日志。"); }
        finally { busy = false; }
    }
}
