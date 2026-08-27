using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// Compiles every macro on disk.
    ///
    /// Macros are recorded by hand and edited as text, so a typo, a duplicate name or a reference to
    /// a macro that no longer exists is easy to introduce and invisible until a hunt runs the broken
    /// one -- by which point the emulator is mashing buttons against the wrong screen. Loading the
    /// registry is enough to catch all three.
    /// </summary>
    [Collection("macro-registry")]
    public class MacroTests
    {
        public MacroTests()
        {
            // MacroRegistry resolves its config path against the working directory and caches after
            // first use, so this has to happen before anything touches Instance.
            Directory.SetCurrentDirectory(RepoPaths.AppProject);
        }

        [Theory]
        [InlineData(GameFlavor.HGSS, "ContinueGame")]
        [InlineData(GameFlavor.HGSS, "SoftReset")]
        [InlineData(GameFlavor.HGSS, "CreateNewSave")]
        [InlineData(GameFlavor.RS, "ContinueGame")]
        [InlineData(GameFlavor.RS, "ContinueGameWithRandom")]
        [InlineData(GameFlavor.RS, "SoftReset")]
        [InlineData(GameFlavor.RS, "CreateNewSave")]
        [InlineData(GameFlavor.RS, "CreateNewSavePart1")]
        [InlineData(GameFlavor.RS, "CreateNewSavePart2")]
        [InlineData(GameFlavor.RS, "SetOTName")]
        [InlineData(GameFlavor.RS, "SelectStarterTreecko")]
        [InlineData(GameFlavor.RS, "SelectStarterTorchic")]
        [InlineData(GameFlavor.RS, "SelectStarterMudkip")]
        [InlineData(GameFlavor.RS, "MashB")]
        public void EveryExpectedMacroCompiles(GameFlavor flavor, string name)
        {
            string macro = MacroRegistry.Instance.GetMacro(flavor, name);

            Assert.False(string.IsNullOrWhiteSpace(macro), $"{flavor}/{name} compiled to nothing.");
        }

        [Fact]
        public void CompositeMacros_InlineTheirParts()
        {
            string composite = MacroRegistry.Instance.GetMacro(GameFlavor.RS, "CreateNewSave");
            string part1 = MacroRegistry.Instance.GetMacro(GameFlavor.RS, "CreateNewSavePart1");
            string name = MacroRegistry.Instance.GetMacro(GameFlavor.RS, "SetOTName");

            // A composite is its parts spliced together, so it must be longer than any one of them
            // and must actually contain them -- a silently empty splice would still "compile".
            Assert.Contains(part1, composite);
            Assert.Contains(name, composite);
        }

        [Fact]
        public void RsSoftReset_UsesTheGbaComboNotTheDsOne()
        {
            string macro = MacroRegistry.Instance.GetMacro(GameFlavor.RS, "SoftReset");

            Assert.Contains("A", macro);
            Assert.Contains("Select", macro);
            // L and R are the DS combo; pressing them on GBA would not reset anything.
            Assert.DoesNotContain(".L.", macro);
            Assert.DoesNotContain(".R.", macro);
        }
    }
}
