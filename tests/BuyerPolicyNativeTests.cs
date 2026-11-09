using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using DeepSpaceQoL;

// Executes the installed game's real buyer predicates, with offline data-only fixtures.
// No constructors/actions/UI/Unity scene initialization or Harmony patches are invoked.
string managed = args.Length == 1 ? Path.GetFullPath(args[0]) : @"D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed";
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string path = Path.Combine(managed, name.Name + ".dll");
    return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};
NativeBuyerTests.Run();

static class NativeBuyerTests
{
    private static int checks;
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new InvalidOperationException(message); }

    private static StoreClient Client(params string[] interests)
    {
        var c = (StoreClient)RuntimeHelpers.GetUninitializedObject(typeof(StoreClient));
        c.clientFaction = "NONE";
        c.tag = new();
        c.clientBuyingTagList = interests.ToList();
        c.clientBuyingIdList = new();
        c.clientBlackTagList = new();
        c.clientBlackIdList = new();
        c.varietyTrackingList = new();
        return c;
    }

    private static GameItem Item(string id, params string[] tags)
    {
        var item = (GameItemElement)RuntimeHelpers.GetUninitializedObject(typeof(GameItemElement));
        item.identifier = id;
        item.unitCount = 1;
        item.unitValue = 10;
        item.itemTypes = tags.ToList();
        item.itemFeatures = new();
        item.state = new TagSystem();
        item.modifiedState = item.state;
        foreach (string tag in tags.Append("IS_OWNED_TAG"))
            item.state.dict[tag] = new TagState(tag, tag) { valueEnabled = true };
        return item;
    }

    internal static void Run()
    {
        var food = Item("food_a", "FOOD");
        var secondFood = Item("food_b", "FOOD");
        var tool = Item("tool_a", "TOOL");
        var foodBuyer = Client("FOOD");
        Check(BuyerStockPolicy.Accepts(foodBuyer, food), "Interested food must be accepted.");
        Check(!BuyerStockPolicy.Accepts(foodBuyer, tool), "Uninterested goods must be rejected.");
        var staged = BuyerStockPolicy.AcceptedStagedItems(foodBuyer, new[] { food, tool }).ToArray();
        Check(staged.Length == 1 && staged[0] == food, "Uninterested staged goods must not enter budget accounting.");
        foodBuyer.clientBlackTagList.Add("FOOD");
        Check(!BuyerStockPolicy.Accepts(foodBuyer, food), "Excluded category must override positive interest.");
        Check(!BuyerStockPolicy.AcceptedStagedItems(foodBuyer, new[] { food }).Any(), "Blacklisted staged goods must not enter totals.");
        foodBuyer.clientBlackTagList.Clear();
        foodBuyer.clientBlackIdList.Add("food_a");
        Check(!BuyerStockPolicy.Accepts(foodBuyer, food), "Blacklisted ID must be rejected.");
        foodBuyer.clientBlackIdList.Clear();
        foodBuyer.clientBuyingIdList.Add("tool_a");
        Check(BuyerStockPolicy.Accepts(foodBuyer, tool), "Native exact ID preference must still work.");

        var variety = Client("ALL_TYPE");
        variety.isRequireVariety = true;
        variety.varietyTrackingList.Add("food_a");
        Check(!BuyerStockPolicy.Accepts(variety, food), "Already purchased variety must be excluded across clicks/sales.");
        Check(BuyerStockPolicy.Accepts(variety, secondFood), "Another identifier in the same category remains eligible.");
        Check(BuyerStockPolicy.CheckCounter(variety, new[] { food, secondFood }) == BuyerStockPolicy.CounterIssue.None,
            "Previously purchased/unaccepted stock must not reserve a new variety.");
        Check(BuyerStockPolicy.AcceptedStagedItems(variety, new[] { food, secondFood }).SequenceEqual(new[] { secondFood }),
            "Previously purchased variety must not enter budget accounting.");
        variety.varietyTrackingList.Clear();
        var duplicate = Item("food_a", "FOOD");
        Check(BuyerStockPolicy.CheckCounter(variety, new[] { food, duplicate }) == BuyerStockPolicy.CounterIssue.DuplicateVariety,
            "Manually staged duplicate varieties must stop stocking before double counting.");
        Check(BuyerStockPolicy.CheckCounter(variety, new[] { food, secondFood }) == BuyerStockPolicy.CounterIssue.None,
            "Distinct IDs in the same category are distinct native varieties.");

        var stolen = Item("stolen_food", "FOOD", "STOLEN_TAG");
        var contraband = Item("illegal_food", "FOOD", "CONTRABAND_ITEM_TAG");
        var legacy = Item("legacy_illegal", "FOOD", "CONTRABAND");
        var security = Client("ALL_TYPE");
        security.isSecurity = true;
        security.noAcceptingContraband = true;
        Check(security.IsClientBuyingThisItem(stolen), "Regression fixture: native buyer accepts stolen goods before the counter callback.");
        Check(!BuyerStockPolicy.Accepts(security, stolen), "Security must reject stolen goods before placement.");
        Check(!security.IsClientBuyingThisItem(contraband), "Native no-contraband rule must reject contraband.");
        Check(!BuyerStockPolicy.Accepts(security, contraband), "Security contraband protection must hold.");
        Check(!BuyerStockPolicy.Accepts(security, legacy), "Security rejects legacy contraband tag too.");
        Check(BuyerStockPolicy.Accepts(security, food), "Lawful interested goods must remain eligible for security.");
        Check(BuyerStockPolicy.CheckCounter(security, new[] { food, stolen }) == BuyerStockPolicy.CounterIssue.SecurityRisk,
            "Preexisting stolen counter goods must stop stocking and warn.");
        Check(BuyerStockPolicy.AcceptedStagedItems(security, new[] { food, stolen, contraband }).SequenceEqual(new[] { food }),
            "Security-forbidden items must never enter automatic budget totals.");
        security.noAcceptingContraband = false;
        Check(security.IsClientBuyingThisItem(contraband), "Regression fixture: a native preference override can accept contraband.");
        Check(!BuyerStockPolicy.Accepts(security, contraband), "Security safety must survive native overrides.");
        security.isSecurity = false;
        security.clientFaction = StoreClient.FACTION_SECURITY;
        Check(!BuyerStockPolicy.Accepts(security, stolen), "Security faction alone must protect stolen goods.");
        security.clientFaction = "NONE";
        security.tag.Add("SECURITY");
        Check(!BuyerStockPolicy.Accepts(security, stolen), "Security tag alone must protect stolen goods.");

        var ordinary = Client("ALL_TYPE");
        Check(BuyerStockPolicy.Accepts(ordinary, stolen) && BuyerStockPolicy.Accepts(ordinary, contraband),
            "Non-security buyers retain their real native acceptance rules.");
        ordinary.noAcceptingContraband = true;
        Check(!BuyerStockPolicy.Accepts(ordinary, contraband), "Ordinary no-contraband buyer still refuses contraband.");
        Check(BuyerStockPolicy.CheckCounter(ordinary, new[] { food, duplicate }) == BuyerStockPolicy.CounterIssue.None,
            "Non-variety buyers may buy duplicate IDs.");
        var foreign = Item("foreign_illegal", "CONTRABAND_ITEM_TAG");
        foreign.state.dict["IS_OWNED_TAG"].valueEnabled = false;
        Check(BuyerStockPolicy.CheckCounter(security, new[] { foreign }) == BuyerStockPolicy.CounterIssue.None,
            "Customer-owned goods must not be treated as the player's offer.");
        Check(!BuyerStockPolicy.AcceptedStagedItems(ordinary, new[] { foreign }).Any(), "Non-owned goods must not enter offer totals.");
        Console.WriteLine($"Native buyer policy: {checks} checks passed using actual StoreClient predicates; no game started.");
    }
}
