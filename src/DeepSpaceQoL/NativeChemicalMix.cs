using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DeepSpaceQoL;

/// <summary>Two explicit stages in the existing composition scanner. Source binding belongs to the scanner.</summary>
internal static class NativeChemicalMix
{
    private const string WindowId = ModEntry.Id + ".chemical-mix", PickerId = WindowId + ".picker";
    private static ChemScannerUIManager? manager;
    private static GameItem? scanner, target;
    private static RectTransform? root;
    private static CustomUIWindow? window, picker;
    private static string active = "";
    private static bool finishing, rebuilding;
    private static int backFrame = -1;

    // Called only from the native close-topmost path, after menus/drag/selection priority.
    // Also handles RMB when an old text input still owns keyboard focus.
    internal static bool TryHandleBack()
    {
        if (backFrame == Time.frameCount) return true;
        if (!ModEntry.Enabled || manager == null || scanner == null || target == null || !IsViewing(scanner, target)) return false;
        if (picker?.IsVisible == true) { picker.Close(); picker = null; backFrame = Time.frameCount; return true; }
        if (window?.IsVisible == true) { window.Close(); window = null; backFrame = Time.frameCount; return true; }
        if (CustomUIManager.Instance?.HasEscapeTarget() == true) return false;
        long focus = PaperPanelFocusStamp.GetStamp(manager.panel);
        if (WindowsHandler.current?.visibleWindows?.Any(w => w.canMove && w.IsVisible() && w.focusStamp > focus) == true) return false;
        var paper = StoreUIManager.Instance?.paperUICanvasObject;
        if (paper != null)
            foreach (Transform panel in paper.transform)
                if (panel.gameObject != manager.panel && panel.gameObject.activeSelf && PaperPanelFocusStamp.GetStamp(panel.gameObject) > focus) return false;
        ChemicalMixService.Cancel(); CloseWindows(); manager.Close(); backFrame = Time.frameCount;
        return true;
    }

    internal static void Opened(ChemScannerUIManager owner, GameItem instrument, GameItem sample)
    {
        if (!ReferenceEquals(scanner, instrument) || !ReferenceEquals(target, sample))
        { ChemicalMixService.Cancel(); CloseWindows(); }
        manager = owner; scanner = instrument; target = sample;
    }

    internal static bool IsViewing(GameItem instrument, GameItem sample) => ModEntry.Enabled && manager != null &&
        ReferenceEquals(manager, ChemScannerUIManager.Instance) && manager.panel != null && manager.panel.activeInHierarchy &&
        ReferenceEquals(scanner, instrument) && ReferenceEquals(target, sample) && ReferenceEquals(manager.currentScannedItem, sample);

    internal static void Refresh()
    {
        if (!ModEntry.Enabled) { Dispose(); return; }
        if (manager == null || scanner == null || target == null || !IsViewing(scanner, target))
        {
            if (ChemicalMixService.IsRunning) ChemicalMixService.Cancel();
            CloseWindows();
            if (root != null) root.gameObject.SetActive(false);
            return;
        }
        bool show = ChemicalMixService.HasRecipeInput(target) && GeneralHelper.IsItemEffectivelyOwned(target) &&
            GeneralHelper.IsItemEffectivelyOwned(scanner);
        if (root != null && root.parent != manager.panel.transform) { Object.Destroy(root.gameObject); root = null; }
        if (root == null && show && CustomUIManager.Instance != null)
        {
            root = new GameObject("DeepSpaceQoL chemical mixing", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(manager.panel.transform, false);
            root.anchorMin = root.anchorMax = new Vector2(.5f, 0);
            root.pivot = new Vector2(.5f, 0); root.anchoredPosition = new Vector2(0, 14);
            root.sizeDelta = new Vector2(132, 34);
            root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var builder = CustomUIManager.Instance.BuildInto(root, false, WindowId + ".entry").UseMachineryStyle();
            builder.AddButton("一键配比", () => ModEntry.OpenManagement(() =>
            {
                ChemicalMixService.Cancel(); active = ""; finishing = false; Open();
                return "已打开两阶段配比。先绑定一个药剂页，再计算预览。";
            }));
            builder.Last.SetPreferredSize(132, 34).SetFlexible(1, 0).SetTooltip("①过滤/蒸发前配比；②处理后重新分析提纯。计算不扣料，确认后才精确加药。");
            CustomUIBuilder.RebuildLayout(root);
        }
        if (root != null) { root.gameObject.SetActive(show); root.SetAsLastSibling(); }
        if (window?.IsAlive == true)
        {
            window.Get("calculate")?.SetInteractable(!ChemicalMixService.IsRunning);
            window.Get("execute")?.SetInteractable(!ChemicalMixService.IsRunning && ChemicalMixService.Plan?.Doses.Count > 0);
        }
    }

    internal static void UpdateStatus(string value) { if (window?.IsAlive == true) window.Get("status")?.SetText(value); }
    internal static void ShowPlan() { if (window?.IsAlive == true) Open(); }

    private static void Open()
    {
        if (scanner == null || target == null || !IsViewing(scanner, target) || CustomUIManager.Instance == null) return;
        CloseWindows();
        int[] parts = WaterHelper.GetLiquidParts(target);
        if (!ChemicalMixPlanner.ActiveIds.Contains(active))
            active = ChemicalMixPlanner.DefaultActive(parts);
        var b = CustomUIManager.Instance.CreateWindow(WindowId, "化学配比 · 试用版", CustomUIManager.LAYER_OVERLAY, style: CustomUIManager.Style.Machinery);
        window = b.Handle;
        float height = 1920f * Screen.height / Math.Max(1, Screen.width);
        b.SetSize(740, Math.Min(780, Math.Max(330, height - 50))).Center().SetDraggable(true).SetCloseOnEscape()
            .OnClose(() => { if (!rebuilding) { ChemicalMixService.Cancel(); backFrame = Time.frameCount; } });
        b.BeginScrollFlexible();
        b.AddHeader(finishing ? "② 处理后提纯" : "① 加酒精／沉淀配比");
        b.BeginRow().AddButton("① 加酒精／沉淀配比", () => ChooseStage(false)).AddButton("② 处理后提纯", () => ChooseStage(true)).End();
        b.AddLabel(finishing ? "先完成过滤／蒸发，再用分析仪打开处理后的容器。此阶段重算残余酸碱和可处理污染；可能仍需再次干燥。" :
            "支持搅拌后的原料浆：先计算并补充萃取酒精，再处理蛋白／酸碱和油。不要求先有提取物；原生加热萃取、过滤和蒸发仍由你操作。");
        b.AddLabel($"当前：{target.GetDisplayName(true)} · {WaterScannerUIManager.FormatAmount(ChemicalMixModel.Total(parts))} · {target.GetTagReadonly(ChemicalHelper.CONTAINER_TEMP_TAG).GetDouble():F1}°C");
        b.AddButton("目标药品：" + RecipeName(active) + "  ▼", PickRecipe);
        var page = ChemicalMixService.BoundPage(scanner);
        b.AddButton("药剂来源：" + (page?.Name ?? "尚未绑定／绑定页不存在") + "  ▼", PickPage);
        b.Last.SetTooltip("绑定保存在这台分析仪上，只用这一页的直接药剂瓶。允许你主动选择模组锁定页，不改变上货／收纳的锁保护。");
        if (page != null && !ChemicalMixService.CanUsePage(page)) b.AddWarning("绑定页已不可访问或被原生封锁，请重选。");
        b.AddLabel("冷配比范围：10°C ≤ 温度 < 40°C。可从梦帽菇浆、噩梦菇浆／咖啡浆开始补酒精并处理现有蛋白；不会把室温加酒精当作已完成萃取。");
        b.AddButton("计算方案（不消耗药剂）", () => ModEntry.OpenManagement(() =>
        {
            if (scanner == null || target == null) return "分析对象已变化。";
            var source = ChemicalMixService.BoundPage(scanner);
            return source == null ? "请先绑定一个药剂来源页。" : ChemicalMixService.Calculate(scanner, target, source, active, finishing);
        }), "calculate");
        var plan = ChemicalMixService.Plan;
        if (plan != null)
        {
            b.AddHeader("预计方案（实际反应后重新核对）");
            b.AddLabel(plan.Note);
            if (plan.Final.Length > 0)
            {
                b.AddLabel(plan.Raw.Length > 0 ? "当前为原料浆准备阶段：下面列出实际加药后的组分，不虚构尚未加热得到的提取物。浆量归零后再进行过滤／蒸发。" :
                    $"当前有效成分占比 {ChemicalMixModel.Purity(plan.Initial, plan.Active):F2}% → 加药并反应后预计 {ChemicalMixModel.Purity(plan.Final, plan.Active):F2}%。\n这是过滤／蒸发前的占比，不保证成品纯度；终结机还会加入自身污染。");
                if (plan.Doses.Count > 0)
                    foreach (var group in plan.Doses.GroupBy(d => d.Reagent))
                        b.AddLabel($"消耗 {group.Key.Name}：{WaterScannerUIManager.FormatAmount(group.Sum(d => d.Volume))}（按比例取用的混合液，含其杂质）");
                AddResiduals(b, plan.Final);
            }
            foreach (string warning in plan.Warnings) b.AddWarning(warning);
            if (plan.Limited) b.AddLabel("每类优先比较浓度较高的四瓶；先补酒精，再最多六轮沉淀／中和。未承诺所有组合的全局最优。");
            b.AddLabel("试剂不足时只显示现有药剂可执行的部分方案；未解决的污染列在上方。请核对后再执行，不会跨页补料。");
        }
        b.AddLabel(ChemicalMixService.Status, "status");
        b.AddButton("执行以上方案（实际消耗药剂）", () => ModEntry.OpenManagement(ChemicalMixService.Execute), "execute");
        b.Last.SetInteractable(plan?.Doses.Count > 0 && !ChemicalMixService.IsRunning);
        b.AddButton("停止计算／加药", () => { ChemicalMixService.Cancel(); UpdateStatus(ChemicalMixService.Status); });
        b.AddButton("关闭", () => { ChemicalMixService.Cancel(); CloseWindows(); });
        b.End().Show();
    }

    private static void AddResiduals(CustomUIBuilder b, int[] parts)
    {
        var residuals = Liquid.Liquids.Select((l, i) => (liquid: l, volume: parts[i])).Where(p => p.volume > 0 &&
            p.liquid.identifier != active).OrderByDescending(p => p.volume).ToArray();
        if (residuals.Length == 0) { b.AddLabel("预计无其他组分残留；实际以重新分析为准。"); return; }
        b.AddHeader("预计剩余组分／下一步");
        foreach (var p in residuals)
        {
            string method = p.liquid.identifier.EndsWith("_mush", StringComparison.Ordinal) ? "尚未萃取：先按原生温窗加热至浆量归零，暂勿过滤／蒸发" :
                p.liquid.isEvaporative ? "原料浆萃取完成后，再用原生蒸发／干燥" :
                p.liquid.identifier != "microbe" && p.liquid.particleSize >= 22 ? "可尝试孔径22的原生滤器（受耐久／残留限制）" :
                p.liquid.identifier == "heavy_metal" ? "重金属：本试用版不能靠加药去除，22孔径滤器也无法处理" :
                p.liquid.identifier == "chemical_contaminant" ? "化学污染：缺氯／水或已达原生处理下限" :
                "本方案尚未去除，请重新分析后处理";
            b.AddLabel($"{p.liquid.displayName} {WaterScannerUIManager.FormatAmount(p.volume)}：{method}");
        }
    }

    private static string RecipeName(string id) => ChemFinisherRecipe.chemFinisherRecipes.FirstOrDefault(r => r.activeIngredientId == id)?.displayName ?? id;
    private static void ChooseStage(bool value)
    {
        if (ChemicalMixService.IsRunning) { NativeFeedback.Show("请先停止当前任务再切换阶段。"); return; }
        finishing = value; ChemicalMixService.Cancel(); Open();
    }
    private static void PickRecipe()
    {
        if (ChemicalMixService.IsRunning || CustomUIManager.Instance == null) return;
        picker?.Close();
        var b = CustomUIManager.Instance.CreateWindow(PickerId, "选择目标药品", CustomUIManager.LAYER_OVERLAY, style: CustomUIManager.Style.Machinery);
        picker = b.Handle; b.SetSize(530, 460).Center().SetDraggable(true).SetCloseOnEscape().BeginScrollFlexible();
        foreach (string id in ChemicalMixPlanner.ActiveIds)
        {
            string selected = id;
            b.AddButton(RecipeName(id), () => { ChemicalMixService.Cancel(); active = selected; Open(); });
        }
        b.End().Show();
    }
    private static void PickPage()
    {
        if (ChemicalMixService.IsRunning || CustomUIManager.Instance == null || scanner == null) return;
        picker?.Close();
        var b = CustomUIManager.Instance.CreateWindow(PickerId, "绑定药剂来源页", CustomUIManager.LAYER_OVERLAY, style: CustomUIManager.Style.Machinery);
        picker = b.Handle;
        float height = 1920f * Screen.height / Math.Max(1, Screen.width);
        b.SetSize(700, Math.Min(650, Math.Max(320, height - 80))).Center().SetDraggable(true).SetCloseOnEscape().BeginScrollFlexible();
        b.AddLabel("只使用这一页的直接酒精／药剂瓶，允许主动选择模组锁页／锁箱。源瓶至少一半为对应试剂，杂质完整计入；固体药片暂不参与。");
        foreach (var page in StorageCatalog.GetStoragePages().Where(ChemicalMixService.CanUsePage))
        {
            var selected = page;
            b.AddButton(page.Name + (StorageRules.IsLocked(page.Inventory) ? "【锁定·主动授权配比】" : ""), () => ModEntry.OpenManagement(() =>
            {
                if (scanner == null || !GeneralHelper.IsItemEffectivelyOwned(scanner) || !ChemicalMixService.CanUsePage(selected)) return "分析仪或来源页已变化。";
                ChemicalMixService.Cancel();
                scanner.ModifyTag(ChemicalMixService.OwnerTag, t => t.Enable().SetInt(selected.Owner!.uniqueId));
                scanner.ModifyTag(ChemicalMixService.PageTag, t => t.Enable().SetInt(selected.Index));
                Open(); return "已绑定：" + selected.Name;
            }));
            b.Last.SetPreferredSize(0, 40);
        }
        b.End().Show();
    }

    private static void CloseWindows()
    {
        rebuilding = true;
        try { picker?.Close(); picker = null; window?.Close(); window = null; }
        finally { rebuilding = false; }
    }
    internal static void Dispose()
    {
        ChemicalMixService.Cancel(); CloseWindows();
        if (root != null) { root.gameObject.SetActive(false); Object.Destroy(root.gameObject); }
        root = null; manager = null; scanner = null; target = null; active = "";
    }
}

[HarmonyPatch(typeof(ChemScannerUIManager), nameof(ChemScannerUIManager.Open))]
internal static class ChemicalMixScannerPatch
{
    private static void Postfix(ChemScannerUIManager __instance, GameItem scanner, GameItem water)
    { if (ModEntry.Enabled && scanner != null && water != null) NativeChemicalMix.Opened(__instance, scanner, water); }
}

[HarmonyPatch(typeof(StoreUIManager), "CloseTopmostEscapeTarget")]
internal static class ChemicalMixBackPatch
{
    private static bool Prefix(ref bool __result)
    {
        if (!ModEntry.Enabled || !NativeChemicalMix.TryHandleBack()) return true;
        __result = true; return false;
    }
}

[HarmonyPatch(typeof(ChemScannerUIManager), "Update")]
internal static class ChemicalMixPointerBackPatch
{
    private static void Postfix()
    {
        if (ModEntry.Enabled && Input.GetMouseButtonDown(1) && StoreUIManager.IsTextFieldFocused()) NativeChemicalMix.TryHandleBack();
    }
}
