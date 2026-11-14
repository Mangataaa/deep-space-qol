using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using HarmonyLib;
using TMPro;

namespace DeepSpaceQoL;

/// <summary>Lets native attraction sources finish rolling without changing daily generation.</summary>
internal static class NativeDailyVisitors
{
    internal static bool PatchApplied { get; private set; }
    private const string TooltipId = "deep_space_qol.daily_expected_visitors";
    private static readonly Func<string> TooltipProvider = Tooltip;
    private static TextMeshProUGUI? title;
    private static string originalTitle = "", appliedTitle = "";

    // Called only from the native source loop, so changing the option never rerolls a saved queue.
    internal static bool ShouldContinueExpectedRolls(int successfulSources, int nativeQuota) =>
        ModEntry.Enabled && ModEntry.ExpectedVisitorsEnabled || successfulSources < nativeQuota;

    internal static string Describe() => !PatchApplied
        ? "未匹配当前版本的普通顾客排程，保留原版规则；开关暂不生效。"
        : "次日排程时，所有预期普通顾客来源各按原概率判定一次，不再达到原普通名额就停止。"
          + "成功来源不足原名额时沿用原版随机补足；剧情、特殊来客、教程与最终去重照旧。"
          + "例如基础名额为4、有13个不同来源时，通常可有4–13个普通来客，但同名去重、通缉占位等原版规则仍可能减少人数。"
          + "勾选不会重抽今天的顾客，也不会保证每个来源每天都来。";

    internal static void Refresh()
    {
        var label = ClienteleUIManager.Instance?.normalCustomerTitleTMP;
        if (title != label) RestoreTitle();
        if (!ModEntry.Enabled || !ModEntry.ExpectedVisitorsEnabled || !PatchApplied)
        { Dispose(); return; }
        if (label == null) return;
        title = label;
        if (label.text != appliedTitle) originalTitle = label.text;
        appliedTitle = FormatTitle(originalTitle);
        if (label.text != appliedTitle) label.text = appliedTitle;
        TMPIconHover.CustomLinks[TooltipId] = TooltipProvider;
    }

    internal static void Dispose()
    {
        RestoreTitle();
        if (TMPIconHover.CustomLinks.TryGetValue(TooltipId, out var provider) && provider == TooltipProvider)
            TMPIconHover.CustomLinks.Remove(TooltipId);
    }

    private static void RestoreTitle()
    {
        if (title != null && title.text == appliedTitle) title.text = originalTitle;
        title = null; originalTitle = appliedTitle = "";
    }

    internal static string FormatTitle(string nativeTitle)
    {
        // Read the already-rendered native quota: do not call either RNG-consuming getter.
        var quota = Regex.Match(nativeTitle ?? "", @"[（(]\s*(\d+)\s*[)）]");
        return "普通顾客基础名额" + (quota.Success ? "（" + quota.Groups[1].Value + "）" : "")
            + " <link=\"" + TooltipId + "\"><sprite name=\"info\" tint=1></link>";
    }

    private static string Tooltip() => LocHelper.GetLocalizedUI("ui_clientele_normal_tooltip")
        + (ModEntry.Enabled && ModEntry.ExpectedVisitorsEnabled && PatchApplied
            ? "\n\n【深空管家：逐项概率模式】\n标题数字是原版随机补足的基础名额，不是来客总上限。"
              + "下列来源将在次日日程中各按标注概率判定一次，仍受原版有效性与同名去重规则约束；今天已排好的顾客不变。"
            : "");

    [HarmonyPatch(typeof(ClienteleUIManager), nameof(ClienteleUIManager.RefreshUI))]
    private static class CustomerPredictionTitle
    {
        private static void Postfix() => Refresh();
    }

    [HarmonyPatch(typeof(StoreClientManager), nameof(StoreClientManager.HandleNormalClient))]
    private static class ExpectedSourceQuota
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = instructions.ToList();
            try
            {
                if (TryRewrite(original, out var rewritten, out string reason))
                {
                    PatchApplied = true;
                    return rewritten;
                }
                ModEntry.Log?.Warning("每日预期顾客补丁未应用，保留原版排程：" + reason);
            }
            catch (Exception ex)
            {
                ModEntry.Log?.Warning("每日预期顾客补丁检查失败，保留原版排程：" + ex.Message);
            }
            PatchApplied = false;
            return original;
        }
    }

    // Match native semantic anchors as well as the quota branch. On a game update or a
    // conflicting transpiler, leave the whole method untouched instead of guessing a branch.
    internal static bool TryRewrite(IReadOnlyList<CodeInstruction> source,
        out List<CodeInstruction> rewritten, out string reason)
    {
        rewritten = source.ToList();
        reason = "原版结构不匹配";
        var getQuota = AccessTools.Method(typeof(StoreClientManager), nameof(StoreClientManager.GetNormalCustomerCount));
        var predictions = AccessTools.Method(typeof(ShowcaseHelper), nameof(ShowcaseHelper.GetDisplayCasePrediction));
        var clearPicks = AccessTools.Method(typeof(ShowcaseHelper), nameof(ShowcaseHelper.ClearPendingSupplierPicks));
        var roll = AccessTools.Method(typeof(RNG), nameof(RNG.Roll), new[] { typeof(int) });
        var gunPity = AccessTools.Method(typeof(ShowcaseHelper), nameof(ShowcaseHelper.OnGunBuyersRolled));
        var appraisalPity = AccessTools.Method(typeof(AppraisalRankHelper), nameof(AppraisalRankHelper.OnCustomerRolled));
        var pickClient = AccessTools.Method(typeof(StoreClientManager), nameof(StoreClientManager.PickClient));
        var updateTracker = AccessTools.Method(typeof(StoreClientManager), nameof(StoreClientManager.UpdateTracker));
        if (!SingleCall(source, getQuota, out int quotaCall) ||
            !SingleCall(source, predictions, out int predictionsCall) ||
            !SingleCall(source, clearPicks, out int clearCall) ||
            !SingleCall(source, roll, out int rollCall) ||
            !SingleCall(source, gunPity, out int gunCall) ||
            !SingleCall(source, appraisalPity, out int appraisalCall) ||
            !SingleCall(source, pickClient, out int pickCall) ||
            !SingleCall(source, updateTracker, out int trackerCall))
        { reason = "原版调用锚点缺失或不唯一"; return false; }
        if (quotaCall + 3 >= source.Count ||
            !Local(source[quotaCall + 1], true, out int quotaLocal) ||
            source[quotaCall + 2].opcode != OpCodes.Ldc_I4_0 ||
            !Local(source[quotaCall + 3], true, out int successfulLocal) ||
            successfulLocal == quotaLocal ||
            !(trackerCall < quotaCall && quotaCall < predictionsCall && predictionsCall < clearCall &&
              clearCall < rollCall && rollCall < gunCall && gunCall < appraisalCall && appraisalCall < pickCall))
        { reason = "原版配额、计数器或调用顺序不匹配"; return false; }

        var candidates = new List<int>();
        for (int i = clearCall + 1; i + 5 < gunCall; i++)
        {
            if (!Local(source[i], false, out int left) || left != successfulLocal ||
                !Local(source[i + 1], false, out int right) || right != quotaLocal ||
                source[i + 2].opcode != OpCodes.Blt && source[i + 2].opcode != OpCodes.Blt_S ||
                source[i + 2].operand is not Label ||
                source[i + 3].opcode != OpCodes.Leave && source[i + 3].opcode != OpCodes.Leave_S ||
                source[i + 3].operand is not Label ||
                Equals(source[i + 2].operand, source[i + 3].operand) ||
                !Local(source[i + 4], false, out _) || i + 5 != rollCall)
                continue;
            candidates.Add(i + 2);
        }
        if (candidates.Count != 1)
        { reason = "预期来源的配额分支缺失或不唯一"; return false; }

        // The unchanged fill loop must still subtract this exact successful-source count
        // from the native quota. In expanded mode a negative result naturally adds none.
        int fills = 0;
        for (int i = appraisalCall + 1; i + 2 < pickCall; i++)
            if (Local(source[i], false, out int left) && left == quotaLocal &&
                Local(source[i + 1], false, out int right) && right == successfulLocal &&
                source[i + 2].opcode == OpCodes.Sub) fills++;
        if (fills != 1)
        { reason = "原版随机补足分支不匹配"; return false; }

        int branch = candidates[0];
        rewritten = source.Select(Clone).ToList();
        var originalBranch = rewritten[branch];
        rewritten[branch] = new CodeInstruction(originalBranch)
        {
            opcode = OpCodes.Call,
            operand = AccessTools.Method(typeof(NativeDailyVisitors), nameof(ShouldContinueExpectedRolls))
        };
        rewritten.Insert(branch + 1, new CodeInstruction(OpCodes.Brtrue, originalBranch.operand));
        reason = "仅替换预期来源的配额判断";
        return true;
    }

    private static CodeInstruction Clone(CodeInstruction instruction) => new(instruction);

    private static bool SingleCall(IReadOnlyList<CodeInstruction> source, MethodInfo? target, out int index)
    {
        index = -1;
        if (target == null) return false;
        for (int i = 0; i < source.Count; i++)
            if ((source[i].opcode == OpCodes.Call || source[i].opcode == OpCodes.Callvirt) && Equals(source[i].operand, target))
            { if (index >= 0) return false; index = i; }
        return index >= 0;
    }

    private static bool Local(CodeInstruction instruction, bool store, out int index)
    {
        var op = instruction.opcode;
        index = op == (store ? OpCodes.Stloc_0 : OpCodes.Ldloc_0) ? 0
            : op == (store ? OpCodes.Stloc_1 : OpCodes.Ldloc_1) ? 1
            : op == (store ? OpCodes.Stloc_2 : OpCodes.Ldloc_2) ? 2
            : op == (store ? OpCodes.Stloc_3 : OpCodes.Ldloc_3) ? 3 : -1;
        if (index >= 0) return true;
        if (op != (store ? OpCodes.Stloc : OpCodes.Ldloc) && op != (store ? OpCodes.Stloc_S : OpCodes.Ldloc_S)) return false;
        index = instruction.operand switch
        {
            LocalBuilder local => local.LocalIndex,
            LocalVariableInfo local => local.LocalIndex,
            byte value => value,
            short value => value,
            int value => value,
            _ => -1
        };
        return index >= 0;
    }
}
