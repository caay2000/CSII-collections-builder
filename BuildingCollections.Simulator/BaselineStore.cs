using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BuildingCollections.Simulator
{
    public static class BaselineStore
    {
        static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true };

        public static string DefaultPath => Path.Combine(GameBlocks.ProjectFolder, "regression-baseline.json");

        public static Baseline Load(string path)
        {
            Baseline baseline;
            try
            {
                baseline = JsonSerializer.Deserialize<Baseline>(File.ReadAllText(path));
            }
            catch (JsonException e)
            {
                throw new InvalidDataException($"{path} is not a valid baseline: {e.Message}", e);
            }

            if (baseline?.Runs == null || baseline.Seeds == null || baseline.Seeds.Length == 0 || baseline.Catalog == null)
            {
                throw new InvalidDataException($"{path} lacks the catalog, the seeds or the runs: run 'regress --update'.");
            }

            return baseline;
        }

        public static void Save(Baseline baseline, string path) => File.WriteAllText(path, JsonSerializer.Serialize(baseline, Json) + Environment.NewLine);

        public static string Mismatch(Baseline expected, string catalog, int[] seeds) =>
            expected.Catalog != catalog ? $"baseline catalog '{expected.Catalog}' differs from '{catalog}'"
            : !expected.Seeds.SequenceEqual(seeds) ? "baseline seeds differ from the current ones"
            : null;
    }
}
