using Microsoft.Extensions.Logging.Abstractions;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// These pin the behaviour that matters for a hunt: the service must never turn a library problem
    /// into a rejected shiny, and must never silently report "not checked" as a pass a caller cannot
    /// distinguish. What counts as legal is PKHeX's judgement, not something worth restating here.
    /// </summary>
    public class LegalityServiceTests
    {
        private static LegalityService Build() => new(NullLogger<LegalityService>.Instance);

        [Fact]
        public void UnsupportedFlavor_IsReportedAsSuchRatherThanAsIllegal()
        {
            // "We did not check" is not evidence a pokemon is impossible, so it must not reject one.
            LegalityResult result = Build().Analyze(new byte[100], (GameFlavor)999);

            Assert.True(result.IsLegal);
            Assert.Contains("not implemented", result.Report);
        }

        [Fact]
        public void GarbageBytesDoNotThrow()
        {
            // Called on whatever a party read returned, so it has to survive anything.
            LegalityResult result = Build().Analyze(new byte[100], GameFlavor.RS);

            Assert.NotNull(result.Report);
        }

        [Fact]
        public void ShortBlockDoesNotThrow()
        {
            LegalityResult result = Build().Analyze(new byte[4], GameFlavor.RS);

            Assert.NotNull(result.Report);
        }

        [Fact]
        public void AnalysisFailureIsTreatedAsLegal()
        {
            // A library error must not cost a genuine shiny; the QA logging is what surfaces it.
            LegalityResult result = Build().Analyze(Array.Empty<byte>(), GameFlavor.RS);

            Assert.True(result.IsLegal);
        }
    }
}
