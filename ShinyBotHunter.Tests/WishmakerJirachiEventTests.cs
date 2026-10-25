using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// The pure parts of the Wishmaker hunt: where its files are, the FR-5 refusals at Start, and the
    /// FR-9 save-again rule. The cycle itself needs BizHawk and Dolphin and is QA (rs-jirachi-soak).
    /// </summary>
    public class WishmakerJirachiEventTests : IDisposable
    {
        private const int FooterOffset = 0xFF4;
        private const int LiveBlock = 0xE000; // the live block of rs-rev0-base

        private readonly string folder = Path.Combine(Path.GetTempPath(), $"wishmaker-{Guid.NewGuid()}");
        private readonly string huntSave;
        private readonly string dolphinExe;
        private readonly string bios;
        private readonly string iso;

        public WishmakerJirachiEventTests()
        {
            Directory.CreateDirectory(folder);
            huntSave = Path.Combine(folder, "Sapphire.SaveRAM");
            dolphinExe = Path.Combine(folder, "Dolphin.exe");
            bios = Path.Combine(folder, "gba_bios.bin");
            iso = Path.Combine(folder, "bonus.iso");
            foreach (string file in new[] { dolphinExe, bios, iso })
            {
                File.WriteAllBytes(file, [0]);
            }
        }

        public void Dispose()
        {
            Directory.Delete(folder, recursive: true);
        }

        private static byte[] Base() =>
            File.ReadAllBytes(Path.Combine(RepoPaths.Root, "ShinyBotHunter.Tests", "Saves", "rs-rev0-base.SaveRAM"));

        /// <summary>Writes the computed checksum of a live-block section into its footer, after an edit.</summary>
        private static void Resign(byte[] save, int id)
        {
            var section = Gen3SaveFile.ReadSections(save, LiveBlock).Single(s => s.Id == id);
            BitConverter.GetBytes(section.ComputedChecksum).CopyTo(save, section.Offset + FooterOffset + 2);
        }

        private string Refusal(byte[]? save)
        {
            if (save is not null)
            {
                File.WriteAllBytes(huntSave, save);
            }
            return Assert.Throws<ArgumentException>(() => WishmakerJirachiEvent.CheckSetup(huntSave, dolphinExe, bios, iso)).Message;
        }

        [Fact]
        public void PathsAreTheHuntSaveAndTheSuffixedPair()
        {
            var paths = WishmakerJirachiEvent.PathsFor(@"X:\ROMs\", @"X:\BizHawk", "Sapphire", ".gba");

            Assert.Equal(@"X:\BizHawk\GBA\SaveRAM\Sapphire.SaveRAM", paths.HuntSave);
            Assert.Equal(@"X:\BizHawk\GBA\SaveRAM\Sapphire-WISHMKR.SaveRAM", paths.ResultSave);
            Assert.Equal(@"X:\ROMs\Sapphire.gba", paths.HuntRom);
            Assert.Equal(@"X:\ROMs\Sapphire-WISHMKR.gba", paths.ResultRom);
        }

        [Fact]
        public void GoodSetupPasses()
        {
            File.WriteAllBytes(huntSave, Base());

            WishmakerJirachiEvent.CheckSetup(huntSave, dolphinExe, bios, iso);
        }

        [Fact]
        public void MissingSaveIsRefused()
        {
            Assert.Contains("No save", Refusal(null));
        }

        [Fact]
        public void ShortSaveIsRefused()
        {
            Assert.Contains("shorter than", Refusal(Base()[..131071]));
        }

        [Fact]
        public void IncompleteLiveBlockIsRefused()
        {
            byte[] save = Base();
            save[Gen3SaveFile.ReadSections(save, LiveBlock)[5].Offset] ^= 0xFF;

            Assert.Contains("no complete save block", Refusal(save));
        }

        [Fact]
        public void NoPokedexIsRefused()
        {
            byte[] save = Base();
            int section2 = Gen3SaveFile.ReadSections(save, LiveBlock).Single(s => s.Id == 2).Offset;
            save[section2 + 0x3A0] &= unchecked((byte)~0x02);
            Resign(save, 2);

            Assert.Contains("no Pokédex", Refusal(save));
        }

        [Fact]
        public void FullPartyIsRefused()
        {
            byte[] save = Base();
            int section1 = Gen3SaveFile.ReadSections(save, LiveBlock).Single(s => s.Id == 1).Offset;
            save[section1 + 0x234] = 6;
            Resign(save, 1);

            Assert.Contains("full party", Refusal(save));
        }

        [Fact]
        public void MissingDolphinIsRefused()
        {
            File.Delete(dolphinExe);

            Assert.Contains("DolphinBaseDirectory", Refusal(Base()));
        }

        [Fact]
        public void MissingBiosIsRefused()
        {
            File.Delete(bios);

            Assert.Contains("GbaBiosPath", Refusal(Base()));
        }

        [Fact]
        public void MissingIsoIsRefused()
        {
            File.Delete(iso);

            Assert.Contains("ColosseumBonusDiscPath", Refusal(Base()));
        }

        [Fact]
        public void SaveAgainWhenTheFirstBlockDidNotAdvance()
        {
            byte[] before = Base();
            Assert.False(WishmakerJirachiEvent.FirstBlockAdvanced(before, Base()));

            byte[] after = Base();
            foreach (var section in Gen3SaveFile.ReadSections(after, 0x0000))
            {
                BitConverter.GetBytes(section.Counter + 2).CopyTo(after, section.Offset + FooterOffset + 8);
            }
            Assert.True(WishmakerJirachiEvent.FirstBlockAdvanced(before, after));
        }
    }
}
