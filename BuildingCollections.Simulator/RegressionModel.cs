using System.Collections.Generic;
using System.Text.Json.Serialization;
using BuildingCollections.Core;

namespace BuildingCollections.Simulator
{
    public sealed class Metrics
    {
        public string Scenario { get; set; }
        public float Overlap { get; set; }
        public int MissedCorners { get; set; }
        public double GapMeters { get; set; }
        public double MaxGap { get; set; }
        public int GapsBetweenBuildings { get; set; }
        public int EmptyCells { get; set; }

        [JsonIgnore]
        public string Key => $"{Scenario} @ {Overlap:0.0#}";

        [JsonIgnore]
        public bool Strict => Overlap <= FootprintMask.StrictOverlap;
    }

    public sealed class Baseline
    {
        public string Catalog { get; set; }
        public int[] Seeds { get; set; }
        public List<Metrics> Runs { get; set; } = new List<Metrics>();
    }

    public sealed class Verdict
    {
        public List<string> Regressions { get; } = new List<string>();
        public List<string> Improvements { get; } = new List<string>();
        public List<string> Missing { get; } = new List<string>();
        public List<string> Added { get; } = new List<string>();

        public bool Passed => Regressions.Count == 0 && Missing.Count == 0 && Added.Count == 0;

        public IEnumerable<string> Failures()
        {
            foreach (var l in Regressions)
            {
                yield return "WORSE   " + l;
            }

            foreach (var l in Missing)
            {
                yield return "MISSING " + l;
            }

            foreach (var l in Added)
            {
                yield return "NEW     " + l + " (not in the baseline: run 'regress --update')";
            }
        }
    }
}
