using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using DeepSpaceQoL;

string managed = args.Length == 1 ? Path.GetFullPath(args[0]) : @"D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed";
AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(managed, name.Name + ".dll"))
    ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed, name.Name + ".dll")) : null;
Tests.Run();

static class Tests
{
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    private static GameItem Item(string tag, string category, string genuine, bool real, int flags, string[] issues)
    {
        var item = (GameItemElement)RuntimeHelpers.GetUninitializedObject(typeof(GameItemElement));
        item.identifier = "fixture"; item.state = new TagSystem(); item.modifiedState = item.state;
        item.modifiedState.dict[tag] = new TagState(tag, tag) { valueEnabled = true };
        item.itemFeatures = new List<ItemFeature> { new ItemFeature(category) { useCondition = true,
            fakeCondition = new ItemCondition { identifier = genuine }, realCondition = new ItemCondition { identifier = real ? genuine : "counterfeit" } } };
        for (int i = 0; i < issues.Length; i++) item.state.dict[issues[i]] = new TagState(issues[i], issues[i]) { valueEnabled = (flags & (1 << i)) != 0 };
        return item;
    }
    internal static void Run()
    {
        Check(AuthenticityPolicy.TypeOf(null) == AuthenticityPolicy.Kind.Unsupported, "null unsupported");
        var cases = new[] {
            ("CIGARETTE_TAG", "CATEGORY_GENUINE_CIGARETTE", "genuineCigarette", new[]{"CIGARETTE_LETTERING_ISSUE","CIGARETTE_COLOR_ISSUE","CIGARETTE_SEAL_ISSUE","CIGARETTE_LOGO_ISSUE","CIGARETTE_BOX_ISSUE"}),
            ("FOOD_STAMP_TAG", "CATEGORY_GENUINE_STAMP", "genuineStamp", new[]{"STAMP_LETTERING_ISSUE","STAMP_LOGO_ISSUE","STAMP_SEAL_ISSUE","STAMP_AMOUNT_ISSUE","STAMP_CHIP_ISSUE"}),
            ("INS_INJECTOR_TAG", "CATEGORY_GENUINE_INJECTOR", "genuineInjector", new[]{"ISSUE_INDICATOR","ISSUE_LABEL","ISSUE_SERIAL","ISSUE_BODY","ISSUE_COLOR"}) };
        foreach (var (tag, category, genuine, issues) in cases)
        {
            for (int flags = 0; flags < 32; flags++)
            {
                var item = Item(tag, category, genuine, flags == 0, flags, issues);
                var before = item.state.dict.ToDictionary(p => p.Key, p => p.Value.valueEnabled);
                var result = AuthenticityPolicy.Evaluate(item);
                Check((result.Status == AuthenticityPolicy.Verdict.Genuine) == (flags == 0), "no false genuine for issue flag combination");
                Check(result.ClaimedGenuine, "current condition is the native claim");
                if (flags != 0)
                {
                    Check(result.IssueIndex >= 0 && (flags & (1 << result.IssueIndex)) != 0, "selected native toggle maps to actual issue");
                    Check(InspectableHelper.IsConfrontCorrect(issues[result.IssueIndex], item), "native confrontation accepts selected reason");
                }
                Check(before.Count == item.state.dict.Count && before.All(p => item.state.dict[p.Key].valueEnabled == p.Value), "read-only tags unchanged");
                Check(item.itemFeatures[0].GetCurrentItemCondition()!.identifier == genuine, "inspection does not expose feature");
            }
            var missing = Item(tag, category, genuine, true, 0, issues);
            missing.itemFeatures.Clear();
            Check(AuthenticityPolicy.Evaluate(missing).Status == AuthenticityPolicy.Verdict.Unknown, "missing feature is unknown");
            missing = Item(tag, category, genuine, true, 0, issues);
            missing.itemFeatures[0].realCondition = null;
            Check(AuthenticityPolicy.Evaluate(missing).Status == AuthenticityPolicy.Verdict.Unknown, "missing actual condition is unknown");
            missing.state.dict[issues[0]].valueEnabled = true;
            Check(AuthenticityPolicy.Evaluate(missing).Status == AuthenticityPolicy.Verdict.Unknown, "issue tag cannot make an incomplete feature safe to confront");
            var counterfeit = Item(tag, category, genuine, false, 0, issues);
            Check(AuthenticityPolicy.Evaluate(counterfeit).Status == AuthenticityPolicy.Verdict.Problem && AuthenticityPolicy.Evaluate(counterfeit).IssueIndex == -1, "missing concrete issue never invents an accusation");
            counterfeit.itemFeatures[0].fakeCondition = new ItemCondition { identifier = "counterfeit" };
            Check(!AuthenticityPolicy.Evaluate(counterfeit).ClaimedGenuine, "already disclosed counterfeit not treated as claimed genuine");
            var disabled = Item(tag, category, genuine, true, 0, issues);
            disabled.state.dict[tag].valueEnabled = false;
            Check(AuthenticityPolicy.TypeOf(disabled) == AuthenticityPolicy.Kind.Unsupported, "disabled kind tag ignored");
            var disabledFeature = Item(tag, category, genuine, true, 0, issues);
            disabledFeature.itemFeatures[0].isDisabled = true;
            disabledFeature.itemFeatures.Add(new ItemFeature(category) { useCondition = true, fakeCondition = new ItemCondition { identifier = genuine }, realCondition = new ItemCondition { identifier = genuine } });
            Check(AuthenticityPolicy.Evaluate(disabledFeature).Status == AuthenticityPolicy.Verdict.Unknown, "first native feature wins; never skip to a conflicting second feature");
        }
        Console.WriteLine($"Authenticity native checks passed: {checks}; actual native issue predicates, no dialogue or UI executed.");
    }
}
