using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;

namespace ShinyBotHunter.Tests
{
    public class GoalPresetsTests
    {
        private const int Zubat = 41;

        // TID 0, SID 0 and PID 0 give shiny value 0; PID 0x00080000 gives 8, not shiny.
        private static PokemonData Pokemon(int species, string formCode, bool shiny = true)
            => new PokemonData { Species = (ushort)species, FormCode = formCode, PID = shiny ? 0u : 0x00080000u };

        [Fact]
        public void GameFile_BsonRoundTrip_KeepsEntriesAndHidden()
        {
            var file = new GameFile
            {
                Id = "Pokemon SoulSilver2",
                Entries = [new GoalEntry(Zubat, "zubat;f", 2), new GoalEntry(Zubat, "zubat", 0), new GoalEntry(16, null, 3)],
                Hidden = [new HiddenRow("34-land", "#16")]
            };

            BsonDocument document = file.ToBsonDocument();
            var loaded = BsonSerializer.Deserialize<GameFile>(document);

            Assert.Equal("Pokemon SoulSilver2", document["_id"].AsString);
            Assert.False(document["Entries"][0].AsBsonDocument.Contains("Key"));
            Assert.Equal(file.Id, loaded.Id);
            Assert.Equal(file.Entries, loaded.Entries);
            Assert.Equal(file.Hidden, loaded.Hidden);
        }

        [Fact]
        public void GoalEntry_Matches_SpeciesAndFormWhenSet()
        {
            var anyForm = new GoalEntry(Zubat, null, 3);
            var female = new GoalEntry(Zubat, "zubat;f", 2);

            Assert.Equal("#41", anyForm.Key);
            Assert.Equal("zubat;f", female.Key);
            Assert.True(anyForm.Matches(Pokemon(Zubat, "zubat")));
            Assert.True(anyForm.Matches(Pokemon(Zubat, "zubat;f")));
            Assert.True(female.Matches(Pokemon(Zubat, "zubat;f")));
            Assert.False(female.Matches(Pokemon(Zubat, "zubat")));
            Assert.False(anyForm.Matches(Pokemon(42, "golbat")));
        }

        [Fact]
        public void GoalEntry_CaughtIn_CountsShiniesOfThisFileThatMatch()
        {
            var female = new GoalEntry(Zubat, "zubat;f", 2);
            CaughtPokemon Caught(PokemonData pokemon, string file) => new CaughtPokemon { Pokemon = pokemon, Phase = "1-4", GameFileName = file };
            var caught = new[]
            {
                Caught(Pokemon(Zubat, "zubat;f"), "Pokemon SoulSilver"),
                Caught(Pokemon(Zubat, "zubat;f"), "Pokemon SoulSilver"),
                Caught(Pokemon(Zubat, "zubat;f", shiny: false), "Pokemon SoulSilver"),
                Caught(Pokemon(Zubat, "zubat"), "Pokemon SoulSilver"),
                Caught(Pokemon(Zubat, "zubat;f"), "Pokemon HeartGold"),
            };

            Assert.Equal(2, female.CaughtIn(caught, "Pokemon SoulSilver"));
        }
    }
}
