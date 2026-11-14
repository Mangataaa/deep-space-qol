using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using DeepSpaceQoL;
using HarmonyLib;

string managed = args.Length == 1 ? Path.GetFullPath(args[0]) : @"D:\SteamLibrary\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed";
AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(managed, name.Name + ".dll"))
    ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed, name.Name + ".dll")) : null;
Tests.Run();

static class Tests
{
    private static int checks;
    private static void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
    private static readonly MethodInfo Native = typeof(StoreClientManager).GetMethod(nameof(StoreClientManager.HandleNormalClient))!;

    internal static void Run()
    {
        var original = PatchProcessor.GetOriginalInstructions(Native);
        Check(NativeDailyVisitors.TryRewrite(original, out var patched, out string reason), "installed native method must match: " + reason);
        Check(patched.Count == original.Count + 1, "exactly one extra instruction");
        var helper = typeof(NativeDailyVisitors).GetMethod(nameof(NativeDailyVisitors.ShouldContinueExpectedRolls), BindingFlags.NonPublic | BindingFlags.Static)!;
        int changed = patched.FindIndex(c => Equals(c.operand, helper));
        Check(changed >= 0 && patched[changed].opcode == OpCodes.Call && patched[changed + 1].opcode == OpCodes.Brtrue, "one helper and conditional branch");
        Check(patched.Count(c => Equals(c.operand, helper)) == 1, "helper called once in native loop");
        for (int i = 0; i < original.Count; i++)
        {
            var copy = patched[i < changed ? i : i + 1];
            if (i == changed) copy = patched[changed];
            else Check(copy.opcode == original[i].opcode && Equals(copy.operand, original[i].operand), "all other native instructions unchanged");
            Check(copy.labels.SequenceEqual(original[i].labels), "branch labels preserved");
            Check(copy.blocks.SequenceEqual(original[i].blocks), "exception boundaries preserved");
        }
        Check(original[changed].opcode == OpCodes.Blt || original[changed].opcode == OpCodes.Blt_S, "input branch was not mutated");
        Check(Equals(patched[changed + 1].operand, original[changed].operand), "same native continue target");
        Check(!ReferenceEquals(patched[changed].labels, original[changed].labels), "copied label lists do not alias original");
        var transpile = typeof(NativeDailyVisitors).GetNestedType("ExpectedSourceQuota", BindingFlags.NonPublic)!
            .GetMethod("Transpiler", BindingFlags.NonPublic | BindingFlags.Static)!;
        var applied = ((IEnumerable<CodeInstruction>)transpile.Invoke(null, new object[] { original })!).ToList();
        Check(NativeDailyVisitors.PatchApplied && applied.Count == patched.Count, "actual Harmony transpiler marks successful installation");
        Check(NativeDailyVisitors.Describe().Contains("次日") && NativeDailyVisitors.Describe().Contains("不会重抽"), "management description explains activation timing");

        var broken = original.Select(c => new CodeInstruction(c)).ToList();
        broken.Add(new CodeInstruction(OpCodes.Call, typeof(StoreClientManager).GetMethod(nameof(StoreClientManager.GetNormalCustomerCount))));
        Reject(broken, "ambiguous quota call");
        broken = original.Select(c => new CodeInstruction(c)).ToList();
        broken[changed].opcode = OpCodes.Ble;
        Reject(broken, "changed quota comparison");
        broken = original.Select(c => new CodeInstruction(c)).ToList();
        broken[changed].operand = broken[changed + 1].operand;
        Reject(broken, "continue and exit targets must differ");
        broken = original.Select(c => new CodeInstruction(c)).ToList();
        int pity = broken.FindIndex(c => c.operand is MethodInfo m && m.Name == nameof(AppraisalRankHelper.OnCustomerRolled));
        broken[pity].opcode = OpCodes.Nop; broken[pity].operand = null;
        Reject(broken, "missing pity callback");
        broken = original.Select(c => new CodeInstruction(c)).ToList();
        int subtraction = broken.FindIndex(pity, c => c.opcode == OpCodes.Sub);
        broken[subtraction].opcode = OpCodes.Add;
        Reject(broken, "changed random-fill formula");
        var fallback = ((IEnumerable<CodeInstruction>)transpile.Invoke(null, new object[] { broken })!).ToList();
        Check(!NativeDailyVisitors.PatchApplied && fallback.Count == broken.Count, "actual Harmony transpiler retains native IL on mismatch");
        Check(NativeDailyVisitors.Describe().Contains("不生效"), "failed installation is visible in management description");
        transpile.Invoke(null, new object[] { original });

        Check(NativeDailyVisitors.FormatTitle("预期的普通顾客 (4) <link=\"ui_clientele_normal_tooltip\">info</link>").StartsWith("普通顾客基础名额（4）"), "native quota retained from rendered title");
        Check(NativeDailyVisitors.FormatTitle("预期的普通顾客（6）").StartsWith("普通顾客基础名额（6）"), "fullwidth title supported");
        Check(!NativeDailyVisitors.FormatTitle("unknown").Contains("（4）"), "never invent a quota for changed localization");
        Check(NativeDailyVisitors.FormatTitle("Normal customers (0)").Contains("（0）"), "zero native quota retained");

        // Emit the installed method's actual IL twice, replacing world/RNG calls with
        // deterministic managed stubs. Its native foreach, finally, fill and tutorial
        // branches execute; no Unity runtime, saves or live queues are touched.
        var nativeRun = Compile(false);
        var expandedRun = Compile(true);
        var all = World.Create(13, 4, 100);
        Execute(nativeRun, all, false);
        Check(all.RollChances.Count == 4 && all.Added.Count == 4, "native quota stops after four successes");
        var expanded = World.Create(13, 4, 100);
        Execute(expandedRun, expanded, true);
        Check(expanded.RollChances.Count == 13 && expanded.Added.Count == 13 && expanded.PickCalls == 0, "expanded mode evaluates all thirteen sources");
        Check(expanded.Added.All(c => c.attractedItemId == "source-" + c.identifier), "native attracted source IDs retained");
        Check(expanded.GunPityCalls == 1 && expanded.AppraisalPityCalls == 1 && expanded.PityPredictions == 13 && expanded.PitySuccesses.SetEquals(expanded.Added.Select(c => c.identifier)), "pity callbacks receive all predictions and successes once");
        Check(expanded.Events.Take(4).SequenceEqual(new[] { "tracker", "quota", "predictions", "clear" }), "native tracker/cache order retained");

        var sparse = World.Create(13, 4, 0);
        sparse.Predictions[11] = ("c11", 100, "source-c11"); sparse.Predictions[12] = ("c12", 100, "source-c12");
        Execute(expandedRun, sparse, true);
        Check(sparse.RollChances.Count == 13 && sparse.Added.Count == 4 && sparse.PickCalls == 2, "failed source rolls retain native random fill");
        Check(sparse.Added.Take(2).Select(c => c.identifier).SequenceEqual(new[] { "c11", "c12" }), "late sources are not starved");

        var wanted = World.Create(13, 4, 0); wanted.Wanted = true;
        Execute(expandedRun, wanted, true);
        Check(wanted.PickCalls == 3, "native wanted slot remains reserved");
        var tutorial = World.Create(13, 4, 0); tutorial.SkipIntro = false; tutorial.Day = 13;
        Execute(expandedRun, tutorial, true);
        Check(tutorial.PickCalls == 0 && tutorial.GunPityCalls == 1 && tutorial.AppraisalPityCalls == 1, "native tutorial fill restriction preserved");
        tutorial = World.Create(13, 4, 0); tutorial.SkipIntro = true; tutorial.Day = 1;
        Execute(expandedRun, tutorial, true);
        Check(tutorial.PickCalls == 0, "skip-intro day-one restriction preserved");
        var missing = World.Create(13, 4, 100); missing.NullFactory.UnionWith(Enumerable.Range(0, 10).Select(i => "c" + i));
        Execute(expandedRun, missing, true);
        Check(missing.RollChances.Count == 13 && missing.PickCalls == 1 && missing.Added.Count == 4 && missing.PitySuccesses.Count == 3, "null factories do not consume successful-source count");
        var rejected = World.Create(13, 4, 100); rejected.Rejected.Add("c0");
        Execute(expandedRun, rejected, true);
        Check(rejected.Added.Count == 12 && rejected.PickCalls == 0 && rejected.PitySuccesses.Contains("c0"), "native AddClient rejection/count semantics unchanged");
        var nullFill = World.Create(13, 4, 0); nullFill.NullRandom = true;
        Execute(expandedRun, nullFill, true);
        Check(nullFill.PickCalls == 4 && nullFill.Added.Count == 0, "native null random picks are not retried or forced");

        // Cross-check the patched method with its option off against the unmodified
        // native method across quotas, tutorial flags, probabilities and source counts.
        var random = new Random(7112026);
        for (int quota = 0; quota <= 6; quota++)
        for (int count = 0; count <= 18; count++)
        for (int variant = 0; variant < 4; variant++)
        {
            var baseline = World.Create(count, quota, 75);
            baseline.SkipIntro = variant != 1; baseline.Day = variant == 2 ? 1 : variant == 1 ? 8 : 63;
            baseline.Wanted = variant == 3;
            for (int i = 0; i < count; i++)
            {
                baseline.Rolls[i] = random.Next(100);
                baseline.Predictions[i] = ("c" + i, random.Next(101), "source-c" + i);
                if (i % 7 == 0) baseline.NullFactory.Add("c" + i);
                if (i % 11 == 0) baseline.Rejected.Add("c" + i);
            }
            var disabled = baseline.Copy();
            Execute(nativeRun, baseline, false);
            Execute(expandedRun, disabled, false);
            Check(baseline.Snapshot() == disabled.Snapshot(), "option off is native-equivalent");
            var modDisabled = disabled.Copy();
            Execute(expandedRun, modDisabled, true, false);
            Check(baseline.Snapshot() == modDisabled.Snapshot(), "mod disabled is native-equivalent");
        }
        for (int quota = 0; quota <= 6; quota++)
        for (int count = 0; count <= 25; count++)
        {
            var world = World.Create(count, quota, 100);
            Execute(expandedRun, world, true);
            Check(world.RollChances.Count == count, "all sources get one native roll at every baseline");
            Check(world.Added.Count == Math.Max(count, quota), "native fill remains baseline, never a new hardcoded four");
            world = World.Create(count, quota, 0);
            Execute(expandedRun, world, true);
            Check(world.RollChances.Count == count && world.Added.Count == quota, "zero-percent sources not forced");
        }
        foreach (int chance in new[] { 0, 25, 35, 40, 50, 75, 100 })
        {
            var world = World.Create(100, 0, chance);
            world.Rolls = Enumerable.Range(0, 100).ToList();
            Execute(expandedRun, world, true);
            Check(world.RollChances.Count == 100 && world.RollChances.All(c => c == chance), "each original percentage is passed unchanged to native RNG call");
            Check(world.Added.Count == chance, "every 0-99 roll obeys exact native probability threshold");
            Check(world.Added.Select(c => c.identifier).SequenceEqual(Enumerable.Range(0, chance).Select(i => "c" + i)), "probability successes preserve original prediction order");
        }
        Console.WriteLine($"Daily visitors native checks passed: {checks}; installed HandleNormalClient IL executed offline with deterministic world/RNG stubs; no Unity runtime or save access.");
    }

    private static void Reject(List<CodeInstruction> input, string message)
    {
        var snapshot = input.Select(c => (c.opcode, c.operand, labels: c.labels.ToArray(), blocks: c.blocks.ToArray())).ToArray();
        Check(!NativeDailyVisitors.TryRewrite(input, out var output, out _), message + " fails closed");
        Check(output.Count == snapshot.Length && output.Select((c, i) => c.opcode == snapshot[i].opcode && Equals(c.operand, snapshot[i].operand) && c.labels.SequenceEqual(snapshot[i].labels) && c.blocks.SequenceEqual(snapshot[i].blocks)).All(v => v), "failed match preserves whole native method");
    }

    private static void Execute(Action<StoreClientManager> action, World world, bool option, bool enabled = true)
    {
        ModEntry.Enabled = enabled; ModEntry.ExpectedVisitorsEnabled = option;
        Harness.Current = world;
        var manager = (StoreClientManager)RuntimeHelpers.GetUninitializedObject(typeof(StoreClientManager));
        manager.isTodayWantedQueued = world.Wanted;
        action(manager);
    }

    private static Action<StoreClientManager> Compile(bool expand)
    {
        var method = new DynamicMethod(expand ? "expanded_native_daily" : "original_native_daily", typeof(void), new[] { typeof(StoreClientManager) }, typeof(Tests).Module, true);
        var il = method.GetILGenerator();
        var codes = PatchProcessor.GetOriginalInstructions(Native, il);
        if (expand)
        {
            Check(NativeDailyVisitors.TryRewrite(codes, out var transformed, out _), "emitted native method matches patch");
            codes = transformed;
        }
        foreach (var code in codes)
        {
            foreach (var label in code.labels) il.MarkLabel(label);
            foreach (var block in code.blocks)
                switch (block.blockType)
                {
                    case ExceptionBlockType.BeginExceptionBlock: il.BeginExceptionBlock(); break;
                    case ExceptionBlockType.BeginFinallyBlock: il.BeginFinallyBlock(); break;
                    case ExceptionBlockType.EndExceptionBlock: break;
                    default: throw new Exception("Unexpected native exception block " + block.blockType);
                }
            var opcode = LongBranch(code.opcode);
            object? operand = code.operand;
            if (operand is MethodInfo called && Harness.Replacements.TryGetValue(called, out var replacement))
            { opcode = OpCodes.Call; operand = replacement; }
            Emit(il, opcode, operand);
            foreach (var block in code.blocks)
                if (block.blockType == ExceptionBlockType.EndExceptionBlock) il.EndExceptionBlock();
        }
        return method.CreateDelegate<Action<StoreClientManager>>();
    }

    private static OpCode LongBranch(OpCode opcode)
    {
        if (opcode.OperandType != OperandType.ShortInlineBrTarget) return opcode;
        return typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null)!).Single(op => op.Name == opcode.Name![..^2]);
    }

    private static void Emit(ILGenerator il, OpCode opcode, object? operand)
    {
        switch (operand)
        {
            case null: il.Emit(opcode); break;
            case Label label: il.Emit(opcode, label); break;
            case Label[] labels: il.Emit(opcode, labels); break;
            case LocalBuilder local: il.Emit(opcode, local); break;
            case MethodInfo method: il.Emit(opcode, method); break;
            case ConstructorInfo constructor: il.Emit(opcode, constructor); break;
            case FieldInfo field: il.Emit(opcode, field); break;
            case Type type: il.Emit(opcode, type); break;
            case int value: il.Emit(opcode, value); break;
            case sbyte value: il.Emit(opcode, value); break;
            case byte value: il.Emit(opcode, value); break;
            case short value: il.Emit(opcode, value); break;
            case string value: il.Emit(opcode, value); break;
            default: throw new Exception("Unexpected native operand " + operand.GetType());
        }
    }
}

sealed class World
{
    internal int Quota, Day = 63, PickCalls, GunPityCalls, AppraisalPityCalls, PityPredictions;
    internal bool SkipIntro = true, Wanted, NullRandom;
    internal List<(string, int, string)> Predictions = new();
    internal List<int> Rolls = new(), RollChances = new();
    internal List<StoreClient> Added = new();
    internal List<string> Events = new();
    internal HashSet<string> NullFactory = new(), Rejected = new(), PitySuccesses = new();
    internal static World Create(int count, int quota, int chance) => new()
    {
        Quota = quota,
        Predictions = Enumerable.Range(0, count).Select(i => ("c" + i, chance, "source-c" + i)).ToList(),
        Rolls = Enumerable.Repeat(0, count).ToList()
    };
    internal World Copy() => new()
    {
        Quota = Quota, Day = Day, SkipIntro = SkipIntro, Wanted = Wanted, NullRandom = NullRandom,
        Predictions = new(Predictions), Rolls = new(Rolls), NullFactory = new(NullFactory), Rejected = new(Rejected)
    };
    internal string Snapshot() => string.Join("|", Events) + ";" + string.Join(",", RollChances) + ";" +
        string.Join(",", Added.Select(c => c.identifier + ":" + c.attractedItemId)) + ";" + string.Join(",", PitySuccesses.Order());
}

static class Harness
{
    internal static World Current = null!;
    internal static readonly Dictionary<MethodInfo, MethodInfo> Replacements = BuildReplacements();
    private static Dictionary<MethodInfo, MethodInfo> BuildReplacements()
    {
        var targets = new[]
        {
            (typeof(StoreClientManager), "UpdateTracker", "Tracker"), (typeof(StoreClientManager), "GetNormalCustomerCount", "Quota"),
            (typeof(ShowcaseHelper), "GetDisplayCasePrediction", "Predictions"), (typeof(ShowcaseHelper), "ClearPendingSupplierPicks", "Clear"),
            (typeof(RNG), "Roll", "Roll"), (typeof(StoreClientListDict), "CreateStoreClient", "Create"),
            (typeof(StoreClientManager), "AddClient", "Add"), (typeof(StoreClientManager), "PickClient", "Pick"),
            (typeof(ShowcaseHelper), "OnGunBuyersRolled", "GunPity"), (typeof(AppraisalRankHelper), "OnCustomerRolled", "AppraisalPity"),
            (typeof(PlayerStore), "get_Instance", "Player"), (typeof(StoreStation), "get_Instance", "Station")
        };
        return targets.Select(p =>
        {
            var replacement = typeof(Harness).GetMethod(p.Item3, BindingFlags.Static | BindingFlags.NonPublic)!;
            var parameters = replacement.GetParameters().Select(parameter => parameter.ParameterType);
            if (p.Item1 == typeof(StoreClientManager)) parameters = parameters.Skip(1);
            return (native: AccessTools.Method(p.Item1, p.Item2, parameters.ToArray()), replacement);
        }).ToDictionary(p => p.native, p => p.replacement);
    }
    private static void Tracker(StoreClientManager manager) => Current.Events.Add("tracker");
    private static int Quota(StoreClientManager manager) { Current.Events.Add("quota"); return Current.Quota; }
    private static List<(string, int, string)> Predictions() { Current.Events.Add("predictions"); return Current.Predictions; }
    private static void Clear() => Current.Events.Add("clear");
    private static bool Roll(int chance)
    {
        int value = Current.Rolls[Current.RollChances.Count]; Current.RollChances.Add(chance);
        Current.Events.Add("roll:" + chance); return value < chance;
    }
    private static StoreClient? Create(string id)
    {
        Current.Events.Add("factory:" + id);
        if (Current.NullFactory.Contains(id)) return null;
        var client = (StoreClient)RuntimeHelpers.GetUninitializedObject(typeof(StoreClient)); client.identifier = id; return client;
    }
    private static void Add(StoreClientManager manager, StoreClient client)
    {
        Current.Events.Add("add:" + client.identifier);
        if (!Current.Rejected.Contains(client.identifier)) Current.Added.Add(client);
    }
    private static StoreClient? Pick(StoreClientManager manager)
    {
        Current.PickCalls++; Current.Events.Add("pick");
        if (Current.NullRandom) return null;
        var client = (StoreClient)RuntimeHelpers.GetUninitializedObject(typeof(StoreClient)); client.identifier = "random-" + Current.PickCalls; return client;
    }
    private static void GunPity(List<(string, int, string)> predictions, HashSet<string> successes)
    {
        Current.Events.Add("gun-pity"); Current.GunPityCalls++; Current.PityPredictions = predictions.Count; Current.PitySuccesses = new(successes);
    }
    private static void AppraisalPity(List<(string, int, string)> predictions, HashSet<string> successes)
    {
        Current.Events.Add("appraisal-pity"); Current.AppraisalPityCalls++;
        if (predictions.Count != Current.PityPredictions || !successes.SetEquals(Current.PitySuccesses)) throw new Exception("pity calls received different sets");
    }
    private static PlayerStore Player()
    {
        var player = (PlayerStore)RuntimeHelpers.GetUninitializedObject(typeof(PlayerStore)); player.skipIntro = Current.SkipIntro; return player;
    }
    private static StoreStation Station()
    {
        var station = (StoreStation)RuntimeHelpers.GetUninitializedObject(typeof(StoreStation)); station.dayCounter = Current.Day; return station;
    }
}

namespace DeepSpaceQoL
{
    internal static class ModEntry
    {
        internal static bool Enabled, ExpectedVisitorsEnabled;
        internal static TestLog? Log = null;
    }
    internal sealed class TestLog { internal void Warning(string text) => Console.WriteLine(text); }
}
