using ShinyBotHunter.Service;

namespace ShinyBotHunter.Games
{
    public sealed record EventRegistration(
        string Name,
        EventArgumentOptions Options,
        // Null needs nothing. The name travels with the predicate because the refusal has to say
        // which capability is missing.
        (string Capability, Func<GameCapabilities, bool> Has)? Requires,
        bool OwnsEmulators,
        Func<BotService, NewEventArguments, int, Task<Dictionary<int, EmulatorState>>> Create);
}
