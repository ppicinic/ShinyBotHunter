using MongoDB.Bson.Serialization;
using PKHeX.Core;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Target;
using ShinyBotHunter.Games;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    public class MilestonePickerTests
    {
        private const string File = "Pokemon SoulSilver";
        private const int Pidgey = 16;

        private static readonly IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>> Hgss =
            MilestoneFile.Load(Path.Combine(RepoPaths.AppProject, "Data", "HGSS"));
        private static readonly IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>> Rs =
            MilestoneFile.Load(Path.Combine(RepoPaths.AppProject, "Data", "RS"));

        private static PokemonMetadataService Metadata()
        {
            var metadata = new PokemonMetadataService(null!);
            for (ushort id = 1; id <= 493; id++)
            {
                string name = SpeciesName.GetSpeciesName(id, (int)LanguageID.English);
                metadata.Seed(new PokemonMetadata { Id = id, PokemonName = name, PokeApiName = name.ToLowerInvariant() });
            }
            return metadata;
        }

        // Production's 20 phase documents as stored (read-only export), loaded as PhaseInfoService.InitAsync loads them.
        private static List<PhaseInfo> ProductionPhases(PokemonMetadataService metadata)
        {
            var phases = BsonSerializer.Deserialize<List<PhaseInfo>>(
                System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "ShinyBotHunter.Tests", "Fixtures", "production-phases.json")));
            phases.ForEach(p => PhaseInfoService.Normalize(p, metadata));
            return phases;
        }

        // TID 0, SID 0 and PID 0 give shiny value 0, so a Pokemon made here is shiny.
        private static CaughtPokemon Caught(string name, int species, string formCode, string gameFileName = File)
            => new() { Pokemon = new PokemonData { Name = name, Species = (ushort)species, FormCode = formCode }, Phase = "phase", GameFileName = gameFileName };

        private static PhaseInfo Phase(string name) => new() { Name = name, Pokemon = [] };

        // UpdateMilestoneState's loop, copied verbatim from OrchestratorEvent.cs at e5c50cd. It is the FR-42 oracle; do not edit it.
        private static bool MainIsComplete(PhaseInfo phaseInfo, List<CaughtPokemon> caughtPokemon)
        {
            bool complete = true;
            if (phaseInfo is not null)
            {
                foreach (var targetPokemon in phaseInfo.Pokemon)
                {
                    if (targetPokemon.RequireSpecificForm)
                    {
                        if (caughtPokemon.Where(x => x.Pokemon.IsShiny && x.Pokemon.FormCode == targetPokemon.FormCode).Count() < targetPokemon.Amount)
                        {
                            complete = false;
                        }
                    }
                    else
                    {
                        if (caughtPokemon.Where(x => x.Pokemon.IsShiny && x.Pokemon.Name == targetPokemon.Name).Count() < targetPokemon.Amount)
                        {
                            complete = false;
                        }
                    }
                }
            }
            return complete;
        }

        [Fact]
        public void ProductionPhases_SameCompletionAsMain()
        {
            var phases = ProductionPhases(Metadata());
            int cases = 0, complete = 0;

            Assert.Equal(20, phases.Count);
            foreach (var phase in phases)
            {
                foreach (var entry in phase.Pokemon.DefaultIfEmpty())
                {
                    foreach (int caughtCount in new[] { 0, (entry?.Amount ?? 0) - 1, entry?.Amount ?? 0 }.Where(n => n >= 0).Distinct())
                    {
                        // Every row caught to caughtCount, so a phase is complete only when each of its rows is.
                        var caught = phase.Pokemon.SelectMany(p => Enumerable.Range(0, Math.Min(caughtCount, p.Amount)).Select(_ => Caught(p.Name, p.SpeciesId, p.FormCode))).ToList();

                        bool expected = MainIsComplete(phase, caught);
                        Assert.Equal(expected, OrchestratorEvent.IsPhaseComplete(phase, caught));
                        cases++;
                        complete += expected ? 1 : 0;
                    }
                }
            }
            Assert.True(cases > 40 && complete > 0 && complete < cases, $"{complete} complete in {cases} cases");
        }

        [Fact]
        public void Picker_NoGoalNoSettings_ListsEveryStoredPhase()
        {
            var phases = ProductionPhases(Metadata());
            var versions = new[] { (GameFlavor.HGSS, RomVersion.SoulSilver, Hgss[RomVersion.SoulSilver], 398), (GameFlavor.RS, RomVersion.Sapphire, Rs[RomVersion.Sapphire], 226) };

            foreach (var (game, version, generated, generatedCount) in versions)
            {
                var stored = phases.Where(p => p.Game == game).ToList();
                var caught = new List<CaughtPokemon> { Caught("Pidgey", Pidgey, "pidgey") };

                var list = MilestonePicker.List(generated, stored, new GameFile { Id = File }, new HashSet<string>(), caught);

                var custom = list.Where(e => e.Modality is null).ToList();
                Assert.Equal(stored.Select(p => p.Name).Order(), custom.Select(e => e.Id).Order());
                Assert.All(custom, e =>
                {
                    Assert.Equal(e.Id, e.Name);
                    Assert.True(e.Huntable);
                    Assert.False(e.Archived);
                    Assert.Null(e.Order);
                    Assert.Equal(MainIsComplete(stored.Single(p => p.Name == e.Name), caught), e.Complete);
                });
                Assert.Equal(generatedCount, list.Count(e => e.Modality is not null));
            }
            Assert.Equal(20, phases.Count);
        }

        [Fact]
        public void Picker_NoLabels_ProductionNamesInTheOwnersOrder()
        {
            var hgss = ProductionPhases(Metadata()).Where(p => p.Game == GameFlavor.HGSS).Reverse().ToList();

            var list = MilestonePicker.List([], hgss, new GameFile { Id = File }, new HashSet<string>(), []);

            Assert.Equal(
            [
                "0c Training", "1-1 Starters", "1-2 Route 29", "1-2c Route 29", "1-3 Route 46", "1-3b Route 46", "1-4 Route 30",
                "1-4b Route 30", "1-4c Route 30", "1-6 Dark Cave", "1-6b Dark Cave", "1-8 Route 32", "1-8b Route 32", "1-9 Sprout Tower",
                "1-10 Ruins of Alph", "2-1c Togepi", "2-2c Slowpoke Well", "2-3c Azalea Town Headbutt"
            ], list.Select(e => e.Name));
        }

        [Fact]
        public void Picker_EveryGeneratedMilestone_PerVersion()
        {
            var versions = new[]
            {
                (Hgss[RomVersion.HeartGold], 398, 150), (Hgss[RomVersion.SoulSilver], 398, 150),
                (Rs[RomVersion.Ruby], 226, 46), (Rs[RomVersion.Sapphire], 226, 46)
            };

            foreach (var (generated, count, huntable) in versions)
            {
                var list = MilestonePicker.List(generated, [], new GameFile { Id = File }, new HashSet<string>(), []);

                Assert.Equal(count, list.Count);
                Assert.Equal(generated.Select(m => m.Id).Order(), list.Select(e => e.Id).Order());
                Assert.Equal(huntable, list.Count(e => e.Huntable));
            }
        }

        [Fact]
        public void Picker_UnassignedPhase_OnEveryFile_AssignedPhase_OnlyOnItsFiles()
        {
            PhaseInfo[] phases = [Phase("X"), Phase("Y")];
            var fileA = new GameFile { Id = "A", Assigned = ["X"] };
            var fileB = new GameFile { Id = "B" };
            var assigned = new HashSet<string> { "X" };

            Assert.Equal(["X", "Y"], MilestonePicker.List([], phases, fileA, assigned, []).Select(e => e.Id));
            Assert.Equal(["Y"], MilestonePicker.List([], phases, fileB, assigned, []).Select(e => e.Id));

            fileA.Assigned.Clear();
            Assert.Equal(["X", "Y"], MilestonePicker.List([], phases, fileA, new HashSet<string>(), []).Select(e => e.Id));
            Assert.Equal(["X", "Y"], MilestonePicker.List([], phases, fileB, new HashSet<string>(), []).Select(e => e.Id));
        }

        [Fact]
        public void Picker_Archived_FlaggedAndNothingElseChanges()
        {
            var generated = Hgss[RomVersion.SoulSilver];
            var milestone = generated.Single(m => m.Id == "34-land");
            var caught = new List<CaughtPokemon>();
            var plain = new GameFile { Id = File, Entries = [new GoalEntry(Pidgey, null, 1)] };
            var archived = new GameFile { Id = File, Entries = [new GoalEntry(Pidgey, null, 1)], Archived = ["34-land"] };

            var before = MilestonePicker.List(generated, [], plain, new HashSet<string>(), caught);
            var after = MilestonePicker.List(generated, [], archived, new HashSet<string>(), caught);

            Assert.True(after.Single(e => e.Id == "34-land").Archived);
            Assert.Equal(before.Single(e => e.Id == "34-land") with { Archived = true }, after.Single(e => e.Id == "34-land"));
            Assert.Equal(before.Where(e => e.Id != "34-land"), after.Where(e => e.Id != "34-land"));
            Assert.Equal(MilestoneRows.Build(milestone, plain, caught), MilestoneRows.Build(milestone, archived, caught));
            var pidgey = new PokemonData { Species = Pidgey, FormCode = "pidgey" };
            Assert.True(PhaseTarget.IsGeneratedTarget("34-land", plain, caught, pidgey));
            Assert.True(PhaseTarget.IsGeneratedTarget("34-land", archived, caught, pidgey));
        }

        [Fact]
        public void Picker_Anchor_GeneratedAndReplaced()
        {
            var generated = Hgss[RomVersion.SoulSilver];
            var replacement = new Anchor(34, 546, 300, 2, 0);
            var replaced = new GameFile { Id = File, Anchors = [new MilestoneAnchor("34-land", replacement, Direction.Left)] };

            var before = MilestonePicker.List(generated, [Phase("X")], new GameFile { Id = File }, new HashSet<string>(), []);
            var after = MilestonePicker.List(generated, [Phase("X")], replaced, new HashSet<string>(), []);

            Assert.Equal((new Anchor(34, 547, 304, 0, 6), false), (before.Single(e => e.Id == "34-land").Anchor, before.Single(e => e.Id == "34-land").AnchorSet));
            Assert.Equal((replacement, true), (after.Single(e => e.Id == "34-land").Anchor, after.Single(e => e.Id == "34-land").AnchorSet));
            Assert.Equal((null, false), (before.Single(e => e.Id == "X").Anchor, before.Single(e => e.Id == "X").AnchorSet));
            Assert.Equal(before.Where(e => e.Id != "34-land"), after.Where(e => e.Id != "34-land"));
        }

        [Fact]
        public void Picker_Order_LabelsFirstNaturalThenName()
        {
            PhaseInfo[] phases = [Phase("Echo"), Phase("Alpha"), Phase("Bravo"), Phase("Delta"), Phase("Charlie")];
            var file = new GameFile
            {
                Id = File,
                Order = [new MilestoneLabel("Alpha", "1-10"), new MilestoneLabel("Echo", "1-4"), new MilestoneLabel("Bravo", "2-2c")]
            };

            var list = MilestonePicker.List([], phases, file, new HashSet<string>(), []);

            Assert.Equal(["Echo", "Alpha", "Bravo", "Charlie", "Delta"], list.Select(e => e.Name));
            Assert.Equal(["1-4", "1-10", "2-2c", null, null], list.Select(e => e.Order));
        }

        [Fact]
        public void Picker_Complete_FollowsTheGoal()
        {
            var generated = Hgss[RomVersion.SoulSilver];
            var file = new GameFile { Id = File, Entries = [new GoalEntry(Pidgey, null, 1)] };

            bool Complete(List<CaughtPokemon> caught) => MilestonePicker.List(generated, [], file, new HashSet<string>(), caught).Single(e => e.Id == "34-land").Complete;

            Assert.False(Complete([]));
            Assert.False(Complete([Caught("Pidgey", Pidgey, "pidgey", "Pokemon HeartGold")]));
            Assert.True(Complete([Caught("Pidgey", Pidgey, "pidgey")]));
        }

        [Fact]
        public void NoHuntYet_SurfRodsRockSmash_Refused()
        {
            var soulSilver = Hgss[RomVersion.SoulSilver];
            var sapphire = Rs[RomVersion.Sapphire];
            var refused = new[] { "34-surf", "34-oldrod", "34-goodrod", "34-superrod" }.Select(id => soulSilver.Single(m => m.Id == id))
                .Append(soulSilver.First(m => m.Modality == Modality.RockSmash))
                .Append(sapphire.First(m => m.Modality == Modality.Surf));

            Assert.All(refused, m => Assert.Equal($"{m.Name}: no hunt yet.", BotService.NoHuntYet(m)));
            Assert.Null(BotService.NoHuntYet(soulSilver.Single(m => m.Id == "34-land")));
            Assert.Null(BotService.NoHuntYet(soulSilver.First(m => m.Modality == Modality.Headbutt)));
            Assert.Null(BotService.NoHuntYet(sapphire.First(m => m.Modality == Modality.Land)));
            Assert.Null(BotService.NoHuntYet(null));
        }

        [Fact]
        public void GeneratedComplete_ByRows_AndAnUnknownNameIsComplete()
        {
            var milestone = Hgss[RomVersion.SoulSilver].Single(m => m.Id == "34-land");
            var file = new GameFile { Id = File, Entries = [new GoalEntry(Pidgey, null, 1)] };

            Assert.False(OrchestratorEvent.IsGeneratedComplete(milestone, file, []));
            Assert.True(OrchestratorEvent.IsGeneratedComplete(milestone, file, [Caught("Pidgey", Pidgey, "pidgey")]));
            Assert.True(OrchestratorEvent.IsGeneratedComplete(milestone, new GameFile { Id = File }, []));
            Assert.True(OrchestratorEvent.IsGeneratedComplete(null, file, []));
        }
    }
}
