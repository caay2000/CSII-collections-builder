using System;
using System.IO;
using Colossal.PSI.Environment;

namespace BuildingCollections
{
    public static class SessionMarker
    {
        static string FilePath => Path.Combine(EnvPath.kUserDataPath, "ModsData", "BuildingCollections", "session.marker");

        public static void Start(string header)
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    Mod.Log.Warn("[session] The previous session did not close cleanly (crash or forced exit). Its last record:\n  "
                        + File.ReadAllText(FilePath).Trim().Replace("\n", "\n  "));
                }

                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                Write($"started: {header}");
            }
            catch (Exception e)
            {
                Mod.Log.Warn("[session] Could not use the session marker: " + e.Message);
            }
        }

        public static void Record(string action)
        {
            try
            {
                var lines = File.Exists(FilePath) ? File.ReadAllLines(FilePath) : Array.Empty<string>();
                var started = lines.Length > 0 && lines[0].StartsWith("started:") ? lines[0] : "started: ?";
                Write(started + "\n" + $"last action: {DateTime.Now:yyyy-MM-dd HH:mm:ss} {action}");
            }
            catch (Exception e)
            {
                Mod.Log.Warn("[session] Could not record the last action: " + e.Message);
            }
        }

        public static void End()
        {
            try
            {
                File.Delete(FilePath);
            }
            catch (Exception e)
            {
                Mod.Log.Warn("[session] Could not remove the session marker: " + e.Message);
            }
        }

        static void Write(string text) => File.WriteAllText(FilePath, text + "\n");
    }
}
