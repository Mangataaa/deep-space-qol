namespace DeepSpaceQoL;

/// <summary>Reads the same issue tags used by native confrontation; never exposes or mutates a feature.</summary>
internal static class AuthenticityPolicy
{
    internal enum Kind { Unsupported, Cigarette, Stamp, Injector }
    internal enum Verdict { Unknown, Genuine, Problem }
    internal readonly struct Result
    {
        internal readonly Kind Type;
        internal readonly Verdict Status;
        internal readonly int IssueIndex;
        internal readonly bool ClaimedGenuine;
        internal readonly string Description;
        internal Result(Kind type, Verdict status, int issue, bool claimed, string description)
        { Type = type; Status = status; IssueIndex = issue; ClaimedGenuine = claimed; Description = description; }
    }

    internal static Kind TypeOf(GameItem? item) => Has(item, "CIGARETTE_TAG") ? Kind.Cigarette :
        Has(item, "FOOD_STAMP_TAG") ? Kind.Stamp : Has(item, "INS_INJECTOR_TAG") ? Kind.Injector : Kind.Unsupported;

    internal static Result Evaluate(GameItem? item)
    {
        var kind = TypeOf(item);
        if (item == null || kind == Kind.Unsupported) return new Result(kind, Verdict.Unknown, -1, false, "当前物品没有可用的真伪检查规则。");
        string category = kind == Kind.Cigarette ? "CATEGORY_GENUINE_CIGARETTE" : kind == Kind.Stamp ? "CATEGORY_GENUINE_STAMP" : "CATEGORY_GENUINE_INJECTOR";
        string genuine = kind == Kind.Cigarette ? "genuineCigarette" : kind == Kind.Stamp ? "genuineStamp" : "genuineInjector";
        // Match the native FindItemFeatureByCategory ordering. A disabled first feature
        // must not be skipped in favour of a different claim that OnConfront will not use.
        var feature = item.itemFeatures?.FirstOrDefault(f => f.category == category);
        if (feature?.isDisabled == true)
            return new Result(kind, Verdict.Unknown, -1, false, "真伪特征当前已停用，无法进行可靠鉴定。");
        var actual = feature?.realCondition?.identifier;
        var claimed = feature?.GetCurrentItemCondition()?.identifier;
        if (string.IsNullOrEmpty(actual) || string.IsNullOrEmpty(claimed) || feature?.useCondition != true)
            return new Result(kind, Verdict.Unknown, -1, false, "鉴定数据不完整，无法确认真伪。");
        string[] issues = kind == Kind.Cigarette ? new[] { "CIGARETTE_LETTERING_ISSUE", "CIGARETTE_COLOR_ISSUE", "CIGARETTE_SEAL_ISSUE", "CIGARETTE_LOGO_ISSUE", "CIGARETTE_BOX_ISSUE" } :
            kind == Kind.Stamp ? new[] { "STAMP_LETTERING_ISSUE", "STAMP_LOGO_ISSUE", "STAMP_SEAL_ISSUE", "STAMP_AMOUNT_ISSUE", "STAMP_CHIP_ISSUE" } :
            new[] { "ISSUE_INDICATOR", "ISSUE_LABEL", "ISSUE_SERIAL", "ISSUE_BODY", "ISSUE_COLOR" };
        string[] labels = kind == Kind.Cigarette ? new[] { "文字", "颜色", "封条", "标志", "包装" } :
            kind == Kind.Stamp ? new[] { "文字", "标志", "封条", "面额", "芯片" } : new[] { "状态指示", "标签", "序列号／召回", "瓶体", "颜色" };
        int first = Array.FindIndex(issues, tag => Has(item, tag));
        bool claimedGenuine = string.Equals(claimed, genuine, StringComparison.Ordinal);
        if (first >= 0)
            return new Result(kind, Verdict.Problem, first, claimedGenuine,
                "鉴定：有问题（" + string.Join("、", labels.Where((_, i) => Has(item, issues[i]))) + "）。");
        if (actual == genuine) return new Result(kind, Verdict.Genuine, -1, claimedGenuine, "鉴定：正品，未发现原生检查项目中的问题。");
        return new Result(kind, Verdict.Problem, -1, claimedGenuine, "鉴定：非正品或存在质量问题；没有可用于原生对质的具体问题标记。");
    }

    internal static bool Has(GameItem? item, string tag) => item?.modifiedState?.dict != null &&
        item.modifiedState.dict.TryGetValue(tag, out var state) && state.IsEnabled();
}
