using MongoDB.Bson.Serialization;
using PKHeX.Core;
using ShinyBotHunter.BotEvents.Target;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    public class PhaseTargetTests
    {
        private const string File = "Pokemon SoulSilver";

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

        // Production's 20 phase documents as stored (read-only export, 2026-11-25), loaded as PhaseInfoService.InitAsync loads them.
        private static List<PhaseInfo> ProductionPhases(PokemonMetadataService metadata)
        {
            var phases = BsonSerializer.Deserialize<List<PhaseInfo>>(
                System.IO.File.ReadAllText(Path.Combine(RepoPaths.Root, "ShinyBotHunter.Tests", "Fixtures", "production-phases.json")));
            phases.ForEach(p => PhaseInfoService.Normalize(p, metadata));
            return phases;
        }

        // TID 0, SID 0 and PID 0 give shiny value 0, so every Pokemon made here is shiny.
        private static PokemonData Shiny(string name, int species, string formCode)
            => new() { Name = name, Species = (ushort)species, FormCode = formCode };

        private static CaughtPokemon Caught(PokemonData pokemon) => new() { Pokemon = pokemon, Phase = "phase", GameFileName = File };

        // Main's loop, copied verbatim from PhaseTarget.cs lines 34-51 at b933e50. It is the FR-42 oracle; do not edit it.
        private static bool MainIsTarget(PhaseInfo phaseInfo, IEnumerable<CaughtPokemon> caughtPokemon, PokemonData pokemon)
        {
            foreach (var targetPokemon in phaseInfo.Pokemon.Where(x => x.Name == pokemon.Name))
            {
                if (targetPokemon.RequireSpecificForm)
                {
                    if(targetPokemon.FormCode == pokemon.FormCode)
                    {
                        return caughtPokemon.Count(x => x.Pokemon.FormCode == targetPokemon.FormCode) < targetPokemon.Amount; 
                    }
                }
                else // not form specific
                {
                    if (targetPokemon.Name == pokemon.Name)
                    {
                        return caughtPokemon.Count(x => x.Pokemon.Name == targetPokemon.Name) < targetPokemon.Amount;
                    }
                }
            }
            return false;
        }

        [Fact]
        public void ProductionPhases_SameTargetsAsMain()
        {
            var metadata = Metadata();
            var phases = ProductionPhases(metadata);
            int cases = 0, targets = 0;

            Assert.Equal(20, phases.Count);
            foreach (var phase in phases)
            {
                foreach (var entry in phase.Pokemon)
                {
                    string slug = metadata.GetPokemonMetadata(entry.SpeciesId).PokeApiName;
                    foreach (string formCode in new[] { entry.FormCode, slug, slug + ";f" }.Distinct())
                    {
                        foreach (int caughtCount in new[] { 0, entry.Amount - 1, entry.Amount }.Where(n => n >= 0).Distinct())
                        {
                            var caught = Enumerable.Range(0, caughtCount).Select(_ => Caught(Shiny(entry.Name, entry.SpeciesId, entry.FormCode))).ToList();
                            var pokemon = Shiny(entry.Name, entry.SpeciesId, formCode);

                            bool expected = MainIsTarget(phase, caught, pokemon);
                            Assert.Equal(expected, PhaseTarget.IsPhaseTarget(phase, caught, pokemon));
                            cases++;
                            targets += expected ? 1 : 0;
                        }
                    }
                }
            }
            Assert.True(cases > 200 && targets > 0 && targets < cases, $"{targets} targets in {cases} cases");
        }

        [Fact]
        public void ProductionPhases_ResolveToThePhasePath()
        {
            var phases = ProductionPhases(Metadata());
            var rs = MilestoneFile.Load(Path.Combine(RepoPaths.AppProject, "Data", "RS"));
            var versions = new Dictionary<GameFlavor, (IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>> Milestones, RomVersion[] Versions)>
            {
                [GameFlavor.HGSS] = (Hgss, [RomVersion.HeartGold, RomVersion.SoulSilver]),
                [GameFlavor.RS] = (rs, [RomVersion.Ruby, RomVersion.Sapphire])
            };

            // No stored phase name is a generated id of its own game's versions, so no stored phase can leave today's path
            // whichever lookup came first.
            Assert.Equal(versions.Keys.ToHashSet(), phases.Select(p => p.Game).ToHashSet());
            Assert.All(phases, p => Assert.All(versions[p.Game].Versions, v =>
            {
                Assert.NotEmpty(versions[p.Game].Milestones[v]);
                Assert.Null(PhaseTarget.FindMilestone(versions[p.Game].Milestones, v, p.Name));
            }));
            Assert.Equal("34-land", PhaseTarget.FindMilestone(Hgss, RomVersion.SoulSilver, "34-land")!.Id);
            Assert.Null(PhaseTarget.FindMilestone(Hgss, null, "34-land"));
            Assert.Null(PhaseTarget.FindMilestone(null, RomVersion.SoulSilver, "34-land"));
        }

        private static readonly IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>> Hgss =
            MilestoneFile.Load(Path.Combine(RepoPaths.AppProject, "Data", "HGSS"));

        private const int Pidgey = 16, Rattata = 19, Growlithe = 58;

        private static CaughtPokemon Caught(int species, string formCode, bool shiny = true)
            => new() { Pokemon = new PokemonData { Species = (ushort)species, FormCode = formCode, PID = shiny ? 0u : 0x00080000u }, Phase = "34-land", GameFileName = File };

        private static PokemonData Wild(int species, string formCode) => new() { Species = (ushort)species, FormCode = formCode };

        [Fact]
        public void Generated_ShinyWantedIncomplete_IsTarget()
        {
            var goal = new GameFile { Id = File, Entries = [new GoalEntry(Pidgey, null, 2)] };

            Assert.True(PhaseTarget.IsGeneratedTarget("34-land", goal, [Caught(Pidgey, "pidgey")], Wild(Pidgey, "pidgey")));
        }

        [Fact]
        public void Generated_AmountZero_NotTarget()
        {
            var goal = new GameFile { Id = File, Entries = [new GoalEntry(Pidgey, null, 0)] };

            Assert.False(PhaseTarget.IsGeneratedTarget("34-land", goal, [], Wild(Pidgey, "pidgey")));
            Assert.False(PhaseTarget.IsGeneratedTarget("34-land", new GameFile { Id = File }, [], Wild(Pidgey, "pidgey")));
        }

        [Fact]
        public void Generated_Complete_NotTarget()
        {
            var goal = new GameFile { Id = File, Entries = [new GoalEntry(Pidgey, null, 2)] };

            Assert.False(PhaseTarget.IsGeneratedTarget("34-land", goal, [Caught(Pidgey, "pidgey"), Caught(Pidgey, "pidgey")], Wild(Pidgey, "pidgey")));
        }

        [Fact]
        public void Generated_Hidden_NotTarget()
        {
            var goal = new GameFile { Id = File, Entries = [new GoalEntry(Pidgey, null, 2)], Hidden = [new HiddenRow("34-land", "#16")] };

            Assert.False(PhaseTarget.IsGeneratedTarget("34-land", goal, [], Wild(Pidgey, "pidgey")));
            Assert.True(PhaseTarget.IsGeneratedTarget("46-land", goal, [], Wild(Pidgey, "pidgey")));
        }

        [Fact]
        public void Generated_NonShinyCatch_NotCounted()
        {
            var goal = new GameFile { Id = File, Entries = [new GoalEntry(Pidgey, null, 1)] };

            Assert.True(PhaseTarget.IsGeneratedTarget("34-land", goal, [Caught(Pidgey, "pidgey", shiny: false)], Wild(Pidgey, "pidgey")));
        }

        [Fact]
        public void Generated_FormEntry_DecidesByForm()
        {
            var goal = new GameFile { Id = File, Entries = [new GoalEntry(Rattata, "rattata;f", 2), new GoalEntry(Rattata, "rattata", 0)] };

            Assert.True(PhaseTarget.IsGeneratedTarget("34-land", goal, [], Wild(Rattata, "rattata;f")));
            Assert.False(PhaseTarget.IsGeneratedTarget("34-land", goal, [], Wild(Rattata, "rattata")));
        }

        [Fact]
        public void Generated_SpeciesNotInTableButWanted_IsTarget()
        {
            var goal = new GameFile { Id = File, Entries = [new GoalEntry(Growlithe, null, 1)] };

            Assert.DoesNotContain(Hgss[RomVersion.SoulSilver].Single(m => m.Id == "34-land").Slots, s => s.Species == Growlithe);
            Assert.True(PhaseTarget.IsGeneratedTarget("34-land", goal, [], Wild(Growlithe, "growlithe")));
        }
    }
}
