using ShinyBotHunter.Pokemon;

namespace ShinyBotHunter.BotEvents.Target
{
    /// <summary>
    /// Nothing encountered is ever a target, so the bot never parks on a catch. Used for runs whose
    /// only goal is the battle requirement (training to a level), which is what ends the event.
    /// </summary>
    public class NoTarget : ITarget
    {
        public Task<bool> IsTargetAsync(PokemonData pokemon)
        {
            return Task.FromResult(false);
        }

        public bool UsesMilestone => false;
    }
}
