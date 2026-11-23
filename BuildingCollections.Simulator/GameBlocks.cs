using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BuildingCollections.Core;

namespace BuildingCollections.Simulator
{
    public static class GameBlocks
    {
        const string Group = "Game blocks";

        static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            ["whitestone_prospect"] = "Whitestone with Prospect (closed loop and 80° corner)",
            ["whitestone_big_loop"] = "Whitestone, big loop",
            ["prospect_tight_curve"] = "Prospect, tight curve",
            ["cobblestone_long_curve"] = "Cobblestone, long curve and short street",
            ["block_17334_-10268"] = "narrow triangle (17334, -10268), 8 existing in the game",
            ["block_17569_-10291"] = "block (17569, -10291)",
            ["block_17608_-10348"] = "block (17608, -10348)",
            ["block_17618_-10274"] = "block (17618, -10274), 3 existing in the game",
            ["block_17647_-10363"] = "block (17647, -10363)",
            ["block_17661_-10352"] = "block (17661, -10352)",
            ["block_17817_-10255"] = "block (17817, -10255), 12 existing in the game",
            ["block_17824_-10159"] = "block (17824, -10159), 4 existing in the game",
            ["block_17925_-10139"] = "block (17925, -10139)",
            ["block_18235_-10153"] = "triangle (18235, -10153), 8 existing in the game",
            ["block_18633_-11042"] = "long block (18633, -11042), 34 existing in the game",
            ["block_18946_-11057"] = "block (18946, -11057), 21 existing in the game",
            ["block_19034_-11154"] = "block (19034, -11154), 11 existing in the game",
            ["block_19101_-11125"] = "Cornfield, Crestwood and Crescent (19101, -11125), gaps at the corners in the game",
            ["block_13230_-7079"] = "block with a 45° and a 135° corner (13230, -7079), angled corners placed by hand in the game",
            ["block_13351_-7456"] = "block with a 53° and a 127° corner (13351, -7456), no angled building of that angle",
            ["block_13413_-7458"] = "block with a 53° and a 127° corner (13413, -7458), no angled building of that angle",
            ["block_13509_-8156"] = "angled block (13509, -8156), 67°, 69° and 24° corners",
            ["block_13718_-8105"] = "angled block (13718, -8105), 119°, 106°, 142° and 70° corners",
            ["block_13648_-8269"] = "angled block (13648, -8269), 110° and 126° corners",
            ["block_14009_-8322"] = "angled block (14009, -8322), 113°, 111° and 134° corners, 80 existing",
            ["block_13109_-8496"] = "Belmont and Oak Lane (13109, -8496), 53° and 127° corners, 135° building over the sidewalk in the game",
            ["block_13928_-8183"] = "angled block (13928, -8183), 61° and 54° corners",
            ["block_14164_-8507"] = "angled block (14164, -8507), 108°, 102° and 159° corners, 29 existing",
            ["block_14381_-7720"] = "angled block (14381, -7720), 112° and 68° corners, 14 existing",
            ["block_14531_-8575"] = "angled block (14531, -8575), 145° and 23° corners",
            ["block_14642_-8647"] = "angled block (14642, -8647), 76° and 112° corners, 8 existing",
            ["block_15000_-8823"] = "angled block (15000, -8823), 77°, 159° and 119° corners, 7 existing",
            ["block_15011_-8964"] = "angled block (15011, -8964), 103° corner, 4 existing",
        };

        public static string ProjectFolder => FindProjectFolder(AppContext.BaseDirectory);

        public static string Folder => Path.Combine(ProjectFolder, "Blocks");

        static string FindProjectFolder(string start)
        {
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "BuildingCollections.Simulator");
                if (File.Exists(Path.Combine(candidate, "BuildingCollections.Simulator.csproj")))
                {
                    return candidate;
                }

                if (File.Exists(Path.Combine(dir.FullName, "BuildingCollections.Simulator.csproj")))
                {
                    return dir.FullName;
                }
            }

            throw new DirectoryNotFoundException($"BuildingCollections.Simulator not found above {start}");
        }

        public static IEnumerable<Scenario> All()
        {
            if (!Directory.Exists(Folder))
            {
                yield break;
            }

            foreach (var file in Directory.GetFiles(Folder, "*.json").OrderBy(f => f))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var sides = new List<List<Pt>>();
                var frontage = new List<bool>();
                foreach (var side in doc.RootElement.GetProperty("sides").EnumerateArray())
                {
                    frontage.Add(side.GetProperty("frontage").GetBoolean());
                    sides.Add(side.GetProperty("points").EnumerateArray()
                        .Select(p => new Pt(p[0].GetDouble(), p[1].GetDouble())).ToList());
                }

                string key = Path.GetFileNameWithoutExtension(file);
                yield return new Scenario
                {
                    Group = Group,
                    Name = Names.TryGetValue(key, out var name) ? name : key,
                    Sides = sides,
                    Frontage = frontage.All(f => f) ? null : frontage,
                    Existing = Existing(doc.RootElement),
                };
            }
        }

        static List<Quad> Existing(JsonElement root)
        {
            var quads = new List<Quad>();
            if (!root.TryGetProperty("existing", out var list))
            {
                return quads;
            }

            foreach (var q in list.EnumerateArray())
            {
                var p = q.EnumerateArray().Select(v => new Pt(v[0].GetDouble(), v[1].GetDouble())).ToList();
                if (p.Count == 4)
                {
                    quads.Add(new Quad(p[0], p[1], p[2], p[3]));
                }
            }

            return quads;
        }
    }
}
