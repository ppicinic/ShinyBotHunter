using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    public class MilestoneLocationTests
    {
        // Production's 20 phase documents as stored (read-only export, 2026-11-25).
        private static List<PhaseInfo> ProductionPhases() => BsonSerializer.Deserialize<List<PhaseInfo>>(
            File.ReadAllText(Path.Combine(RepoPaths.Root, "ShinyBotHunter.Tests", "Fixtures", "production-phases.json")));

        [Fact]
        public void StoredMilestones_WithoutNewFields_ReadAsToday()
        {
            var stored = BsonSerializer.Deserialize<List<BsonDocument>>(
                File.ReadAllText(Path.Combine(RepoPaths.Root, "ShinyBotHunter.Tests", "Fixtures", "production-phases.json")));

            var phases = ProductionPhases();

            Assert.Equal(20, phases.Count);
            Assert.All(stored, d => Assert.False(d.Contains("Location") || d.Contains("GameFiles") || d.Contains("Archived")));
            Assert.All(phases, p => Assert.Null(p.Location));
            Assert.All(phases, p => Assert.Empty(p.GameFiles));
            Assert.All(phases, p => Assert.False(p.Archived));
            Assert.Equal(stored.Select(d => d["Pokemon"]), phases.Select(p => p.ToBsonDocument()["Pokemon"]));
        }

        [Fact]
        public void Milestone_BsonRoundTrip_KeepsLocationFilesArchived()
        {
            var phase = ProductionPhases().Single(p => p.Name == "1-4 Route 30");
            phase.Location = "34-land";
            phase.GameFiles = ["Pokemon SoulSilver", "Pokemon HeartGold"];
            phase.Archived = true;

            var loaded = BsonSerializer.Deserialize<PhaseInfo>(phase.ToBsonDocument());

            Assert.Equal("34-land", loaded.Location);
            Assert.Equal(["Pokemon SoulSilver", "Pokemon HeartGold"], loaded.GameFiles);
            Assert.True(loaded.Archived);
        }

        [Fact]
        public void WithLocation_AttachThenDetach_ChangesNoRowOrAmount()
        {
            foreach (var phase in ProductionPhases())
            {
                var before = phase.ToBsonDocument();

                var attached = PhaseInfoService.WithLocation(phase, "34-land");
                var detached = PhaseInfoService.WithLocation(attached, null);

                var expected = before.DeepClone().AsBsonDocument;
                expected["Location"] = "34-land";
                Assert.Equal(expected, attached.ToBsonDocument());
                Assert.Equal(before, detached.ToBsonDocument());
                Assert.Equal(before, phase.ToBsonDocument());
            }
        }
    }
}
