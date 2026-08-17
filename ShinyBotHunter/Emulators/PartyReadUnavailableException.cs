namespace ShinyBotHunter.Emulators
{
    /// <summary>
    /// The emulator could not produce a party that passed validation within its frame budget.
    ///
    /// This is a normal outcome for the in-battle party, which only holds real data during a battle,
    /// so it is deliberately distinct from a transport failure: callers that poll can treat it as
    /// "not yet", while callers that require a party can let it propagate.
    /// </summary>
    public class PartyReadUnavailableException : Exception
    {
        public PartyReadUnavailableException(string message) : base(message) { }
    }
}
