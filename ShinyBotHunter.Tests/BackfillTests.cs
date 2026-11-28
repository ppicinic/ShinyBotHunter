using MongoDB.Bson.Serialization;
using PKHeX.Core;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    public class BackfillTests
    {
        private static readonly IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>> Hgss =
            MilestoneFile.Load(Path.Combine(RepoPaths.AppProject, "Data", "HGSS"));

        // Production's 20 phase documents as stored (read-only export, 2026-11-25), loaded as PhaseInfoService.InitAsync loads them.
        private static List<PhaseInfo> NormalizedProductionPhases()
        {
            var metadata = new PokemonMetadataService(null!);
            for (ushort id = 1; id <= 493; id++)
            {
                string name = SpeciesName.GetSpeciesName(id, (int)LanguageID.English);
                metadata.Seed(new PokemonMetadata { Id = id, PokemonName = name, PokeApiName = name.ToLowerInvariant() });
            }
            var phases = BsonSerializer.Deserialize<List<PhaseInfo>>(
                System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "ShinyBotHunter.Tests", "Fixtures", "production-phases.json")));
            phases.ForEach(p => PhaseInfoService.Normalize(p, metadata));
            return phases;
        }

        [Fact]
        public void Suggestion_ProductionNames_MatchTheResearchTable()
        {
            var expected = new Dictionary<string, string?>
            {
                ["0c Training"] = null,
                ["1-1 Starters"] = null,
                ["1-2 Route 29"] = "Route 29 · grass",
                ["1-2c Route 29"] = "Route 29 · grass",
                ["1-3 Route 46"] = "Route 46 · grass",
                ["1-3b Route 46"] = "Route 46 · grass",
                ["1-4 Route 30"] = "Route 30 · grass",
                ["1-4b Route 30"] = "Route 30 · grass",
                ["1-4c Route 30"] = "Route 30 · grass",
                ["1-6 Dark Cave"] = "Dark Cave (Route 31 side) · cave",
                ["1-6b Dark Cave"] = "Dark Cave (Route 31 side) · cave",
                ["1-8 Route 32"] = "Route 32 · grass",
                ["1-8b Route 32"] = "Route 32 · grass",
                ["1-9 Sprout Tower"] = "Sprout Tower · cave",
                ["1-10 Ruins of Alph"] = "Ruins of Alph (underground hall) · cave",
                ["2-1c Togepi"] = null,
                ["2-2c Slowpoke Well"] = "Slowpoke Well B1F · cave",
                ["2-3c Azalea Town Headbutt"] = "Azalea Town · headbutt",
                ["1-2 WISHMKR Jirachi"] = null,
            };

            Assert.Equal(expected.Keys.Order(), NormalizedProductionPhases().Select(p => p.Name).Distinct().Order());
            foreach (var version in new[] { RomVersion.HeartGold, RomVersion.SoulSilver })
            {
                Assert.All(expected, e => Assert.Equal(e.Value,
                    Backfill.Suggestion(e.Key) is { } id ? Hgss[version].Single(m => m.Id == id).Name : null));
            }
        }
    }
}
