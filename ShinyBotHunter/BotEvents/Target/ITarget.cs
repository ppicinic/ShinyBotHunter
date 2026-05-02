using ShinyBotHunter.Pokemon;

namespace ShinyBotHunter.BotEvents.Target
{
    public interface ITarget
    {
        Task<bool> IsTargetAsync(PokemonData pokemon);
    }
}
