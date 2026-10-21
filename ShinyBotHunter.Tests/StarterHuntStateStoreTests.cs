using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Tests
{
    public class StarterHuntStateStoreTests
    {
        private static readonly DateTime Noon = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void FullStateRoundTripsUnchanged()
        {
            var state = new StarterHuntGameState
            {
                Id = Guid.NewGuid(),
                Name = "RS Starters",
                Game = GameFlavor.RS,
                CompletedStarters = ["Treecko", "Torchic"],
                CompletedFinds =
                [
                    new CompletedFind
                    {
                        Starter = "Torchic",
                        CopyHandle = "copy-3",
                        RecordingName = "torchic.mp4",
                        PID = 12345u,
                        Species = 255,
                        CaughtUtc = Noon,
                    },
                ],
                CurrentStarter = "Mudkip",
                SavesCreated = ["copy-1", "copy-2"],
                RetiredCopies = ["copy-3", "copy-4"],
                PendingShinies =
                [
                    new PendingShiny
                    {
                        Starter = "Mudkip",
                        RomHandle = "copy-5",
                        RecordingName = "mudkip.mp4",
                        PID = 67890u,
                        Species = 258,
                        FoundUtc = Noon.AddMinutes(5),
                        PokemonData = null,
                    },
                ],
                StartedUtc = Noon.AddHours(-3),
                UpdatedUtc = Noon,
            };

            BsonDocument doc = state.ToBsonDocument();
            var loaded = BsonSerializer.Deserialize<StarterHuntGameState>(doc);

            Assert.Equal(state.Id, loaded.Id);
            Assert.Equal("RS Starters", loaded.Name);
            Assert.Equal(GameFlavor.RS, loaded.Game);
            Assert.Equal(state.CompletedStarters, loaded.CompletedStarters);
            Assert.Equal("Mudkip", loaded.CurrentStarter);
            Assert.Equal(state.SavesCreated, loaded.SavesCreated);
            Assert.Equal(state.RetiredCopies, loaded.RetiredCopies);
            Assert.Equal(state.StartedUtc, loaded.StartedUtc);
            Assert.Equal(state.UpdatedUtc, loaded.UpdatedUtc);

            CompletedFind find = Assert.Single(loaded.CompletedFinds);
            Assert.Equal("Torchic", find.Starter);
            Assert.Equal("copy-3", find.CopyHandle);
            Assert.Equal("torchic.mp4", find.RecordingName);
            Assert.Equal(12345u, find.PID);
            Assert.Equal((ushort)255, find.Species);
            Assert.Equal(Noon, find.CaughtUtc);

            PendingShiny pending = Assert.Single(loaded.PendingShinies);
            Assert.Equal("Mudkip", pending.Starter);
            Assert.Equal("copy-5", pending.RomHandle);
            Assert.Equal("mudkip.mp4", pending.RecordingName);
            Assert.Equal(67890u, pending.PID);
            Assert.Equal((ushort)258, pending.Species);
            Assert.Equal(Noon.AddMinutes(5), pending.FoundUtc);
            Assert.Null(pending.PokemonData);
        }

        [Fact]
        public void HighBitPidIsStoredAsInt64AndRoundTrips()
        {
            var state = new StarterHuntGameState
            {
                Id = Guid.NewGuid(),
                Name = "RS Starters",
                Game = GameFlavor.RS,
                CompletedFinds =
                [
                    new CompletedFind { Starter = "Torchic", CopyHandle = "copy-3", PID = 0xF00DBEEFu, CaughtUtc = Noon },
                ],
                PendingShinies =
                [
                    new PendingShiny { Starter = "Mudkip", RomHandle = "copy-5", RecordingName = "mudkip.mp4", PID = 0xF00DBEEFu, FoundUtc = Noon },
                ],
            };

            BsonDocument doc = state.ToBsonDocument();
            var loaded = BsonSerializer.Deserialize<StarterHuntGameState>(doc);

            Assert.Equal(BsonType.Int64, doc["PendingShinies"][0]["PID"].BsonType);
            Assert.Equal(BsonType.Int64, doc["CompletedFinds"][0]["PID"].BsonType);
            Assert.Equal(0xF00DBEEFu, loaded.PendingShinies[0].PID);
            Assert.Equal(0xF00DBEEFu, loaded.CompletedFinds[0].PID);
        }

        [Fact]
        public void IdIsStoredAsStandardGuid()
        {
            var state = new StarterHuntGameState { Id = Guid.NewGuid(), Name = "RS Starters", Game = GameFlavor.RS };

            BsonDocument doc = state.ToBsonDocument();
            var loaded = BsonSerializer.Deserialize<StarterHuntGameState>(doc);

            var id = Assert.IsType<BsonBinaryData>(doc["_id"]);
            Assert.Equal(BsonBinarySubType.UuidStandard, id.SubType);
            Assert.Equal(state.Id, loaded.Id);
        }

        [Fact]
        public void RetiredAndCreatedCopiesRoundTrip()
        {
            var state = new StarterHuntGameState
            {
                Id = Guid.NewGuid(),
                Name = "Created Saves",
                Game = GameFlavor.RS,
                SavesCreated = ["copy-1", "copy-2", "copy-3"],
                RetiredCopies = ["copy-3", "copy-1"],
            };

            var loaded = BsonSerializer.Deserialize<StarterHuntGameState>(state.ToBsonDocument());

            Assert.Equal(["copy-1", "copy-2", "copy-3"], loaded.SavesCreated);
            Assert.Equal(["copy-3", "copy-1"], loaded.RetiredCopies);
        }

        [Fact]
        public void DocumentWithoutCompletedFindsStillLoads()
        {
            var guid = Guid.NewGuid();
            var doc = new BsonDocument
            {
                { "_id", new BsonBinaryData(guid, GuidRepresentation.Standard) },
                { "Name", "RS Starters" },
                { "Game", (int)GameFlavor.RS },
                { "CompletedStarters", new BsonArray { "Treecko" } },
            };

            var loaded = BsonSerializer.Deserialize<StarterHuntGameState>(doc);

            Assert.NotNull(loaded.CompletedFinds);
            Assert.Empty(loaded.CompletedFinds);
            Assert.Equal(["Treecko"], loaded.CompletedStarters);
            Assert.Equal(guid, loaded.Id);
        }
    }
}
