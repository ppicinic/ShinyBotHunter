using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Bot
{
    public interface IBot : ILauncher, ICommandClient, IMemoryClient, IDataReader
    {
        /// <summary>
        /// Which game this emulator is running, from its ROM header. Callers that need a flavor should
        /// ask the bot rather than assume one — that assumption is what kept the codebase single-game.
        /// </summary>
        GameFlavor GameFlavor { get; }

        /// <summary>The parts this bot's game uses, resolved once from its flavor.</summary>
        GameProfile Profile { get; }

        /// <summary>The address table for the launched ROM's revision.</summary>
        IPointerService Addresses { get; }

        /// <summary>The overworld reads. Throws InvalidOperationException for a flavor without them.</summary>
        IOverworldReader Overworld { get; }
    }
}
