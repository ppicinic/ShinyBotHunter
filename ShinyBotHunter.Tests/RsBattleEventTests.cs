using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Tests
{
    // The RS battle event over scripted snapshots and scripted chain reads; values from docs/research/rs-wild-encounter.md.
    [Collection("macro-registry")]
    public class RsBattleEventTests
    {
        public RsBattleEventTests()
        {
            Directory.SetCurrentDirectory(RepoPaths.AppProject);
        }

        private static string Macro(string name) => MacroRegistry.Instance.GetMacro(GameFlavor.RS, name);

        [Fact]
        public void MacrosAreTheResearchedHolds()
        {
            Assert.Equal("H.A.2", Macro("BattleAdvance"));
            Assert.Equal("H.Right.2;H.Down.2", Macro("BattleCursorToRun"));
            Assert.Equal("H.A.2", Macro("BattleConfirm"));
            // FR-16: no one-frame tap.
            Assert.DoesNotContain("T.", Macro("BattleAdvance") + Macro("BattleCursorToRun") + Macro("BattleConfirm"));
        }
    }
}
