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
        };

        public static string Folder => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../Blocks"));

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
