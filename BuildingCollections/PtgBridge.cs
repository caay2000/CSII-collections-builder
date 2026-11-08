using System;
using System.Linq;
using System.Reflection;

namespace BuildingCollections
{
    public static class PtgBridge
    {
        const string SystemTypeName = "PlopTheGrowables.PloppedBuildingSystem";
        const string PloppedTypeName = "PlopTheGrowables.PloppedBuilding";
        const string SpawnedTypeName = "PlopTheGrowables.SpawnedBuilding";

        static Assembly assembly;
        static bool resolved;

        public static bool Available => Resolve() != null;

        public static Type PloppedType => Resolve()?.GetType(PloppedTypeName, false);

        public static Type SpawnedType => Resolve()?.GetType(SpawnedTypeName, false);

        static Assembly Resolve()
        {
            if (!resolved)
            {
                resolved = true;
                assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetType(SystemTypeName, false) != null);
                Mod.Log.Info(assembly != null
                    ? "Plop the Growables found: it keeps the placed buildings from being condemned."
                    : "Plop the Growables not found: the game will condemn buildings placed without zoning.");
            }

            return assembly;
        }
    }
}
