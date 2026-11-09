using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: NativeAudit <game-managed-dir> <mod-folder>");
    return 2;
}
string managed = Path.GetFullPath(args[0]), folder = Path.GetFullPath(args[1]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    string candidate = Path.Combine(managed, name.Name + ".dll");
    return File.Exists(candidate) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(candidate) : null;
};
// This is an isolated offline process, never a running game or live loader.
// Supply the same set the game's startup collects from its own Managed folder.
var gameNames = System.IO.Directory.GetFiles(managed, "*.dll").Select(Path.GetFileNameWithoutExtension).Cast<string>().ToArray();
typeof(ModLoader).GetField("gameAssemblyNames", BindingFlags.Static | BindingFlags.NonPublic)!
    .SetValue(null, new HashSet<string>(gameNames, StringComparer.OrdinalIgnoreCase));
var parsed = ModLoader.ParseManifestForTools(folder, out string manifestProblem);
if (parsed == null) throw new InvalidDataException("Native manifest parser: " + manifestProblem);
Console.WriteLine($"Native manifest parser: ID={parsed.ID}, version={parsed.ModVersion}");
var manifest = XDocument.Load(Path.Combine(folder, "manifest.xml"));
string[] dlls = manifest.Root!.Element("Assemblies")!.Elements("Assembly").Select(e => Path.Combine(folder, e.Value)).ToArray();
var result = ModScanner.Scan(dlls, gameNames, folder, localFolder: true);
Console.WriteLine($"Native ModScanner: methods={result.MethodsScanned}, refused={result.Refused}, hardFlags={result.HardFlags.Count}, capabilities=[{string.Join(",", result.Capabilities)}], pending={result.Pending.Count}");
if (result.Error != null) Console.WriteLine("ERROR: " + result.Error);
foreach (var finding in result.HardFlags.Concat(result.Findings).Concat(result.Pending)) Console.WriteLine(finding.ToString());
string[] declared = manifest.Root.Element("Capabilities")?.Elements("Capability").Select(e => e.Value).ToArray() ?? Array.Empty<string>();
var missing = result.Capabilities.Except(declared).ToArray();
if (missing.Length > 0) Console.WriteLine("Undeclared capabilities: " + string.Join(",", missing));
// Independently resolve every declared Harmony target without applying patches or initializing Unity.
int patches = 0;
int injections = 0;
const BindingFlags AllMembers = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
Type ValueType(Type type) => type.IsByRef ? type.GetElementType()! : type;
void ValidateInjections(Type patchType, MethodBase original)
{
    foreach (var patch in patchType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
        .Where(m => m.Name is "Prefix" or "Postfix" or "Finalizer"))
    {
        foreach (var parameter in patch.GetParameters())
        {
            string name = parameter.Name!;
            Type? expected = null;
            if (name == "__instance") expected = original.DeclaringType;
            else if (name.StartsWith("___", StringComparison.Ordinal))
            {
                FieldInfo? field = null;
                for (Type? type = original.DeclaringType; type != null && field == null; type = type.BaseType)
                    field = type.GetField(name.Substring(3), AllMembers);
                expected = field?.FieldType ?? throw new MissingFieldException(original.DeclaringType!.FullName, name.Substring(3));
            }
            else if (name.StartsWith("__", StringComparison.Ordinal) && int.TryParse(name.Substring(2), out int index))
            {
                var args = original.GetParameters();
                if (index < 0 || index >= args.Length) throw new InvalidDataException($"{patchType.Name}: no native argument {index}");
                expected = args[index].ParameterType;
            }
            else if (!name.StartsWith("__", StringComparison.Ordinal))
                expected = original.GetParameters().SingleOrDefault(p => p.Name == name)?.ParameterType
                    ?? throw new InvalidDataException($"{patchType.Name}: no native parameter named {name}");
            if (expected != null)
            {
                if (!ValueType(parameter.ParameterType).IsAssignableFrom(ValueType(expected)))
                    throw new InvalidDataException($"{patchType.Name}.{patch.Name}: {name} expects {expected}, got {parameter.ParameterType}");
                injections++;
            }
        }
    }
}
foreach (var path in dlls)
{
    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
    foreach (var type in assembly.GetTypes())
    {
        foreach (var attribute in type.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch"))
        {
            var constructor = attribute.ConstructorArguments;
            if (constructor.Count >= 2 && constructor[0].Value is Type target && constructor[1].Value is string method)
            {
                MethodBase? original;
                if (constructor.Count >= 3 && constructor[2].Value is IReadOnlyCollection<CustomAttributeTypedArgument> signature)
                    original = target.GetMethod(method, AllMembers, null, signature.Select(a => (Type)a.Value!).ToArray(), null);
                else original = target.GetMethods(AllMembers).SingleOrDefault(m => m.Name == method);
                if (original == null) throw new MissingMethodException(target.FullName, method);
                ValidateInjections(type, original);
                patches++;
            }
        }
        var targets = type.GetMethod("TargetMethods", BindingFlags.Static | BindingFlags.NonPublic);
        if (targets != null)
            foreach (var method in (IEnumerable<MethodBase>)targets.Invoke(null, null)!)
            {
                if (method == null) throw new MissingMethodException(type.FullName + " unresolved TargetMethods");
                ValidateInjections(type, method);
                patches++;
            }
    }
}
Console.WriteLine($"Resolved Harmony targets: {patches}; validated parameter/field injections: {injections}; no patches applied and no game started.");
return result.Refused || result.Error != null || result.HardFlags.Count > 0 || result.Pending.Count > 0 || missing.Length > 0 ? 1 : 0;
