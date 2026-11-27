using ShinyBotHunter.Games;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.BotEvents.Target
{
    public class PhaseTarget : ITarget
    {
        // Set once in the constructor, as the fields below are: the saved milestone's rows that count on this file.
        private readonly List<PhasePokemonInfo> rows;
        private PokemonPersistenceService persistenceService;
        private GameFlavor game;
        private string gameFileName;
        public PhaseTarget(IServiceProvider provider, GameFlavor game, string phase, string gameFileName)
        {
            var phaseInfo = provider.GetRequiredService<PhaseInfoService>().GetPhaseInfo(game, phase);
            persistenceService = provider.GetRequiredService<PokemonPersistenceService>();
            if(phaseInfo is null)
            {
                throw new ArgumentException($"{game.ToString()}:{phase} does not exist");
            }
            rows = MilestoneRows.Counted(phaseInfo, provider.GetRequiredService<GameProfiles>().Get(game).Milestones,
                provider.GetRequiredService<RomInfoService>().GetByHandle(gameFileName)?.Version);
            this.game = game;
            this.gameFileName = gameFileName;
        }
        public bool UsesMilestone => true;

        public async Task<bool> IsTargetAsync(PokemonData pokemon)
        {
            if (!pokemon.IsShiny)
            {
                return false;
            }
            var caughtPokemon = (await persistenceService.GetCaughtPokemonAsync(game)).Where(x => x.GameFileName == gameFileName);
            return IsPhaseTarget(rows, caughtPokemon, pokemon);
        }

        // The generated milestone of the file's version with this id, or null when there is no id, the file is not scanned or the
        // flavor has no milestones.
        internal static Milestone? FindMilestone(IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>>? milestones, RomVersion? version, string? id)
            => id is not null && version is not null && milestones is not null && milestones.TryGetValue(version.Value, out var versionMilestones)
                ? versionMilestones.SingleOrDefault(m => m.Id == id)
                : null;

        // Today's phase rule, unchanged (FR-42): rows matched by name, and caught counted without an IsShiny filter.
        internal static bool IsPhaseTarget(IEnumerable<PhasePokemonInfo> rows, IEnumerable<CaughtPokemon> caughtPokemon, PokemonData pokemon)
        {
            foreach (var targetPokemon in rows.Where(x => x.Name == pokemon.Name))
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
    }
}
