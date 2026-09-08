using ShinyBotHunter.Emulators;
using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Bot
{
    public interface IBot : ILauncher, ICommandClient, IMemoryClient, IDataReader
    {
        /// <summary>
        /// Which game this emulator is running, from its ROM header. Callers that need a flavor should
        /// ask the bot rather than assume one — that assumption is what kept the codebase single-game.
        /// </summary>
        GameFlavor GameFlavor { get; }
    }
}
