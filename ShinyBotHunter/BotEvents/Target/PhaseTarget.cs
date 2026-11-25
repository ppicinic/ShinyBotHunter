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
        private PhaseInfo? phaseInfo;
        private PokemonPersistenceService persistenceService;
        private GameFlavor game;
        private string gameFileName;
        // Set once in the constructor, as the fields above are: the generated milestone when no phase has the name, else null.
        private readonly Milestone? milestone;
        // Set once in the constructor; the DAO is thread-safe.
        private readonly MongoGameFileDao gameFileDao;
        public PhaseTarget(IServiceProvider provider, GameFlavor game, string phase, string gameFileName)
        {
            var phaseInfoTemp = provider.GetRequiredService<PhaseInfoService>().GetPhaseInfo(game, phase);
            persistenceService = provider.GetRequiredService<PokemonPersistenceService>();
            gameFileDao = provider.GetRequiredService<MongoGameFileDao>();
            // A stored phase is looked up first, so every stored phase takes today's path whatever its name (FR-42).
            if(phaseInfoTemp is null)
            {
                milestone = FindMilestone(provider.GetRequiredService<GameProfiles>().Get(game).Milestones,
                    provider.GetRequiredService<RomInfoService>().GetByHandle(gameFileName)?.Version, phase)
                    ?? throw new ArgumentException($"{game.ToString()}:{phase} does not exist");
            }
            phaseInfo = phaseInfoTemp;
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
            if (milestone is not null)
            {
                return IsGeneratedTarget(milestone.Id, await gameFileDao.LoadAsync(gameFileName), await persistenceService.GetCaughtPokemonAsync(game), pokemon);
            }
            var caughtPokemon = (await persistenceService.GetCaughtPokemonAsync(game)).Where(x => x.GameFileName == gameFileName);
            return IsPhaseTarget(phaseInfo!, caughtPokemon, pokemon);
        }


        // The generated milestone of the file's version with this id, or null when the file is not scanned or the flavor has no milestones.
        internal static Milestone? FindMilestone(IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>>? milestones, RomVersion? version, string id)
            => version is not null && milestones is not null && milestones.TryGetValue(version.Value, out var versionMilestones)
                ? versionMilestones.SingleOrDefault(m => m.Id == id)
                : null;

        // FR-13: the first goal entry the shiny matches decides, whether or not the milestone's table has the species (a swarm).
        internal static bool IsGeneratedTarget(string milestoneId, GameFile gameFile, IEnumerable<CaughtPokemon> caughtPokemon, PokemonData pokemon)
        {
            var entry = gameFile.Entries.FirstOrDefault(e => e.Matches(pokemon));
            return entry is not null && entry.Amount > 0 && entry.CaughtIn(caughtPokemon, gameFile.Id) < entry.Amount
                && !gameFile.Hidden.Contains(new HiddenRow(milestoneId, entry.Key));
        }

        // Today's phase rule, unchanged (FR-42): rows matched by name, and caught counted without an IsShiny filter.
        internal static bool IsPhaseTarget(PhaseInfo phaseInfo, IEnumerable<CaughtPokemon> caughtPokemon, PokemonData pokemon)
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
    }
}
