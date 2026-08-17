using ShinyBotHunter.Pokemon;

namespace ShinyBotHunter.BotEvents.Target
{
    public class ShinyTarget : ITarget
    {
        public Task<bool> IsTargetAsync(PokemonData pokemon)
        {
            return Task.FromResult(pokemon.IsShiny);
        }

        public bool UsesMilestone => true;
    }
}
