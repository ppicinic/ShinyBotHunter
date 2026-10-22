using ShinyBotHunter.Macro;
using ShinyBotHunter.Persistence;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    public class GameEncounterCacheBuilderTests
    {
        private static void AssertCounts(PokemonCounts counts, long encounters, long shinyEncounters, long shiniesCaught)
        {
            Assert.Equal(encounters, counts.Encounters);
            Assert.Equal(shinyEncounters, counts.ShinyEncounters);
            Assert.Equal(shiniesCaught, counts.ShiniesCaught);
        }

        [Fact]
        public void BuildGameCache_SumsEveryNodeFromRows()
        {
            var counts = new List<CountRow>
            {
                new("P1", "F1", "a", 1, 10, 1),
                new("P1", "F1", "b", 2, 20, 0),
                new("P1", "F2", "a", 1, 30, 0),
                new("P2", "F1", "c", 3, 40, 0),
                new("P2", "F2", "b", 2, 50, 0),
                new("P2", "F2", "c", 3, 60, 0),
            };
            var caughtF1 = new CaughtPokemon() { Pokemon = new PokemonData() { FormCode = "a", Species = 1 }, Game = GameFlavor.HGSS, Phase = "P1", GameFileName = "F1" };
            var caughtF2 = new CaughtPokemon() { Pokemon = new PokemonData() { FormCode = "c", Species = 3 }, Game = GameFlavor.HGSS, Phase = "P2", GameFileName = "F2" };

            var cache = PokemonPersistenceService.BuildGameCache(counts, new List<ProgressRow>(), new List<CaughtPokemon> { caughtF1, caughtF2 }, new List<PokemonData>());

            // game: 10+20+30+40+50+60 = 210, one shiny (row 1), two caught
            AssertCounts(cache.AllPokemonCounts, 210, 1, 2);
            AssertCounts(cache.PokemonEncounterCache["a"], 40, 1, 1);
            AssertCounts(cache.PokemonEncounterCache["b"], 70, 0, 0);
            AssertCounts(cache.PokemonEncounterCache["c"], 100, 0, 1);
            AssertCounts(cache.PokemonEncounterCache["#1"], 40, 1, 1);
            AssertCounts(cache.PokemonEncounterCache["#3"], 100, 0, 1);
            // phase: P1 = 10+20+30, P2 = 40+50+60
            AssertCounts(cache.PhaseEncounterCache["P1"].AllPokemonCounts, 60, 1, 1);
            AssertCounts(cache.PhaseEncounterCache["P1"].PokemonEncounterCache["a"], 40, 1, 1);
            AssertCounts(cache.PhaseEncounterCache["P1"].PokemonEncounterCache["b"], 20, 0, 0);
            AssertCounts(cache.PhaseEncounterCache["P2"].AllPokemonCounts, 150, 0, 1);
            AssertCounts(cache.PhaseEncounterCache["P2"].PokemonEncounterCache["c"], 100, 0, 1);
            // file: F1 = 10+20+40, F2 = 30+50+60
            AssertCounts(cache.GameLevelCache["F1"].AllPokemonCounts, 70, 1, 1);
            AssertCounts(cache.GameLevelCache["F1"].PokemonEncounterCache["a"], 10, 1, 1);
            AssertCounts(cache.GameLevelCache["F1"].PokemonEncounterCache["c"], 40, 0, 0);
            AssertCounts(cache.GameLevelCache["F2"].AllPokemonCounts, 140, 0, 1);
            AssertCounts(cache.GameLevelCache["F2"].PokemonEncounterCache["c"], 60, 0, 1);
            // file and phase: F1/P1 = 10+20, F2/P1 = 30, F2/P2 = 50+60
            AssertCounts(cache.GameLevelCache["F1"].PhaseEncounterCache["P1"].AllPokemonCounts, 30, 1, 1);
            AssertCounts(cache.GameLevelCache["F1"].PhaseEncounterCache["P1"].PokemonEncounterCache["a"], 10, 1, 1);
            AssertCounts(cache.GameLevelCache["F2"].PhaseEncounterCache["P1"].AllPokemonCounts, 30, 0, 0);
            AssertCounts(cache.GameLevelCache["F2"].PhaseEncounterCache["P2"].AllPokemonCounts, 110, 0, 1);
            AssertCounts(cache.GameLevelCache["F2"].PhaseEncounterCache["P2"].PokemonEncounterCache["c"], 60, 0, 1);

            Assert.Equal(2, cache.CaughtPokemon.Count);
            Assert.Equal(caughtF1, Assert.Single(cache.GameLevelCache["F1"].CaughtPokemon));
            Assert.Equal(caughtF2, Assert.Single(cache.GameLevelCache["F2"].CaughtPokemon));
        }

        [Fact]
        public void BuildGameCache_CaughtWithoutGameFileName_CountsForGameAndPhaseOnly()
        {
            // Documents written before 2026-05-01 have no GameFileName and load with it null.
            var legacy = new CaughtPokemon() { Pokemon = new PokemonData() { FormCode = "a", Species = 1 }, Game = GameFlavor.HGSS, Phase = "P1", GameFileName = null! };
            var empty = new CaughtPokemon() { Pokemon = new PokemonData() { FormCode = "a", Species = 1 }, Game = GameFlavor.HGSS, Phase = "P1", GameFileName = "" };
            var withFile = new CaughtPokemon() { Pokemon = new PokemonData() { FormCode = "a", Species = 1 }, Game = GameFlavor.HGSS, Phase = "P1", GameFileName = "F1" };

            var cache = PokemonPersistenceService.BuildGameCache(
                new List<CountRow> { new("P1", "F1", "a", 1, 10, 0) },
                new List<ProgressRow>(),
                new List<CaughtPokemon> { legacy, empty, withFile },
                new List<PokemonData>());

            // game and phase: all three catches; file F1: only its own
            AssertCounts(cache.AllPokemonCounts, 10, 0, 3);
            AssertCounts(cache.PokemonEncounterCache["a"], 10, 0, 3);
            AssertCounts(cache.PokemonEncounterCache["#1"], 10, 0, 3);
            AssertCounts(cache.PhaseEncounterCache["P1"].AllPokemonCounts, 10, 0, 3);
            AssertCounts(cache.PhaseEncounterCache["P1"].PokemonEncounterCache["a"], 10, 0, 3);
            Assert.Equal(3, cache.CaughtPokemon.Count);
            Assert.Equal(new[] { "F1" }, cache.GameLevelCache.Keys);
            AssertCounts(cache.GameLevelCache["F1"].AllPokemonCounts, 10, 0, 1);
            Assert.Equal(withFile, Assert.Single(cache.GameLevelCache["F1"].CaughtPokemon));
        }

        [Fact]
        public void EnsureKeys_MissingKey_CreatesZeroNodeThatTakesAnIncrement()
        {
            var cache = PokemonPersistenceService.BuildGameCache(
                new List<CountRow> { new("P1", "F1", "a", 1, 10, 0) },
                new List<ProgressRow>(),
                new List<CaughtPokemon>(),
                new List<PokemonData>());
            // TID 100 keeps ShinyValue at 100; a default PokemonData has ShinyValue 0 and reads as shiny.
            var encounter = new PersistPokemonData()
            {
                PokemonData = new PokemonData() { FormCode = "z", Species = 9, TID = 100 },
                Game = GameFlavor.HGSS,
                Phase = "P9",
                GameFileName = "F9"
            };

            PokemonPersistenceService.EnsureKeys(cache, new CacheLoadRequest(encounter));

            AssertCounts(cache.PokemonEncounterCache["z"], 0, 0, 0);
            AssertCounts(cache.PokemonEncounterCache["#9"], 0, 0, 0);
            AssertCounts(cache.PhaseEncounterCache["P9"].AllPokemonCounts, 0, 0, 0);
            AssertCounts(cache.PhaseEncounterCache["P9"].PokemonEncounterCache["z"], 0, 0, 0);
            AssertCounts(cache.GameLevelCache["F9"].AllPokemonCounts, 0, 0, 0);
            AssertCounts(cache.GameLevelCache["F9"].PokemonEncounterCache["z"], 0, 0, 0);
            AssertCounts(cache.GameLevelCache["F9"].PhaseEncounterCache["P9"].PokemonEncounterCache["#9"], 0, 0, 0);

            cache.Increment(encounter);

            AssertCounts(cache.AllPokemonCounts, 11, 0, 0);
            AssertCounts(cache.PokemonEncounterCache["z"], 1, 0, 0);
            AssertCounts(cache.PokemonEncounterCache["#9"], 1, 0, 0);
            AssertCounts(cache.PhaseEncounterCache["P9"].AllPokemonCounts, 1, 0, 0);
            AssertCounts(cache.PhaseEncounterCache["P9"].PokemonEncounterCache["z"], 1, 0, 0);
            Assert.Equal(1, cache.PhaseEncounterCache["P9"].PhaseCount);
            AssertCounts(cache.GameLevelCache["F9"].AllPokemonCounts, 1, 0, 0);
            AssertCounts(cache.GameLevelCache["F9"].PokemonEncounterCache["z"], 1, 0, 0);
            AssertCounts(cache.GameLevelCache["F9"].PhaseEncounterCache["P9"].PokemonEncounterCache["#9"], 1, 0, 0);
            Assert.Equal(1, cache.GameLevelCache["F9"].PhaseEncounterCache["P9"].PhaseCount);
        }

        [Fact]
        public void BuildGameCache_PlacesPhaseProgressAtPhaseAndFileLevel()
        {
            var counts = new List<CountRow>
            {
                new("P1", "F1", "a", 1, 4, 1),
                new("P1", "F2", "a", 1, 6, 1),
                new("P2", "F1", "b", 2, 8, 0),
            };
            var progress = new List<ProgressRow>
            {
                new("P1", null, 5),
                new("P1", "F1", 2),
                new("P1", "F2", 3),
            };

            var cache = PokemonPersistenceService.BuildGameCache(counts, progress, new List<CaughtPokemon>(), new List<PokemonData>());

            Assert.Equal(5, cache.PhaseEncounterCache["P1"].PhaseCount);
            Assert.Equal(2, cache.GameLevelCache["F1"].PhaseEncounterCache["P1"].PhaseCount);
            Assert.Equal(3, cache.GameLevelCache["F2"].PhaseEncounterCache["P1"].PhaseCount);
            Assert.Equal(0, cache.PhaseEncounterCache["P2"].PhaseCount);
            Assert.Equal(0, cache.GameLevelCache["F1"].PhaseEncounterCache["P2"].PhaseCount);
        }

        [Fact]
        public void BuildGameCache_SpeciesKeySumsFormsOfOneSpecies()
        {
            var counts = new List<CountRow>
            {
                new("P1", "F1", "201-a", 201, 5, 1),
                new("P1", "F1", "201-b", 201, 7, 0),
            };

            var cache = PokemonPersistenceService.BuildGameCache(counts, new List<ProgressRow>(), new List<CaughtPokemon>(), new List<PokemonData>());

            // #201 = 5+7 encounters, 1+0 shinies
            AssertCounts(cache.PokemonEncounterCache["#201"], 12, 1, 0);
            AssertCounts(cache.PokemonEncounterCache["201-a"], 5, 1, 0);
            AssertCounts(cache.PokemonEncounterCache["201-b"], 7, 0, 0);
            AssertCounts(cache.PhaseEncounterCache["P1"].PokemonEncounterCache["#201"], 12, 1, 0);
            AssertCounts(cache.PhaseEncounterCache["P1"].PokemonEncounterCache["201-a"], 5, 1, 0);
            AssertCounts(cache.PhaseEncounterCache["P1"].PokemonEncounterCache["201-b"], 7, 0, 0);
            AssertCounts(cache.GameLevelCache["F1"].PokemonEncounterCache["#201"], 12, 1, 0);
            AssertCounts(cache.GameLevelCache["F1"].PokemonEncounterCache["201-a"], 5, 1, 0);
            AssertCounts(cache.GameLevelCache["F1"].PokemonEncounterCache["201-b"], 7, 0, 0);
            AssertCounts(cache.GameLevelCache["F1"].PhaseEncounterCache["P1"].PokemonEncounterCache["#201"], 12, 1, 0);
        }
    }
}
