using System.Reflection;
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

    internal static void Run()
    {
        var ids = typeof(StoreService).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!)
            .Concat(new[] { "UNRELATED_MOD_SERVICE", "CRIMINEL_NETWORK_suffix", "criminal_network", "", null! }).ToArray();
        var patch = typeof(NativeServiceCooldowns).GetNestedType("NightlyServiceCooldown", BindingFlags.NonPublic)!
            .GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (string id in ids)
        foreach (int remaining in new[] { -1, 0, 1, 2, 3, 5, 6, 99 })
        foreach (bool enabled in new[] { false, true })
        foreach (bool option in new[] { false, true })
        foreach (bool unlocked in new[] { false, true })
        foreach (bool chosen in new[] { false, true })
        {
            var service = new StoreService { id = id, currentCooldown = remaining, totalCooldown = 5,
                cost = 500, unlocked = unlocked, isUsing = chosen, introduced = true };
            bool native = service.IsOnCooldown();
            bool expected = native && !(enabled && option && id is StoreService.CRIMINEL_NETWORK or StoreService.HYPERTECH_SMUGGLER_SERVICE);
            Check(NativeServiceCooldowns.EffectiveCooldown(service, native, enabled, option) == expected, "Only the two exact service IDs bypass the real native cooldown.");
            ModEntry.Enabled = enabled; ModEntry.RemoveServiceCooldowns = option;
            object[] arguments = { service, native };
            patch.Invoke(null, arguments);
            Check((bool)arguments[1] == expected, "The actual Harmony postfix applies the same bounded override.");
            Check(service.currentCooldown == remaining && service.totalCooldown == 5 && service.cost == 500 &&
                service.unlocked == unlocked && service.isUsing == chosen && service.introduced,
                "No saved cooldown, fee, unlock, selection, or introduction field is changed.");
            Check(service.IsActive() == (unlocked && chosen), "Native unlock/selection gating is unchanged.");
        }
        Check(!NativeServiceCooldowns.IsRequestedService(null), "Missing service is not targeted.");
        var query = typeof(StoreService).GetMethod(nameof(StoreService.IsOnCooldown))!;
        foreach (var method in new[] {
            typeof(EndOfDayUIManager).GetMethod("BuildServiceRow", BindingFlags.NonPublic | BindingFlags.Instance)!,
            typeof(EndOfDayUIManager).GetProperty(nameof(EndOfDayUIManager.HasToggleableService))!.GetMethod!,
            typeof(ServiceElement).GetMethod(nameof(ServiceElement.Init))! })
            Check(PatchProcessor.GetOriginalInstructions(method).Any(i => Equals(i.operand, query)),
                method.Name + " still queries the patched native method; no UI-only fake cooldown.");
        Console.WriteLine($"Service cooldown native tests passed: {checks} checks; native query/IsActive and production postfix, no saves or nightly charges executed.");
    }
}

namespace DeepSpaceQoL
{
    internal static class ModEntry
    {
        internal static bool Enabled;
        internal static bool RemoveServiceCooldowns;
    }
}
