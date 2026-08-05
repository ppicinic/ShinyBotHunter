using ShinyBotHunter.Pokemon;

namespace ShinyBotHunter.BotEvents.Target
{
    public interface ITarget
    {
        Task<bool> IsTargetAsync(PokemonData pokemon);

        /// <summary>
        /// Whether this target's completion is governed by the milestone checklist. Targets that
        /// answer false have no milestone to finish, so the event has to stop on something else
        /// (today: the training requirement).
        /// </summary>
        bool UsesMilestone { get; }
    }
}
