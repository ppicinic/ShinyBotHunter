using MongoDB.Bson.Serialization;
using PKHeX.Core;
using ShinyBotHunter.BotEvents.Target;
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
    }
}
