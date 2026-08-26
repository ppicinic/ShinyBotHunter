using ShinyBotHunter.Context;
using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Service
{
    /// <summary>
    /// Whether a ROM's SHA1 matches a known-good dump.
    ///
    /// The three states exist because "not verified" means two very different things. A Ruby ROM we
    /// have reference hashes for but cannot match is suspect — patched dumps redirect the entry point
    /// into injected code, which invalidates both the symbol-derived address tables and any legality
    /// analysis. An NDS ROM we simply hold no reference hashes for is not suspect at all, and must not
    /// be treated as such or HGSS would break.
    /// </summary>
    public enum RomVerification
    {
        /// <summary>SHA1 matched a known-good dump.</summary>
        Verified,
        /// <summary>We hold reference hashes for this game and this file matched none of them.</summary>
        Unrecognized,
        /// <summary>No reference hashes exist for this game, so nothing can be concluded.</summary>
        NoReferenceData
    }

    /// <summary>
    /// How well-exercised a detected revision is. Distinct from <see cref="RomVerification"/>: a
    /// revision can be a perfectly clean dump we have simply never recorded macros against.
    /// </summary>
    public enum RevisionSupport
    {
        /// <summary>The revision macros are recorded against and addresses are live-verified on.</summary>
        Tested,
        /// <summary>Runnable, but unproven — logged as a warning, never refused.</summary>
        SupportedUntested
    }

    /// <summary>
    /// A game's identity, derived from its ROM header. This — not the filename — is what says which
    /// game, flavor, console and revision an emulator is running.
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
        string Sha1,
        RomVerification Verification,
        RevisionSupport RevisionSupport)
    {
        /// <summary>True when the header maps to a flavor the bot has game logic for.</summary>
        public bool IsKnownGame => Flavor is not null;

        /// <summary>
        /// A clean dump as far as we can tell — either matched, or unknowable for lack of reference
        /// data. Only <see cref="RomVerification.Unrecognized"/> is a real red flag.
        /// </summary>
        public bool IsTrusted => Verification != RomVerification.Unrecognized;

        public string DisplayName => $"{GameName} ({Language}, rev{Revision})";
    }
}
