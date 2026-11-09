using DeepSpaceQoL.Core;
using Newtonsoft.Json;

int checks = 0;
void Check(bool test, string label) { checks++; if (!test) throw new Exception(label); }
Check(CategoryMatcher.Matches("食物", "apple", new[] { "FOOD" }), "Native food category");
Check(!CategoryMatcher.Matches("水", "apple", new[] { "FOOD" }), "Food not water");
Check(CategoryMatcher.Matches("日用品", "bottle", new[] { "HOUSEHOLD_GOOD" }), "Empty bottle native household group");
Check(CategoryMatcher.Matches("食物,水", "bottle", new[] { "WATER" }), "Categories are OR");
Check(CategoryMatcher.Matches("tag:FOOD", "apple", new[] { "food" }), "Case insensitive raw tag");
Check(CategoryMatcher.Matches("id:water_tank", "water_tank", Array.Empty<string>()), "Exact ID");
Check(!CategoryMatcher.Matches("id:water", "water_tank", new[] { "WATER" }), "No fuzzy ID");
Check(!CategoryMatcher.Matches("不认识的分类", "water", new[] { "WATER" }), "Unknown labels do not silently match");
Check(!CategoryMatcher.Matches("", "water", new[] { "WATER" }), "Unlabeled is not a category match");
Check(CategoryMatcher.Tokens("食物， 水；食物/日用品|tag:FOOD").Length == 4, "Chinese separators and deduplication");
var policy = new StoragePolicy();
Check(policy.IsValid() && !policy.IsSaleProtected(), "Default unrestricted");
policy.Page(1).SaleLocked = true;
policy.Page(1).Label = "食物专用";
policy.Page(1).Categories = "食物,水";
policy.Page(1).Restrict = true;
Check(policy.IsSaleProtected(1), "Locked tab protected");
Check(!policy.IsSaleProtected(0), "Other tab stays unlocked");
Check(policy.IsSaleProtected(), "Selling whole box cannot bypass a locked tab");
Check(!policy.IsSaleProtected(9), "New tab defaults unlocked");
policy.ContainerLocked = true;
Check(policy.IsSaleProtected(0) && policy.IsSaleProtected(9), "Whole box protects every tab");
string json = JsonConvert.SerializeObject(policy);
var restored = JsonConvert.DeserializeObject<StoragePolicy>(json)!;
Check(restored.IsValid() && restored.ContainerLocked, "Native serializer round-trip container lock");
Check(restored.Page(1).SaleLocked && restored.Page(1).Restrict, "Round-trip flags");
Check(restored.Page(1).Label == "食物专用" && restored.Page(1).Categories == "食物,水", "Round-trip Chinese names/categories");
Check(!restored.Pages.ContainsKey(0), "Reading absent tab lock does not mutate dictionary");
Check(new StoragePolicy { Version = 2 }.IsSaleProtected(), "Unknown version fails closed");
Check(new StoragePolicy { Pages = null! }.IsSaleProtected(), "Null pages fail closed");
Check(new StoragePolicy { Pages = new() { [-1] = new() } }.IsSaleProtected(), "Invalid tab fails closed");
Check(new StoragePolicy { Pages = new() { [0] = null! } }.IsSaleProtected(), "Null rule fails closed");
var root = new StoragePolicy(); root.Page(2).SaleLocked = true;
var inner = new StoragePolicy();
Check(new[] { (root, 2), (inner, 0) }.Any(p => p.Item1.IsSaleProtected(p.Item2)), "Nested unlocked box inherits locked outer tab");
Check(!new[] { (root, 0), (inner, 0) }.Any(p => p.Item1.IsSaleProtected(p.Item2)), "Unrelated outer tab does not block inner contents");
var oldExpression = "食物,日用品,tag:WATER,id:water_tank,*,CUSTOM_TAG";
var explicitTokens = CategoryMatcher.ExplicitTokens(oldExpression);
Check(explicitTokens.Contains("tag:HOUSEHOLD_GOOD") && explicitTokens.Contains("tag:FOOD"), "Old aliases become actual tag choices");
Check(explicitTokens.Contains("id:water_tank") && explicitTokens.Contains("*") && explicitTokens.Contains("tag:CUSTOM_TAG"), "Checklist preserves exact IDs, wildcard and custom tags");
foreach (string expression in new[] { "食物,水", "日用品", "tag:FOOD,food", "id:apple,tag:WATER", "全部", "CUSTOM_TAG", "" })
    foreach (string tag in new[] { "FOOD", "BREVAGE", "HOUSEHOLD_GOOD", "CLOTHING", "WATER", "CUSTOM_TAG", "MODULE" })
        Check(CategoryMatcher.Matches(expression, "apple", new[] { tag }) ==
            CategoryMatcher.Matches(string.Join(",", CategoryMatcher.ExplicitTokens(expression)), "apple", new[] { tag }), "Native checklist migration preserves matching semantics");
var deselected = CategoryMatcher.ExplicitTokens("食物,水").Where(t => !string.Equals(t, "tag:WATER", StringComparison.OrdinalIgnoreCase));
Check(!CategoryMatcher.Matches(string.Join(",", deselected), "water", new[] { "WATER" }), "Unchecking native category removes match");
Check(CategoryMatcher.Matches(string.Join(",", deselected), "apple", new[] { "FOOD" }), "Unchecking water preserves food");
Console.WriteLine($"Storage policy: {checks} checks passed.");
