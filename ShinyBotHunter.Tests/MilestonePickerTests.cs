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

        private static PhaseInfo Located(string name, string location, params PhasePokemonInfo[] rows) => new() { Name = name, Location = location, Pokemon = [.. rows] };

        private static PhasePokemonInfo Row(string name, int species, int amount)
            => new() { Name = name, SpeciesId = species, Amount = amount, FormCode = name.ToLowerInvariant() };

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
                        Assert.Equal(expected, OrchestratorEvent.IsPhaseComplete(phase.Pokemon, caught));
                        cases++;
                        complete += expected ? 1 : 0;
                    }
                }
            }
            Assert.True(cases > 40 && complete > 0 && complete < cases, $"{complete} complete in {cases} cases");
        }

        // FR-42: the 20 have no location, no ticked file and are not archived, so each is on every file, hunted where the bot stands.
        [Fact]
        public void Picker_ProductionPhases_ListedOnEveryFile()
        {
            var phases = ProductionPhases(Metadata());
            var versions = new[] { (GameFlavor.HGSS, Hgss, RomVersion.SoulSilver), (GameFlavor.RS, Rs, RomVersion.Sapphire) };

            foreach (var (game, milestones, version) in versions)
            {
                var stored = phases.Where(p => p.Game == game).ToList();
                var caught = new List<CaughtPokemon> { Caught("Pidgey", Pidgey, "pidgey") };

                var list = MilestonePicker.List(stored, milestones, version, new GameFile { Id = File }, caught, null);

                Assert.Equal(stored.Select(p => p.Name).Order(), list.Select(e => e.Name).Order());
                Assert.All(list, e =>
                {
                    Assert.Equal((null, true, null, false), (e.Modality, e.Huntable, e.Anchor, e.AnchorSet));
                    Assert.Equal(MainIsComplete(stored.Single(p => p.Name == e.Name), caught), e.Complete);
                });
            }
            Assert.Equal(20, phases.Count);
        }

        [Fact]
        public void Picker_ProductionNamesInTheOwnersOrder()
        {
            var hgss = ProductionPhases(Metadata()).Where(p => p.Game == GameFlavor.HGSS).Reverse().ToList();

            var list = MilestonePicker.List(hgss, null, null, new GameFile { Id = File }, [], null);

            Assert.Equal(
            [
                "0c Training", "1-1 Starters", "1-2 Route 29", "1-2c Route 29", "1-3 Route 46", "1-3b Route 46", "1-4 Route 30",
                "1-4b Route 30", "1-4c Route 30", "1-6 Dark Cave", "1-6b Dark Cave", "1-8 Route 32", "1-8b Route 32", "1-9 Sprout Tower",
                "1-10 Ruins of Alph", "2-1c Togepi", "2-2c Slowpoke Well", "2-3c Azalea Town Headbutt"
            ], list.Select(e => e.Name));
        }

        [Fact]
        public void Picker_GeneratedMilestonesNeverListed()
        {
            Assert.Equal(["X"], MilestonePicker.List([Phase("X")], Hgss, RomVersion.SoulSilver, new GameFile { Id = File }, [], null).Select(e => e.Name));
            Assert.Empty(MilestonePicker.List([], Hgss, RomVersion.SoulSilver, new GameFile { Id = File }, [], null));
        }

        [Fact]
        public void Picker_GameFiles_TickedFileOnly_NoneTickedEveryFile()
        {
            var x = Phase("X");
            x.GameFiles = ["A"];
            PhaseInfo[] phases = [x, Phase("Y")];

            Assert.Equal(["X", "Y"], MilestonePicker.List(phases, null, null, new GameFile { Id = "A" }, [], null).Select(e => e.Name));
            Assert.Equal(["Y"], MilestonePicker.List(phases, null, null, new GameFile { Id = "B" }, [], null).Select(e => e.Name));
        }

        [Fact]
        public void Picker_Archived_LeftOut_TargetUnchanged()
        {
            var plain = Located("1-4 Route 30", "34-land", Row("Pidgey", Pidgey, 1));
            var archived = Located("1-4 Route 30", "34-land", Row("Pidgey", Pidgey, 1));
            archived.Archived = true;
            var pidgey = new PokemonData { Name = "Pidgey", Species = Pidgey, FormCode = "pidgey" };

            Assert.Single(MilestonePicker.List([plain], Hgss, RomVersion.SoulSilver, new GameFile { Id = File }, [], null));
            Assert.Empty(MilestonePicker.List([archived], Hgss, RomVersion.SoulSilver, new GameFile { Id = File }, [], null));
            Assert.True(PhaseTarget.IsPhaseTarget(MilestoneRows.Counted(plain, Hgss, RomVersion.SoulSilver), [], pidgey));
            Assert.True(PhaseTarget.IsPhaseTarget(MilestoneRows.Counted(archived, Hgss, RomVersion.SoulSilver), [], pidgey));
        }

        [Fact]
        public void Picker_Anchor_GeneratedAndReplaced()
        {
            var replacement = new Anchor(34, 546, 300, 2, 0);
            var replaced = new GameFile { Id = File, Anchors = [new MilestoneAnchor("1-4 Route 30", replacement, Direction.Left)] };
            PhaseInfo[] phases = [Located("1-4 Route 30", "34-land"), Phase("X")];

            var before = MilestonePicker.List(phases, Hgss, RomVersion.SoulSilver, new GameFile { Id = File }, [], null);
            var after = MilestonePicker.List(phases, Hgss, RomVersion.SoulSilver, replaced, [], null);

            Assert.Equal((Modality.Land, new Anchor(34, 547, 304, 0, 6), false), (before[0].Modality, before[0].Anchor, before[0].AnchorSet));
            Assert.Equal((replacement, true), (after[0].Anchor, after[0].AnchorSet));
            Assert.Equal((null, false), (before[1].Anchor, before[1].AnchorSet));
            Assert.Equal(before[1], after[1]);
        }

        // FR-21 (#567): the owner's HG-Headbutt ID (15613, 35374) wanting Heracross gets the file's Azalea tree; no ID, no anchor.
        [Fact]
        public void List_HeadbuttMilestone_AnchorIsTheFilesTree()
        {
            const int heracross = 214;
            const uint ownersId = 2318286077;
            var azalea = Hgss[RomVersion.HeartGold].Single(m => m.Id == "74-headbutt");
            PhaseInfo[] phases = [Located("2-3c Azalea Town Headbutt", "74-headbutt", Row("Heracross", heracross, 1))];

            var entry = Assert.Single(MilestonePicker.List(phases, Hgss, RomVersion.HeartGold, new GameFile { Id = File }, [], ownersId));
            var noId = Assert.Single(MilestonePicker.List(phases, Hgss, RomVersion.HeartGold, new GameFile { Id = File }, [], null));

            Assert.NotNull(entry.Anchor);
            Assert.Equal((HeadbuttTrees.AnchorFor(azalea, ownersId, new HashSet<int> { heracross })!.Anchor, false), (entry.Anchor, entry.AnchorSet));
            Assert.Equal((null, false), (noId.Anchor, noId.AnchorSet));
        }

        [Fact]
        public void Picker_Complete_ByItsOwnAmounts()
        {
            PhaseInfo[] phases = [Located("1-4 Route 30", "34-land", Row("Pidgey", Pidgey, 1))];

            bool Complete(List<CaughtPokemon> caught) => MilestonePicker.List(phases, Hgss, RomVersion.SoulSilver, new GameFile { Id = File }, caught, null).Single().Complete;

            Assert.False(Complete([]));
            Assert.False(Complete([Caught("Pidgey", Pidgey, "pidgey", "Pokemon HeartGold")]));
            Assert.True(Complete([Caught("Pidgey", Pidgey, "pidgey")]));
        }

        [Fact]
        public void Picker_UnhuntableLocation_HuntableFalse()
        {
            var entry = Assert.Single(MilestonePicker.List([Located("Route 30 surf", "34-surf")], Hgss, RomVersion.SoulSilver, new GameFile { Id = File }, [], null));

            Assert.Equal((Modality.Surf, false), (entry.Modality, entry.Huntable));
        }

        [Fact]
        public void OffFile_Archived_Refused()
        {
            PhaseInfo archived = Phase("Route 30 Night");
            archived.Archived = true;

            Assert.Equal("Route 30 Night is archived.", BotService.OffFileRefusal(archived, File));
        }

        [Fact]
        public void OffFile_TickedForAnotherFile_Refused_TickedForThisOne_Accepted()
        {
            PhaseInfo ticked = Phase("Route 30 Night");
            ticked.GameFiles = ["other.nds"];

            Assert.Equal($"Route 30 Night is not ticked for {File}.", BotService.OffFileRefusal(ticked, File));
            ticked.GameFiles.Add(File);
            Assert.Null(BotService.OffFileRefusal(ticked, File));
        }

        // The owner's existing milestones have no ticks, so they are offered and hunted on every file; a name that is no saved
        // milestone is left to the checks after this one.
        [Fact]
        public void OffFile_NoTicks_AcceptedOnAnyFile()
        {
            Assert.Null(BotService.OffFileRefusal(Phase("Route 30 Night"), File));
            Assert.Null(BotService.OffFileRefusal(Phase("Route 30 Night"), "other.nds"));
            Assert.Null(BotService.OffFileRefusal(null, File));
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
    }
}
