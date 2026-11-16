using System.Collections;
using System.Diagnostics;

namespace DeepSpaceQoL;

/// <summary>Plans on detached data; execution only pours, then observes the native real-time reactions.</summary>
internal static class ChemicalMixService
{
    internal const string OwnerTag = "DEEPSPACE_QOL_CHEM_REAGENT_OWNER", PageTag = "DEEPSPACE_QOL_CHEM_REAGENT_PAGE";
    internal static bool IsRunning { get; private set; }
    internal static string Status { get; private set; } = "绑定药剂页后计算方案，不会在计算时消耗药剂。";
    internal static ChemicalMixPlan? Plan { get; private set; }
    private static Context? context;
    private static int generation;

    internal static bool HasRecipeInput(GameItem? target) => target != null && target.unitCount == 1 &&
        target.IsTag(WaterHelper.LIQUID_CONTAINER_TAG) && target.IsTag(WaterHelper.LIQUID_CONTAINER_CAPACITY) &&
        !target.IsTag(WaterHelper.FILTER_CONTAINER_TAG) && !target.IsTag("WINE_CONTAINER_TAG") &&
        !target.IsTag(WaterHelper.LIQUID_CONTAINER_DROPPER_TAG);

    internal static bool CanUseContainer(GameItem? target, out string reason)
    {
        if (!CanMutateLiquid(target, out reason)) return false;
        var parent = target!.parentInventory;
        if (StorageCatalog.GetPages().Any(p => ReferenceEquals(p.Inventory, parent))) return true;
        var owner = parent.GetParentItem();
        if (owner != null && ChemicalBatchService.CanUseMachine(owner, out _) &&
            ChemicalBatchService.TryParts(owner, out var slot, out _, out _) && ReferenceEquals(slot, parent)) return true;
        reason = "请先把待处理容器放到库存、储物页或化学终结机输入槽；运行中的其他设备不参与自动加药。";
        return false;
    }

    private static bool CanMutateLiquid(GameItem? target, out string reason)
    {
        reason = "";
        if (!HasRecipeInput(target) || target!.parentInventory == null || target is GameItemElement e && e.IsDestroyed() ||
            !GeneralHelper.IsItemEffectivelyOwned(target) || StoryProtection.IsProtected(target) ||
            !ChemicalBatchService.CanUseExplicitInventory(target.parentInventory) || !StorageRules.Policy(target).IsValid() ||
            GeneralHelper.IsItemLocked(target) || LockHelper.IsCurrentlyLocked(target) || target.IsTag("ITEM_BEHIND_LOCK_TAG") ||
            target.forceDisableUse || !target.canUseOutsideCombat || !WaterHelper.CanUseWaterContainer(target) || BottlingCatalog.IsOnTradeCounter(target))
        { reason = "仅能配比自有、原生可访问的普通液体容器；顾客、剧情、卢箱、封锁区域和滤器容器不参与。模组页锁／箱锁不限制你明确选择的配比。"; return false; }
        return true;
    }

    internal static bool CanUsePage(StoragePage? page) => page?.Owner != null &&
        GeneralHelper.IsItemEffectivelyOwned(page.Owner) && ChemicalBatchService.CanUseExplicitInventory(page.Inventory) &&
        StorageCatalog.GetStoragePages().Any(p => ReferenceEquals(p.Owner, page.Owner) && p.Index == page.Index && ReferenceEquals(p.Inventory, page.Inventory));

    internal static StoragePage? BoundPage(GameItem scanner)
    {
        if (!scanner.IsTag(OwnerTag) || !scanner.IsTag(PageTag)) return null;
        int owner = scanner.GetTagReadonly(OwnerTag).GetInt(), index = scanner.GetTagReadonly(PageTag).GetInt();
        return StorageCatalog.GetStoragePages().FirstOrDefault(p => p.Owner?.uniqueId == owner && p.Index == index);
    }

    internal static void Cancel(bool clear = true)
    {
        generation++; IsRunning = false;
        if (clear) { Plan = null; context = null; }
        Status = "配比任务已停止；已加药和已发生的原生反应保留，请重新分析后计算。";
    }

    internal static string Calculate(GameItem scanner, GameItem target, StoragePage page, string active, bool finishing)
    {
        if (IsRunning) return Status;
        Plan = null; context = null;
        if (!ModEntry.Ready(out string reason, true) || !CanUseContainer(target, out reason)) return reason;
        int[] initial = WaterHelper.GetLiquidParts(target);
        if (!ChemicalMixModel.SafeInput(initial, target.GetTagReadonly(ChemicalHelper.CONTAINER_TEMP_TAG).GetDouble()))
            return "请在 10°C ≤ 温度 < 40°C 进行冷配比；对应菇浆／咖啡浆可以先加酒精并处理蛋白，专用热合成前体暂不处理。";
        if (!ChemicalMixPlanner.HasTarget(initial, active, finishing))
            return finishing ? "处理后提纯需含有所选药品的有效成分。原料浆请先用第一阶段加药，再完成原生加热萃取。" : "当前容器不含所选药品的有效成分或对应原料浆，请选择对应药品。";
        if (!CanUsePage(page)) return "绑定药剂页不在可用储物区或被原生封锁。";
        if (!GeneralHelper.IsItemEffectivelyOwned(scanner) || StoryProtection.IsProtected(scanner)) return "请使用自有分析仪。";
        var c = new Context(scanner, target, page);
        var reagents = new List<ChemicalReagent>();
        foreach (var item in page.Inventory.childItems.ToArray())
        {
            if (ReferenceEquals(item, target) || !CanUseReagent(item, target, page, out _)) continue;
            int[] parts = WaterHelper.GetLiquidParts(item);
            string kind = ChemicalMixPlanner.ReagentKind(parts);
            if (kind.Length == 0) continue;
            reagents.Add(new ChemicalReagent { Item = item, Name = item.GetDisplayName(true) + " #" + item.uniqueId,
                Kind = kind, Parts = parts, Temperature = item.GetTagReadonly(ChemicalHelper.CONTAINER_TEMP_TAG).GetDouble() });
        }
        var plan = new ChemicalMixPlan(); Plan = null; context = c;
        int ticket = ++generation;
        IsRunning = true; SetStatus("正在计算两阶段配比方案，试剂不会在计算时消耗…");
        try
        {
            if (ModHelper.StartCoroutine(ModEntry.Id, Build(ticket, c, plan, active, finishing, reagents)) == null)
            { IsRunning = false; return "无法启动计算，原料未改变。"; }
        }
        catch { IsRunning = false; throw; }
        return Status;
    }

    private static IEnumerator Build(int ticket, Context c, ChemicalMixPlan plan, string active, bool finishing, List<ChemicalReagent> reagents)
    {
        using var work = ChemicalMixPlanner.Search(plan, WaterHelper.GetLiquidParts(c.Target),
            c.Target.GetTagReadonly(ChemicalHelper.CONTAINER_TEMP_TAG).GetDouble(), WaterHelper.GetTotalCapacity(c.Target), active, finishing, reagents).GetEnumerator();
        yield return null;
        var timer = Stopwatch.StartNew();
        int evaluated = 0;
        try
        {
            while (ticket == generation && c.Valid())
            {
                bool next;
                try { next = work.MoveNext(); }
                catch (Exception ex) { ModEntry.Log?.Error(ex); SetStatus("计算失败，未消耗试剂：" + ex.Message); yield break; }
                if (!next)
                {
                    Plan = plan;
                    SetStatus($"计算完成：{plan.Doses.Count} 次加药，评估 {evaluated} 个候选。" + plan.Note);
                    NativeChemicalMix.ShowPlan();
                    yield break;
                }
                evaluated = work.Current;
                if (timer.ElapsedMilliseconds >= 4)
                {
                    SetStatus($"正在计算：已评估 {evaluated} 个候选，未消耗试剂。");
                    yield return null; timer.Restart();
                }
            }
            if (ticket == generation) SetStatus("窗口、容器或场景已变化，计算取消，未消耗试剂。");
        }
        finally { if (ticket == generation) IsRunning = false; }
    }

    internal static string Execute()
    {
        if (IsRunning) return Status;
        var c = context; var plan = Plan;
        if (c == null || plan == null || plan.Doses.Count == 0) return "请先计算有可执行步骤的配比方案。";
        if (!c.Valid() || !CanUseContainer(c.Target, out string reason)) return "容器或场景已变化，请重新分析计算。";
        if (!WaterHelper.GetLiquidParts(c.Target).SequenceEqual(plan.Initial) ||
            plan.Doses.GroupBy(d => d.Reagent).Any(g => g.Key.Item == null ||
                !WaterHelper.GetLiquidParts(g.Key.Item).SequenceEqual(g.Key.Parts))) return "容器或药剂成分已变化，请重新计算，未加药。";
        Plan = null; // A plan is single-use, including failed/cancelled attempts.
        int ticket = ++generation;
        IsRunning = true; SetStatus("开始按预览方案加药，等待原生反应；Esc 或停止按钮可中断。");
        try
        {
            if (ModHelper.StartCoroutine(ModEntry.Id, Run(ticket, c, plan)) == null) { IsRunning = false; return "无法启动执行，未加药。"; }
        }
        catch { IsRunning = false; throw; }
        return Status;
    }

    private static IEnumerator Run(int ticket, Context c, ChemicalMixPlan plan)
    {
        yield return null;
        int done = 0;
        try
        {
            int[] expected = plan.Baseline;
            foreach (var dose in plan.Doses)
            {
                float start = UnityEngine.Time.unscaledTime;
                while (c.Valid() && ticket == generation && !WaterHelper.GetLiquidParts(c.Target).SequenceEqual(expected))
                {
                    if (Cancelled() || UnityEngine.Time.unscaledTime - start > 90)
                    { SetStatus($"已加药 {done} 次。实际反应尚未达到预计值，已停止；请重新分析计算。已消耗的试剂保留原生效果。"); yield break; }
                    yield return null;
                }
                if (ticket != generation) yield break;
                if (!c.Valid() || !ModEntry.Ready(out _, true) || Cancelled() || !CanUseContainer(c.Target, out _) ||
                    !CanUseReagent(dose.Reagent.Item, c.Target, c.Page, out _) ||
                    !WaterHelper.GetLiquidParts(dose.Reagent.Item!).SequenceEqual(dose.SourceBefore))
                { SetStatus($"已加药 {done} 次。药剂、锁定、容器或场景已变化，已停止；请重新计算。"); yield break; }
                try { Pour(dose.Reagent.Item!, c.Target, dose.Volume, dose.AfterPour); }
                catch (Exception ex) { ModEntry.Log?.Error(ex); SetStatus("本次加药已停止：" + ex.Message); yield break; }
                done++;
                SetStatus($"已加药 {done}/{plan.Doses.Count}：{dose.Reagent.Name} {WaterScannerUIManager.FormatAmount(dose.Volume)}；等待原生反应。");
                expected = dose.AfterReaction;
                yield return null;
            }
            // Observe the last reaction too; a final pour alone is never reported as successful purification.
            float last = UnityEngine.Time.unscaledTime;
            while (ticket == generation && c.Valid() && !WaterHelper.GetLiquidParts(c.Target).SequenceEqual(expected))
            {
                if (Cancelled() || UnityEngine.Time.unscaledTime - last > 90)
                { SetStatus($"已加药 {done} 次，等待反应超时或被取消。请重新分析；未直接修改纯度。 "); yield break; }
                yield return null;
            }
            if (ticket != generation) yield break;
            if (!c.Valid()) { SetStatus("容器或场景已变化，配比已停止。"); yield break; }
            SetStatus(plan.Raw.Length > 0 ? RawCompletionHint(WaterHelper.GetLiquidParts(c.Target)) : plan.Finishing ? "处理后配比已完成。请重新分析残留；如仍含挥发液或沉淀，请再次蒸发／过滤，再进行批量制药。" :
                "预处理配比已完成。请按提示使用原生滤器／蒸发器，处理后点“② 处理后提纯”重新计算。");
        }
        finally { if (ticket == generation) IsRunning = false; }
    }

    internal static string RawCompletionHint(int[] actual)
    {
        long missing = Math.Max(0, ChemicalMixPlanner.SolventTarget(actual) - ChemicalMixModel.Part(actual, "alcohol"));
        return missing > 0 ? $"部分加药已完成，酒精仍不足 {WaterScannerUIManager.FormatAmount((int)Math.Min(int.MaxValue, missing))}。请先补料／分装并重新计算，暂勿加热、过滤或蒸发，避免降低萃取产率或丢失原料。" :
            "酒精／沉淀加药已完成。若仍有菇浆／咖啡浆，先用原生加热板完成对应温窗萃取，浆量归零后再过滤／蒸发；不要提前滤掉原料浆。";
    }

    internal static void Pour(GameItem source, GameItem target, int amount, int[] expected)
    {
        var from = new ItemStateSnapshot(source);
        var to = new ItemStateSnapshot(target);
        try
        {
            ChemicalDoseTransfer.Move(source, target, amount, (t, s, empty) =>
            {
                WaterFeatureHelper.OnLiquidContainerPourInto(t, s, empty);
                WaterFeatureHelper.OnLiquidContainerPourOutOf(s);
                WaterHelper.UpdateWaterBottleSound(s); WaterHelper.UpdateWaterBottleSound(t);
            });
            if (!WaterHelper.GetLiquidParts(target).SequenceEqual(expected)) throw new InvalidOperationException("实际倒入组分与计划不一致");
        }
        catch (Exception failure)
        {
            try { from.Restore(source); to.Restore(target); }
            catch (Exception restore) { throw new InvalidOperationException("加药恢复失败，请保留日志并停止保存。", new AggregateException(failure, restore)); }
            try { SpriteHelper.UpdateWaterContainerSprite(source); SpriteHelper.UpdateWaterContainerSprite(target); }
            catch (Exception visual) { ModEntry.Log?.Warning("加药数据已恢复，但图形刷新失败：" + visual.Message); }
            throw new InvalidOperationException("本次转移已回滚；先前已完成的加药保留。" + failure.Message, failure);
        }
    }

    private static bool CanUseReagent(GameItem? item, GameItem target, StoragePage page, out string reason)
    {
        reason = "";
        return item != null && !ReferenceEquals(item, target) && ReferenceEquals(item.parentInventory, page.Inventory) &&
            page.Inventory.childItems.Contains(item) && CanMutateLiquid(item, out reason) &&
            ChemicalMixModel.SafeInput(WaterHelper.GetLiquidParts(item), item.GetTagReadonly(ChemicalHelper.CONTAINER_TEMP_TAG).GetDouble()) &&
            item.MayTarget(target) && item.CanTarget(target);
    }

    private static bool Cancelled() => UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Escape);
    private static void SetStatus(string value) { Status = value; NativeChemicalMix.UpdateStatus(value); ModEntry.Log?.Log(value); }

    private sealed class Context
    {
        internal readonly GameItem Scanner, Target;
        internal readonly StoragePage Page;
        private readonly GameInventory parent;
        private readonly PlayerStore store;
        private readonly EmporiumEntry entry;
        private readonly int slot, capacity;
        internal Context(GameItem scanner, GameItem target, StoragePage page)
        {
            Scanner = scanner; Target = target; Page = page; parent = target.parentInventory;
            store = PlayerStore.Instance; entry = EmporiumEntry.Instance; slot = store.saveSlotId;
            capacity = WaterHelper.GetTotalCapacity(target);
        }
        internal bool Valid() => ModEntry.Ready(out _, true) && ReferenceEquals(PlayerStore.Instance, store) && ReferenceEquals(EmporiumEntry.Instance, entry) &&
            store.saveSlotId == slot && ReferenceEquals(Target.parentInventory, parent) &&
            !(Target is GameItemElement e && e.IsDestroyed()) && WaterHelper.GetTotalCapacity(Target) == capacity &&
            ChemicalMixModel.SafeInput(WaterHelper.GetLiquidParts(Target), Target.GetTagReadonly(ChemicalHelper.CONTAINER_TEMP_TAG).GetDouble()) &&
            NativeChemicalMix.IsViewing(Scanner, Target) && CanUseContainer(Target, out _) && CanUsePage(Page);
    }
}
