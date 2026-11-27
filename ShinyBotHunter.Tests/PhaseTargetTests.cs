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
                            Assert.Equal(expected, PhaseTarget.IsPhaseTarget(phase.Pokemon, caught, pokemon));
                            cases++;
                            targets += expected ? 1 : 0;
                        }
                    }
                }
            }
            Assert.True(cases > 200 && targets > 0 && targets < cases, $"{targets} targets in {cases} cases");
        }

        [Fact]
        public void FindMilestone_ByLocationId()
        {
            Assert.Equal("34-land", PhaseTarget.FindMilestone(Hgss, RomVersion.SoulSilver, "34-land")!.Id);
            Assert.Null(PhaseTarget.FindMilestone(Hgss, null, "34-land"));
            Assert.Null(PhaseTarget.FindMilestone(null, RomVersion.SoulSilver, "34-land"));
            Assert.Null(PhaseTarget.FindMilestone(Hgss, RomVersion.SoulSilver, null));
        }

        private static readonly IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>> Hgss =
            MilestoneFile.Load(Path.Combine(RepoPaths.AppProject, "Data", "HGSS"));

        private const int Caterpie = 10, Weedle = 13, Pidgey = 16, Growlithe = 58;

        private static PhasePokemonInfo Row(string name, int species, int amount)
            => new() { Name = name, SpeciesId = species, Amount = amount, FormCode = name.ToLowerInvariant() };

        private static PhaseInfo Route30Milestone(params PhasePokemonInfo[] rows) => new() { Name = "1-4 Route 30", Pokemon = [.. rows], Location = "34-land" };

        // As PhaseTarget.IsTargetAsync decides it: the rows that count on a file of this version, and that file's catches.
        private static bool IsTarget(PhaseInfo phase, RomVersion version, IEnumerable<CaughtPokemon> caught, PokemonData pokemon)
            => PhaseTarget.IsPhaseTarget(MilestoneRows.Counted(phase, Hgss, version), caught.Where(c => c.GameFileName == File), pokemon);

        private static CaughtPokemon CaughtOn(string gameFileName, string name, int species)
            => new() { Pokemon = Shiny(name, species, name.ToLowerInvariant()), Phase = "1-4 Route 30", GameFileName = gameFileName };

        [Fact]
        public void Located_OwnAmounts_TargetUntilCaughtOnThisFile()
        {
            var phase = Route30Milestone(Row("Pidgey", Pidgey, 2));
            var pidgey = Shiny("Pidgey", Pidgey, "pidgey");

            Assert.True(IsTarget(phase, RomVersion.SoulSilver, [], pidgey));
            Assert.True(IsTarget(phase, RomVersion.SoulSilver, [CaughtOn(File, "Pidgey", Pidgey)], pidgey));
            Assert.False(IsTarget(phase, RomVersion.SoulSilver, [CaughtOn(File, "Pidgey", Pidgey), CaughtOn(File, "Pidgey", Pidgey)], pidgey));
            Assert.True(IsTarget(phase, RomVersion.SoulSilver, [CaughtOn("Pokemon HeartGold", "Pidgey", Pidgey), CaughtOn("Pokemon HeartGold", "Pidgey", Pidgey)], pidgey));
        }

        // Weedle is SoulSilver's on Route 30 and Caterpie HeartGold's (default 5).
        [Theory]
        [InlineData(RomVersion.HeartGold, false, true)]
        [InlineData(RomVersion.SoulSilver, true, false)]
        public void Located_OtherVersionRow_NotATarget(RomVersion version, bool weedle, bool caterpie)
        {
            var phase = Route30Milestone(Row("Weedle", Weedle, 1), Row("Caterpie", Caterpie, 1));

            Assert.Equal(weedle, IsTarget(phase, version, [], Shiny("Weedle", Weedle, "weedle")));
            Assert.Equal(caterpie, IsTarget(phase, version, [], Shiny("Caterpie", Caterpie, "caterpie")));
        }

        [Theory]
        [InlineData(RomVersion.HeartGold)]
        [InlineData(RomVersion.SoulSilver)]
        public void Located_HandAddedRow_StillATarget(RomVersion version)
        {
            Assert.DoesNotContain(Hgss[version].Single(m => m.Id == "34-land").Slots, s => s.Species == Growlithe);
            Assert.True(IsTarget(Route30Milestone(Row("Growlithe", Growlithe, 1)), version, [], Shiny("Growlithe", Growlithe, "growlithe")));
        }
    }
}
