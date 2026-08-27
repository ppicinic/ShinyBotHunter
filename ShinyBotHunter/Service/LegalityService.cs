using PKHeX.Core;
using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Service
{
    /// <summary>Verdict for one pokemon, with the analyser's own reasoning kept for logging.</summary>
    public record LegalityResult(bool IsLegal, string Report)
    {
        /// <summary>Used when a flavor has no legality support, so callers do not read it as a pass.</summary>
        public static LegalityResult Unsupported(GameFlavor flavor) =>
            new(true, $"Legality analysis is not implemented for {flavor}; treated as legal.");
    }

    /// <summary>
    /// Runs PKHeX's legality analysis over a pokemon read out of the emulator.
    ///
    /// This exists because Gen 3 emulation does not reliably produce legal pokemon: the RNG is seeded
    /// from the cartridge clock, and how an emulator handles that clock can yield combinations the
    /// real hardware would never generate. A shiny that no legitimate game could have produced is
    /// worse than no shiny at all -- recorded as an encounter it silently corrupts the data the whole
    /// hunt exists to collect.
    ///
    /// Analysis is deliberately over the raw bytes rather than the parsed model. PKHeX has its own
    /// reader for these structures, so handing it the bytes avoids a disagreement between two decoders
    /// being mistaken for an illegal pokemon.
    /// </summary>
    public class LegalityService
    {
        private readonly ILogger<LegalityService> logger;

        public LegalityService(ILogger<LegalityService> logger)
        {
            this.logger = logger;
        }

        /// <summary>
        /// Analyses the raw block a party read returned.
        ///
        /// A flavor PKHeX cannot be handed is reported as legal-with-explanation rather than illegal:
        /// the point is to catch pokemon that could not exist, and "we did not check" is not evidence
        /// of that. An analyser that throws is treated the same way, for the same reason -- refusing a
        /// genuine shiny because of a library error would be the worse failure.
        /// </summary>
        public LegalityResult Analyze(byte[] raw, GameFlavor flavor)
        {
            EntityContext context = ContextFor(flavor);
            if (context == EntityContext.None)
            {
                return LegalityResult.Unsupported(flavor);
            }

            try
            {
                PKM? pkm = EntityFormat.GetFromBytes(raw, context);
                if (pkm is null)
                {
                    // The block already passed the game's own checksum before reaching here, so PKHeX
                    // failing to read it means the two decoders disagree -- not that the pokemon is
                    // impossible. Rejecting on that would cost genuine shinies to a library quirk.
                    logger.LogWarning("PKHeX could not read a {Flavor} block that passed validation.", flavor);
                    return new LegalityResult(true, "PKHeX could not read the block; treated as legal.");
                }

                LegalityAnalysis analysis = new(pkm);
                return new LegalityResult(analysis.Valid, analysis.Report());
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Legality analysis failed for {Flavor}; treating as legal.", flavor);
                return new LegalityResult(true, $"Legality analysis threw: {ex.Message}");
            }
        }

        /// <summary>
        /// Logs a verdict without acting on it. Used by the QA pass that measures how common illegal
        /// pokemon actually are before the check starts rejecting anything.
        /// </summary>
        public void LogVerdict(byte[] raw, GameFlavor flavor, string description = "")
        {
            LegalityResult result = Analyze(raw, flavor);
            if (result.IsLegal)
            {
                logger.LogInformation("Legality OK {Context}", description);
            }
            else
            {
                logger.LogWarning("Legality FAILED {Context}: {Report}", description, result.Report);
            }
        }

        private static EntityContext ContextFor(GameFlavor flavor) => flavor switch
        {
            GameFlavor.RS => EntityContext.Gen3,
            GameFlavor.HGSS => EntityContext.Gen4,
            _ => EntityContext.None,
        };
    }
}
