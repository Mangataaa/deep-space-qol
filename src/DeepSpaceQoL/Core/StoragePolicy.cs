using System;
using System.Collections.Generic;
using System.Linq;

namespace DeepSpaceQoL.Core;

public sealed class PageRule
{
    public string Label { get; set; } = "";
    public string Categories { get; set; } = "";
    public bool Restrict { get; set; }
    public bool SaleLocked { get; set; }
}

public sealed class StoragePolicy
{
    public int Version { get; set; } = 1;
    public bool ContainerLocked { get; set; }
    public Dictionary<int, PageRule> Pages { get; set; } = new();
    public bool IsValid() => Version == 1 && Pages != null && Pages.All(p => p.Key >= 0 && p.Value != null);
    public bool IsSaleProtected(int? pageIndex = null) => !IsValid() || ContainerLocked ||
        (pageIndex.HasValue ? Pages.TryGetValue(pageIndex.Value, out var p) && p.SaleLocked : Pages.Values.Any(p => p.SaleLocked));
    public PageRule Page(int index)
    {
        if (!Pages.TryGetValue(index, out var rule)) Pages[index] = rule = new PageRule();
        return rule;
    }
}

/// <summary>Pure matching rules, shared with the executable test harness.</summary>
public static class CategoryMatcher
{
    private static readonly Dictionary<string, string[]> Groups = new(StringComparer.OrdinalIgnoreCase)
    {
        ["食物"] = new[] { "FOOD", "PROCESSED_FOOD", "TREAT", "BREVAGE" },
        ["水"] = new[] { "WATER" },
        ["日用品"] = new[] { "HOUSEHOLD_GOOD", "PERSONAL_CARE", "CLOTHING" },
        ["工具"] = new[] { "TOOL", "ENGINEERING", "ENGINEERING_SUPPLIES" },
        ["医疗"] = new[] { "MEDICAL", "MEDICAL_SUPPLIES", "MEDICINE" },
        ["武器"] = new[] { "FIREARM", "MELEE_WEAPON", "WEAPON", "GUN_MOD", "AMMUNITION" },
        ["化学"] = new[] { "CHEMICAL_SUPPLIES", "CHEMICAL_PRODUCT_TAG", "WATER_PURIFICATION_SUPPLY", "NARCOTIC" },
        ["贵重品"] = new[] { "LUXURY_ITEM", "JEWELRY", "VALUABLE" },
        ["材料"] = new[] { "RAW_MATERIAL", "MATERIAL", "SCRAP", "CRAFTING_MATERIAL" },
        ["电子"] = new[] { "ELECTRONICS", "ELECTRONIC", "GADGET" }
    };
    public static IReadOnlyList<string> Names => Groups.Keys.ToArray();
    public static string[] Tokens(string? expression) => (expression ?? "")
        .Split(new[] { ',', '，', ';', '；', '/', '\n', '|' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(t => t.Trim()).Where(t => t.Length != 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static bool HasCategories(PageRule rule) => Tokens(rule.Categories).Length > 0;
    /// <summary>Equivalent explicit tokens for the native checklist; preserves ID/custom/wildcard filters.</summary>
    public static string[] ExplicitTokens(string? expression) => Tokens(expression).SelectMany(token =>
        token == "全部" || token == "*" ? new[] { "*" } :
        Groups.TryGetValue(token, out var group) ? group.Select(tag => "tag:" + tag) :
        token.StartsWith("id:", StringComparison.OrdinalIgnoreCase) ? new[] { "id:" + token.Substring(3) } :
        new[] { "tag:" + (token.StartsWith("tag:", StringComparison.OrdinalIgnoreCase) ? token.Substring(4) : token) })
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public static bool Matches(string? expression, string itemId, IEnumerable<string> tags)
    {
        var set = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);
        foreach (var token in Tokens(expression))
        {
            if (token == "全部" || token == "*") return true;
            if (token.StartsWith("id:", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(itemId, token.Substring(3), StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (Groups.TryGetValue(token, out var group))
            {
                if (group.Any(set.Contains)) return true;
            }
            else if (set.Contains(token.StartsWith("tag:", StringComparison.OrdinalIgnoreCase) ? token.Substring(4) : token)) return true;
        }
        return false;
    }
}
