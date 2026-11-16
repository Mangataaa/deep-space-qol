using System.Collections;
using DeepSpaceQoL.Core;

namespace DeepSpaceQoL;

/// <summary>Batch the audited native chem-finisher recipe into one explicit grid. Never searches nested storage.</summary>
internal static class ChemicalBatchService
{
    internal const string MachineId = "chem_finisher";
    internal const string TargetOwnerTag = "DEEPSPACE_QOL_CHEM_OUTPUT_OWNER";
    internal const string TargetPageTag = "DEEPSPACE_QOL_CHEM_OUTPUT_PAGE";
    internal static bool IsRunning { get; private set; }
    internal static string CurrentStatus { get; private set; } = "";
    private static int generation;

    internal static bool TryParts(GameItem? machine, out GameSlotInventory? container,
        out GameGridInventory? output, out MachineryStack? controls)
    {
        container = null; output = null; controls = null;
        if (machine == null || machine.identifier != MachineId || machine is GameCharacterItem ||
            machine is GameItemElement element && element.IsDestroyed() ||
            machine.contentWindow?.childElement is not MachineryPanelRow row) return false;
        var input = row.GetPanel("container")?.content;
        container = input as GameSlotInventory ?? (input as MachineryIntake)?.content as GameSlotInventory;
        var result = row.GetPanel("output")?.content;
        output = result as GameGridInventory ?? (result as MachineryIntake)?.content as GameGridInventory;
        controls = row.GetPanel("control")?.content as MachineryStack;
        return container != null && output != null && controls != null &&
            ReferenceEquals(container.GetParentItem(), machine) && ReferenceEquals(output.GetParentItem(), machine);
    }

    internal static ChemFinisherRecipe? SelectedRecipe(GameItem machine) =>
        machine.IsTag(MachineChemFinisher.SELECTED_CHEM_ID_TAG)
            ? ChemFinisherRecipe.GetRecipeById(machine.GetTagReadonly(MachineChemFinisher.SELECTED_CHEM_ID_TAG).GetString()) : null;

    internal static bool CanUseMachine(GameItem machine, out string reason)
    {
        reason = "";
        if (!TryParts(machine, out var slot, out var output, out _) || machine.parentInventory == null ||
            !GeneralHelper.IsItemEffectivelyOwned(machine) || StoryProtection.IsProtected(machine) ||
            !CanUseExplicitInventory(machine.parentInventory) || !StorageRules.Policy(machine).IsValid() ||
            !CanUseExplicitInventory(slot!) || !CanUseExplicitInventory(output!) ||
            LockHelper.IsCurrentlyLocked(machine) || GeneralHelper.IsItemLocked(machine))
        { reason = "请打开自有且原生可访问的化学终结机；剧情、顾客和封锁区域不能批量操作。"; return false; }
        return true;
    }

    internal static bool CanUseSource(GameItem machine, GameItem? source, out string reason)
    {
        reason = "";
        if (!TryParts(machine, out var slot, out _, out _) || source == null ||
            !ReferenceEquals(slot!.childItem, source) || !ReferenceEquals(source.parentInventory, slot) ||
            source.unitCount != 1 || source is GameItemElement element && element.IsDestroyed() ||
            !source.IsTag(WaterHelper.LIQUID_CONTAINER_TAG) || !GeneralHelper.IsItemEffectivelyOwned(source) ||
            StoryProtection.IsProtected(source) || !StorageRules.Policy(source).IsValid() ||
            !CanUseExplicitInventory(slot) || GeneralHelper.IsItemLocked(source) || LockHelper.IsCurrentlyLocked(source) ||
            source.IsTag("ITEM_BEHIND_LOCK_TAG"))
        { reason = "输入槽需要一个自有、未锁定的原料容器；固定容器可以原位使用。"; return false; }
        return true;
    }

    internal static bool CanUseTarget(GameItem machine, GameGridInventory target)
    {
        if (!CanUseExplicitInventory(target)) return false;
        if (TryParts(machine, out _, out var output, out _) && ReferenceEquals(output, target)) return true;
        var page = StorageCatalog.Find(target);
        return page?.Owner != null && GeneralHelper.IsItemEffectivelyOwned(page.Owner) &&
            StorageCatalog.GetStoragePages().Any(p => ReferenceEquals(p.Owner, page.Owner) &&
                p.Index == page.Index && ReferenceEquals(p.Inventory, target));
    }

    // This permission is only used after the player explicitly selects a chemistry source/output.
    // QoL sale/storage locks still protect all background stocking/stowing operations.
    internal static bool CanUseExplicitInventory(GameInventory? inventory) => inventory != null &&
        !StorageRules.IsQuestInventory(inventory) && !StorageCatalog.IsNativeAccessLocked(inventory) &&
        GeneralHelper.MayPlayerInsertInto(inventory) &&
        GraphUtils.FindParentsType<GameItem>(inventory, passthrough: _ => true).All(owner =>
            GeneralHelper.IsItemEffectivelyOwned(owner) && !GeneralHelper.IsItemLocked(owner) &&
            !owner.IsTag("ITEM_BEHIND_LOCK_TAG") && StorageRules.Policy(owner).IsValid());

    internal static int AvailableCount(GameItem? source, ChemFinisherRecipe? recipe) =>
        source == null || recipe == null ? 0 : AvailableCount(WaterHelper.GetTotalVolume(source), recipe.requiredVolume);

    internal static int AvailableCount(int total, int requiredVolume) => requiredVolume <= 0 ? 0 : Math.Max(0, total) / requiredVolume;

    private static SlotMarker? FindOutputSlot(GameItem machine, GameGridInventory target, GameItem item)
    {
        // Only explicit chemistry destinations can use QoL-locked pages.
        // Native ownership, grid rules, shape and category checks remain.
        if (!CanUseTarget(machine, target) || !StorageRules.Allows(target, item) ||
            !target.MayHaveValidInventorySlot(item) || item.MaxNumRemove() < 1) return null;
        var box = target.inventoryShape;
        var original = item.modifiedShape;
        if (box == null || original == null) return null;
        for (int flip = 0; flip < 2; flip++) for (int turn = 0; turn < 4; turn++)
        {
            var shape = new GridShapeBuilder(original).SetOrientation(turn).SetFlipped(flip != 0);
            for (int y = box.minY; y <= box.maxY; y++) for (int x = box.minX; x <= box.maxX; x++)
            {
                shape.SetPosition(x, y);
                var slot = target.TryInventorySlot(item, 1, shape);
                if (slot != null && slot.targetItem == null && IsDirectSlot(target, item, slot)) return slot;
            }
        }
        return null;
    }

    private static bool IsDirectSlot(GameGridInventory target, GameItem item, SlotMarker slot) =>
        ReferenceEquals(slot.item, item) && ReferenceEquals(slot.inventory, target) && slot.numTransfer >= 1 &&
        (slot.targetItem == null || ReferenceEquals(slot.targetItem.parentInventory, target));

    internal static string Start(GameItem machine, GameGridInventory target, int requested)
    {
        if (IsRunning) return CurrentStatus;
        if (!ModEntry.Ready(out string reason, true) || !CanUseMachine(machine, out reason)) return reason;
        TryParts(machine, out var slot, out _, out _);
        var source = slot!.childItem;
        if (!CanUseSource(machine, source, out reason)) return reason;
        var recipe = SelectedRecipe(machine);
        if (recipe == null || recipe.requiredVolume <= 0) return "请先用原生“配置”按钮选择制药配方。";
        if (!MachineChemFinisher.CanOutput(machine, slot))
            return recipe.isRequireDry && WaterHelper.IsWet(source)
                ? "原料仍含可挥发液体，原生配方要求干燥；请先处理后再生成。" : "原料不足一份，不能批量生成。";
        if (!CanUseTarget(machine, target)) return "输出页被移走、原生封锁或不属于可用储物页，请重新选择。";
        int count = Math.Min(requested, AvailableCount(source, recipe));
        if (count <= 0) return "请输入大于 0 的生成数量。";
        int ticket = ++generation;
        var store = PlayerStore.Instance;
        var entry = EmporiumEntry.Instance;
        int saveSlot = store.saveSlotId;
        int loss = MachineChemFinisher.GetLoss(machine);
        IsRunning = true;
        CurrentStatus = $"正在批量生成 {recipe.displayName}：0/{count}。按 Esc 可停止。";
        try
        {
            if (ModHelper.StartCoroutine(ModEntry.Id, Run(ticket, store, entry, saveSlot, machine, source,
                    target, recipe.identifier, recipe.requiredVolume, loss, count)) == null)
            { IsRunning = false; return "未能启动批量任务，原料未改变。"; }
        }
        catch { IsRunning = false; throw; }
        return CurrentStatus;
    }

    internal static void Cancel()
    {
        generation++;
        IsRunning = false;
        CurrentStatus = "批量制药已停止；成品保留，剩余原料留在容器中。";
    }

    private static IEnumerator Run(int ticket, PlayerStore store, EmporiumEntry entry, int saveSlot,
        GameItem machine, GameItem source, GameGridInventory target, string recipeId, int volume, int loss, int count)
    {
        yield return null;
        int made = 0;
        string stop = "已完成";
        try
        {
            while (made < count)
            {
                if (ticket != generation || !ModEntry.Enabled || !ReferenceEquals(PlayerStore.Instance, store) ||
                    !ReferenceEquals(EmporiumEntry.Instance, entry) || store.saveSlotId != saveSlot)
                { stop = "场景或存档已变化"; break; }
                if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Escape)) { stop = "已取消"; break; }
                if (!ModEntry.Ready(out stop, true) || !CanUseMachine(machine, out stop) || !CanUseSource(machine, source, out stop)) break;
                var current = SelectedRecipe(machine);
                if (current?.identifier != recipeId || current.requiredVolume != volume || MachineChemFinisher.GetLoss(machine) != loss)
                { stop = "配方或机器升级已变化，请重新开始"; break; }
                if (!CanUseTarget(machine, target)) { stop = "输出页原生封锁或不再可用"; break; }
                TryParts(machine, out var slot, out _, out _);
                if (!MachineChemFinisher.CanOutput(machine, slot!)) { stop = "剩余原料不足或不再符合原生干燥条件"; break; }
                try
                {
                    var result = ProduceOne(machine, source, target, current, loss);
                    if (result != ChemBatchTransaction.Result.Produced)
                    { stop = result == ChemBatchTransaction.Result.NoSpace ? "目标页空间不足或分类限制不允许；可清空成品区或改选更大的储物页继续。最后一份未消耗原料" : "成品放置失败，最后一份原料已恢复"; break; }
                }
                catch (Exception ex)
                {
                    ModEntry.Log?.Error(ex);
                    stop = "原生生产出错，已停止；请查看日志：" + ex.Message;
                    break;
                }
                made++;
                CurrentStatus = $"批量制药：{made}/{count}，按 Esc 可停止。";
                NativeChemicalBatch.UpdateStatus(CurrentStatus);
                // Native factories, liquid callbacks and inventory validation all stay on the main thread, one item per frame.
                yield return null;
            }
        }
        finally
        {
            if (ticket == generation)
            {
                IsRunning = false;
                CurrentStatus = $"批量制药：{made}/{count}，{(made == count ? "已完成" : stop)}。剩余原料留在容器中。";
                if (ModEntry.Enabled) ControlPanel.Status(CurrentStatus);
                NativeChemicalBatch.UpdateStatus(CurrentStatus);
            }
        }
    }

    private static ChemBatchTransaction.Result ProduceOne(GameItem machine, GameItem source, GameGridInventory target,
        ChemFinisherRecipe recipe, int loss)
    {
        ItemStateSnapshot? snapshot = null;
        SlotMarker? slot = null;
        GameItem? produced = null;
        int[]? expected = null;
        var result = ChemBatchTransaction.One(() => DirectoryMaster.Item(recipe.identifier),
            item =>
            {
                if (item.identifier != recipe.identifier || item.unitCount != 1 || item.contentWindow != null ||
                    !item.IsTag(ChemicalHelper.CHEM_ITEM_TAG) || !item.IsTag(ChemicalProductHelper.CHEMICAL_PRODUCT_TAG) ||
                    StoryProtection.IsProtected(item) || !GeneralHelper.IsItemOwned(item))
                    throw new InvalidOperationException("配方工厂返回了非预期成品，已拒绝处理。");
                ChemicalHelper.RemoveAllLiquid(item);
                int[] portions = WaterHelper.SplitProportionally(WaterHelper.GetLiquidParts(source), recipe.requiredVolume);
                ChemicalHelper.AddLiquidToChemFromList(item, Liquid.Liquids.Select((liquid, i) =>
                    new KeyValuePair<string, int>(liquid.identifier, portions[i])).ToList());
                MachineChemFinisher.IntroduceImpurity(item, loss);
                ChemicalProductHelper.GradeProduct(item);
                expected = WaterHelper.GetLiquidParts(item);
            },
            item => (slot = FindOutputSlot(machine, target, item)) != null,
            () => snapshot = new ItemStateSnapshot(source),
            item =>
            {
                ChemicalHelper.RemoveAllLiquid(item);
                // Same production sequence as the native output button: real transfer, machine contamination, native grading.
                ChemicalHelper.TransferLiquidToChem(source, item, recipe.requiredVolume);
                MachineChemFinisher.IntroduceImpurity(item, loss);
                ChemicalProductHelper.GradeProduct(item);
                if (expected == null || !expected.SequenceEqual(WaterHelper.GetLiquidParts(item)))
                    throw new InvalidOperationException("原料或原生回调改变了本次配比，已取消最后一份。");
            },
            item => slot != null && IsDirectSlot(target, item, slot) && slot.targetItem == null &&
                slot.TryAcceptOnce(1) == 1 && ReferenceEquals(item.parentInventory, target) &&
                target.childItems.Contains(produced = item),
            () =>
            {
                snapshot!.Restore(source);
                SpriteHelper.UpdateWaterContainerSprite(source);
                WaterHelper.UpdateWaterBottleSound(source);
                source.ValidateShapeState();
            },
            item =>
            {
                item.Destroy();
                if (item.parentInventory != null) throw new InvalidOperationException("未能移除失败成品，未恢复原料以免复制；请保留日志。");
            });
        if (result == ChemBatchTransaction.Result.Produced && produced != null)
        {
            try { AchievementTracker.Instance?.CheckLabPure(produced); }
            catch (Exception ex) { ModEntry.Log?.Warning("成品已生成，但成就检查失败：" + ex.Message); }
        }
        return result;
    }
}
