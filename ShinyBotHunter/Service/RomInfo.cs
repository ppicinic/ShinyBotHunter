using ShinyBotHunter.Context;
using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Service
{
    /// <summary>
    /// How well-exercised a detected revision is. A revision that is not the tested one still runs —
    /// it is only flagged as unproven, so promoting one later is a matter of recording macros against
    /// it and verifying addresses, never a refactor.
    /// </summary>
    public enum RevisionSupport
    {
        /// <summary>The revision macros are recorded against and addresses are live-verified on.</summary>
        Tested,
        /// <summary>Runnable, but unproven — logged as a warning, never refused.</summary>
        SupportedUntested
    }

    /// <summary>
    /// A game's identity, read from its ROM header. This — not the filename — is what says which game,
    /// flavor, console and revision an emulator is running.
    ///
    /// <see cref="Handle"/> is the one filename-shaped field, and it means only "which file on disk":
    /// the bot copies a ROM per emulator instance so each gets its own save, so the handle identifies
    /// the copy, never the game.
    /// </summary>
    public record RomInfo(
        string Handle,
        string FilePath,
        string Extension,
        ConsoleType Console,
        GameFlavor? Flavor,
        string GameName,
        string GameCode,
        string Title,
        string Language,
        int Revision,
        RevisionSupport RevisionSupport)
    {
        /// <summary>True when the header maps to a flavor the bot has game logic for.</summary>
        public bool IsKnownGame => Flavor is not null;

        public string DisplayName => $"{GameName} ({Language}, rev{Revision})";
    }
}
