using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildingCollections.Core;
using Colossal.PSI.Environment;
using Newtonsoft.Json;

namespace BuildingCollections
{
    /// <summary>Colección de prefabs de edificios, guardada por nombre de prefab.</summary>
    public class BuildingCollection
    {
        public string Name { get; set; }

        /// <summary>Etiqueta de Find It asociada (nombre normalizado).</summary>
        public string Tag { get; set; }

        public List<string> Prefabs { get; set; } = new List<string>();
    }

    public static class CollectionStore
    {
        static string FilePath => Path.Combine(EnvPath.kUserDataPath, "ModsData", "BuildingCollections", "collections.json");

        public static List<BuildingCollection> Collections { get; private set; } = new List<BuildingCollection>();

        public static BuildingCollection Find(string name) =>
            Collections.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    return;
                }

                Collections = JsonConvert.DeserializeObject<List<BuildingCollection>>(File.ReadAllText(FilePath)) ?? new List<BuildingCollection>();
                foreach (var c in Collections)
                {
                    c.Tag ??= CollectionLogic.NormalizeTag(c.Name);
                }

                Mod.Log.Info($"Loaded {Collections.Count} collections");
            }
            catch (Exception e) { Mod.Log.Error(e); }
        }

        /// <summary>Escritura atómica: fichero temporal y renombrado (regla R9.2).</summary>
        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(Collections, Formatting.Indented));
                if (File.Exists(FilePath))
                {
                    File.Replace(tmp, FilePath, null);
                }
                else
                {
                    File.Move(tmp, FilePath);
                }
            }
            catch (Exception e) { Mod.Log.Error(e); }
        }
    }
}
