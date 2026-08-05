using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.BotEvents.Target
{
    public class PhaseTarget : ITarget
    {
        private PhaseInfo phaseInfo;
        private PokemonPersistenceService persistenceService;
        private GameFlavor game;
        private string gameFileName;
        public PhaseTarget(IServiceProvider provider, GameFlavor game, string phase, string gameFileName)
        {
            var phaseInfoTemp = provider.GetRequiredService<PhaseInfoService>().GetPhaseInfo(game, phase);
            persistenceService = provider.GetRequiredService<PokemonPersistenceService>();
            if(phaseInfoTemp is null)
            {
                throw new ArgumentException($"{game.ToString()}:{phase} does not exist");
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
            var caughtPokemon = (await persistenceService.GetCaughtPokemonAsync(game)).Where(x => x.GameFileName == gameFileName);
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
