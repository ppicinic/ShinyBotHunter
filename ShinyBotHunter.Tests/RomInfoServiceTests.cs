using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShinyBotHunter.Context;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// Identity is read from ROM headers rather than filenames, so these build headers by hand and
    /// give the files deliberately misleading names -- the point is that the name is not consulted.
    /// </summary>
    public class RomInfoServiceTests : IDisposable
    {
        private readonly string dir;

        public RomInfoServiceTests()
        {
            dir = Path.Combine(Path.GetTempPath(), "sbh-rom-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
        }

        public void Dispose() => Directory.Delete(dir, recursive: true);

        private void WriteGba(string fileName, string gameCode, byte revision)
        {
            byte[] rom = new byte[0x200];
            "POKEMON TEST"u8.ToArray().CopyTo(rom, 0xA0);
            System.Text.Encoding.ASCII.GetBytes(gameCode).CopyTo(rom, 0xAC);
            "01"u8.ToArray().CopyTo(rom, 0xB0);
            rom[0xB2] = 0x96;   // the fixed byte identifying a real cartridge header
            rom[0xBC] = revision;
            File.WriteAllBytes(Path.Combine(dir, fileName), rom);
        }

        private void WriteNds(string fileName, string gameCode, byte revision)
        {
            byte[] rom = new byte[0x200];
            "POKEMON HG\0\0"u8.ToArray().CopyTo(rom, 0x00);
            System.Text.Encoding.ASCII.GetBytes(gameCode).CopyTo(rom, 0x0C);
            "01"u8.ToArray().CopyTo(rom, 0x10);
            rom[0x1E] = revision;
            File.WriteAllBytes(Path.Combine(dir, fileName), rom);
        }

        private RomInfoService Build()
        {
            var settings = Options.Create(new ApplicationSettings { ROMBaseDirectory = dir + Path.DirectorySeparatorChar });
            var service = new RomInfoService(settings, NullLogger<RomInfoService>.Instance);
            service.InitAsync().GetAwaiter().GetResult();
            return service;
        }

        [Theory]
        [InlineData("AXVE", "Pokemon Ruby")]
        [InlineData("AXPE", "Pokemon Sapphire")]
        public void GbaHeader_IdentifiesTheGameRegardlessOfFilename(string code, string expected)
        {
            WriteGba("totally-wrong-name.gba", code, revision: 2);

            RomInfo rom = Assert.Single(Build().GetAll());

            Assert.Equal(expected, rom.GameName);
            Assert.Equal(GameFlavor.RS, rom.Flavor);
            Assert.Equal(ConsoleType.GBA, rom.Console);
            Assert.Equal(2, rom.Revision);
            Assert.Equal("USA/English", rom.Language);
            // The handle is the filename and carries no meaning beyond which file this is.
            Assert.Equal("totally-wrong-name", rom.Handle);
        }

        [Fact]
        public void NdsHeader_IdentifiesHeartGold()
        {
            WriteNds("Pokemon Sapphire.nds", "IPKE", revision: 0);

            RomInfo rom = Assert.Single(Build().GetAll());

            Assert.Equal("Pokemon HeartGold", rom.GameName);
            Assert.Equal(GameFlavor.HGSS, rom.Flavor);
            Assert.Equal(ConsoleType.DS, rom.Console);
        }

        [Fact]
        public void TestedRevision_IsDistinguishedFromMerelySupported()
        {
            WriteGba("rev2.gba", "AXVE", revision: 2);
            WriteGba("rev0.gba", "AXVE", revision: 0);

            var roms = Build().GetAll().ToDictionary(r => r.Handle);

            Assert.Equal(RevisionSupport.Tested, roms["rev2"].RevisionSupport);
            Assert.Equal(RevisionSupport.SupportedUntested, roms["rev0"].RevisionSupport);
        }

        [Fact]
        public void FindByGame_IsHowCallersLocateARomInsteadOfMatchingNames()
        {
            WriteNds("anything.nds", "IPGE", revision: 0);

            Assert.Single(Build().FindByGame("Pokemon SoulSilver"));
        }

        [Fact]
        public void FileWithoutAValidGbaHeader_IsSkippedRatherThanMisidentified()
        {
            File.WriteAllBytes(Path.Combine(dir, "not-a-rom.gba"), new byte[0x200]);

            Assert.Empty(Build().GetAll());
        }

        [Fact]
        public void NonRomFilesAreIgnored()
        {
            WriteGba("game.gba", "AXVE", revision: 2);
            File.WriteAllText(Path.Combine(dir, "game.sav"), "save data");

            // The old filename-based listing emitted a duplicate entry for each .sav beside a ROM.
            Assert.Single(Build().GetAll());
        }
    }
}
