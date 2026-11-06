using System;
using System.IO;
using Colossal.PSI.Environment;
using Newtonsoft.Json;

namespace BuildingCollections
{
    public sealed class DevConfig
    {
        public double BlockTouchTolerance = 4.0;

        public double ClickSnapDistance = 0;

        public double SearchRadius = 400.0;

        public bool UseRoadFace = true;

        public bool UseVirtualLots = true;

        public double RoadSearchRadius = 1500.0;

        public bool SwapCornerSides = false;

        public bool RequireNoErrors = true;

        public bool UsePlacementOffset = true;

        public bool LogCells = true;

        public int ReportAfterFrames = 1800;

        static string FilePath => Path.Combine(EnvPath.kUserDataPath, "ModsData", "BuildingCollections", "dev.json");

        public static DevConfig Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    return JsonConvert.DeserializeObject<DevConfig>(File.ReadAllText(FilePath)) ?? new DevConfig();
                }

                var d = new DevConfig();
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(d, Formatting.Indented));
                return d;
            }
            catch (Exception e)
            {
                Mod.Log.Warn("Invalid dev.json, using defaults: " + e.Message);
                return new DevConfig();
            }
        }
    }
}
