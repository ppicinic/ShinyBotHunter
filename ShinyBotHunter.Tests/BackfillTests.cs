using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using ShinyBotHunter.BotEvents.Target;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Persistence;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    public class BackfillTests
    {
        private const string SoulSilver = "Pokemon SoulSilver", SoulSilver2 = "Pokemon SoulSilver2";

        private static readonly IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>> Hgss =
            MilestoneFile.Load(Path.Combine(RepoPaths.AppProject, "Data", "HGSS"));

        // Production's 20 phase documents as stored (read-only export, 2026-11-25).
        private static readonly List<PhaseInfo> ProductionPhases = BsonSerializer.Deserialize<List<PhaseInfo>>(
            System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "ShinyBotHunter.Tests", "Fixtures", "production-phases.json")));

        private static PersistPokemonData Encounter(string phase, string gameFileName, int species, string formCode) => new()
        {
            PokemonData = new PokemonData { Species = (ushort)species, FormCode = formCode, PID = 0x00080000u },
            Game = GameFlavor.HGSS,
            Phase = phase,
            GameFileName = gameFileName
        };

        // As GetMilestoneRowsAsync reads it: the key's encounters at every counted phase on the file's own node.
        private static long EncountersHere(GameEncounterCache cache, GameFile file, string milestoneId, string key)
        {
            var phases = file.CountedPhases(milestoneId);
            PokemonPersistenceService.EnsureKeys(cache, new CacheLoadRequest(GameFlavor.HGSS, [key], phases, [file.Id]));
            return phases.Sum(p => cache.GameLevelCache[file.Id].PhaseEncounterCache[p].PokemonEncounterCache[key].Encounters);
        }

        // Pidgey (any form, "#16") on Route 30 under the old names and the generated id, on two SoulSilver files.
        private static List<CountRow> Route30Counts() =>
        [
            new("1-4 Route 30", SoulSilver, "pidgey", 16, 10, 0),
            new("34-land", SoulSilver, "pidgey", 16, 3, 0),
            new("1-4b Route 30", SoulSilver2, "pidgey", 16, 5, 0),
            new("34-land", SoulSilver2, "pidgey", 16, 7, 0),
            new("1-8 Route 32", SoulSilver, "rattata", 19, 4, 0),
            new("36-land", SoulSilver, "rattata", 19, 2, 0),
        ];

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

            Assert.Equal(expected.Keys.Order(), ProductionPhases.Select(p => p.Name).Distinct().Order());
            foreach (var version in new[] { RomVersion.HeartGold, RomVersion.SoulSilver })
            {
                Assert.All(expected, e => Assert.Equal(e.Value,
                    Backfill.Suggestion(e.Key) is { } id ? PhaseTarget.FindMilestone(Hgss, version, id)!.Name : null));
            }
        }

        [Fact]
        public void GameFile_StoredWithoutLinks_LoadsWithNone()
        {
            var file = BsonSerializer.Deserialize<GameFile>(new BsonDocument { { "_id", SoulSilver }, { "Entries", new BsonArray() }, { "Hidden", new BsonArray() } });

            Assert.Empty(file.Links);
            Assert.Equal(["34-land"], file.CountedPhases("34-land"));
        }

        [Fact]
        public void Link_Route30_CountsIncludeLinkedName_RemovalRestores()
        {
            var cache = PokemonPersistenceService.BuildGameCache(Route30Counts(), [], []);
            var linked = new GameFile { Id = SoulSilver, Links = [new MilestoneLink("1-4 Route 30", "34-land")] };
            var unlinked = new GameFile { Id = SoulSilver };
            var other = new GameFile { Id = SoulSilver2 };

            Assert.Equal(13, EncountersHere(cache, linked, "34-land", "#16"));
            Assert.Equal(3, EncountersHere(cache, unlinked, "34-land", "#16"));
            Assert.Equal(7, EncountersHere(cache, other, "34-land", "#16"));
        }

        [Fact]
        public void Link_NewEncounterUnderTheId_CountedOnce_LinkedNameUnchanged()
        {
            var cache = PokemonPersistenceService.BuildGameCache(Route30Counts(), [], []);
            var file = new GameFile { Id = SoulSilver, Links = [new MilestoneLink("1-4 Route 30", "34-land")] };
            var encounter = Encounter("34-land", SoulSilver, 16, "pidgey");

            PokemonPersistenceService.EnsureKeys(cache, new CacheLoadRequest(encounter));
            cache.Increment(encounter);

            Assert.Equal(14, EncountersHere(cache, file, "34-land", "#16"));
            Assert.Equal(10, cache.GameLevelCache[SoulSilver].PhaseEncounterCache["1-4 Route 30"].PokemonEncounterCache["#16"].Encounters);
        }

        [Fact]
        public void PartBackfilled_UnlinkedPhaseKeepsItsOwnCounts()
        {
            var cache = PokemonPersistenceService.BuildGameCache(Route30Counts(), [], []);
            var file = new GameFile { Id = SoulSilver, Links = [new MilestoneLink("1-4 Route 30", "34-land")] };

            Assert.Equal(["36-land"], file.CountedPhases("36-land"));
            Assert.Equal(2, EncountersHere(cache, file, "36-land", "#19"));
            Assert.Equal(4, cache.GameLevelCache[SoulSilver].PhaseEncounterCache["1-8 Route 32"].PokemonEncounterCache["#19"].Encounters);
        }

        [Fact]
        public void LinkedRefusal_LinkedName_NamesTheMilestone_OtherNamesStart()
        {
            var file = new GameFile { Id = SoulSilver, Links = [new MilestoneLink("1-4 Route 30", "34-land")] };

            Assert.Equal("1-4 Route 30 is linked to Route 30 · grass on Pokemon SoulSilver; start Route 30 · grass, or remove the link.",
                BotService.LinkedRefusal(file, "1-4 Route 30", Hgss, RomVersion.SoulSilver));
            Assert.Null(BotService.LinkedRefusal(file, "34-land", Hgss, RomVersion.SoulSilver));
            Assert.Null(BotService.LinkedRefusal(file, "1-8 Route 32", Hgss, RomVersion.SoulSilver));
            Assert.Null(BotService.LinkedRefusal(new GameFile { Id = SoulSilver2 }, "1-4 Route 30", Hgss, RomVersion.SoulSilver));
        }
    }
}
