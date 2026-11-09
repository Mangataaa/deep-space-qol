// Compile-only contracts for the storage subsystem, never included in the mod DLL.
using System;
using System.Collections.Generic;
namespace DeepSpaceQoL
{
    internal static class StorageCatalog
    {
        public static IReadOnlyList<GameInventory> GetSources() => throw new NotImplementedException();
    }
    internal static class StorageRules
    {
        public static bool CanAutoAccess(GameInventory inventory) => throw new NotImplementedException();
        public static Core.StoragePolicy Policy(GameItem item) => throw new NotImplementedException();
    }
}
