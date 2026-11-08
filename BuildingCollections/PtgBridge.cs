using System;
using System.Linq;

namespace BuildingCollections
{
    public static class PtgBridge
    {
        const string SystemTypeName = "PlopTheGrowables.PloppedBuildingSystem";

        static bool? available;

        public static bool Available
        {
            get
            {
                if (available == null)
                {
                    available = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetType(SystemTypeName, false) != null);
                    Mod.Log.Info(available.Value
                        ? "Plop the Growables found: it keeps the placed buildings from being condemned."
                        : "Plop the Growables not found: the game will condemn buildings placed without zoning.");
                }

                return available.Value;
            }
        }
    }
}
