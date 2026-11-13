using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

string managed = args.Length > 0 ? args[0] : @"D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed";
AssemblyLoadContext.Default.Resolving += (_, name) => {
    string p = Path.Combine(managed, name.Name + ".dll");
    return File.Exists(p) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(p) : null;
};
var type = Assembly.Load("DeepSpaceQoL").GetType("DeepSpaceQoL.NativeAugCustomer", true)!;
var flags = BindingFlags.NonPublic | BindingFlags.Static;
var predicate = type.GetMethod("ShouldMark", flags)!;
var formatter = type.GetMethod("FormatName", flags)!;
var suffix = (string)type.GetField("Suffix", flags)!.GetRawConstantValue()!;
int count = 0;
void Check(bool ok, string message) { count++; if (!ok) throw new Exception(message); }
bool Mark(StoreClient? c) => (bool)predicate.Invoke(null, new object?[] { c })!;
string Format(string name, StoreClient? c) => (string)formatter.Invoke(null, new object?[] { name, c })!;
Check(!Mark(null), "No customer must not be marked");
Check(Format("顾客", null) == "顾客", "No customer keeps original label");
foreach (bool isAug in new[] { false, true })
foreach (bool scanned in new[] { false, true })
foreach (StoreClient.ClientIntent intent in Enum.GetValues<StoreClient.ClientIntent>())
{
    var client = (StoreClient)RuntimeHelpers.GetUninitializedObject(typeof(StoreClient));
    client.displayName = "普通来客";
    client.isAug = isAug;
    client.isAugScanned = scanned;
    client.clientIntent = intent;
    client.isWanted = true; // Wanted status is not evidence of augmentation.
    Check(Mark(client) == isAug, "Only the native isAug flag identifies an augmented customer");
    string expected = client.displayName + (isAug ? suffix : "");
    Check(Format(client.displayName, client) == expected, "Name decoration follows true identity, including disguised buyers");
    Check(Format(expected, client) == expected, "Repeated refresh must not duplicate suffix");
    Check(client.displayName == "普通来客" && client.isAug == isAug && client.isAugScanned == scanned && client.clientIntent == intent,
        "Display hints must not change customer identity or investigation/story state");
}
Console.WriteLine($"CustomerHints native tests: {count} checks passed.");
