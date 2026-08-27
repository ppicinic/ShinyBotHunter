using System.Security.Cryptography;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// Per-instance ROM copies must hash differently, or BizHawk resolves them all to the same game
    /// database entry and every emulator writes to one save file. These check the stamp does that
    /// while leaving everything the game reads untouched.
    /// </summary>
    public class RomSaveIsolationTests : IDisposable
    {
        private readonly string dir;

        public RomSaveIsolationTests()
        {
            dir = Path.Combine(Path.GetTempPath(), "sbh-stamp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
        }

        public void Dispose() => Directory.Delete(dir, recursive: true);

        /// <summary>A stand-in ROM: content, then the trailing padding a real cartridge dump has.</summary>
        private string WriteRom(string name, int contentBytes = 4096, int padding = 8192)
        {
            byte[] rom = new byte[contentBytes + padding];
            Random.Shared.NextBytes(rom.AsSpan(0, contentBytes));
            rom.AsSpan(contentBytes).Fill(0xFF);
            string path = Path.Combine(dir, name);
            File.WriteAllBytes(path, rom);
            return path;
        }

        private static string Hash(string path)
        {
            using FileStream f = File.OpenRead(path);
            return Convert.ToHexString(SHA1.HashData(f));
        }

        [Fact]
        public void StampedCopiesHashDifferentlyFromEachOtherAndTheOriginal()
        {
            string original = WriteRom("base.gba");
            string first = Path.Combine(dir, "base1.gba");
            string second = Path.Combine(dir, "base2.gba");
            File.Copy(original, first);
            File.Copy(original, second);

            RomSaveIsolation.Stamp(first, 1);
            RomSaveIsolation.Stamp(second, 2);

            Assert.NotEqual(Hash(original), Hash(first));
            Assert.NotEqual(Hash(first), Hash(second));
        }

        [Fact]
        public void EverythingBeforeThePaddingIsUntouched()
        {
            // The whole argument for stamping is that it cannot change how the game runs.
            string path = WriteRom("game.gba", contentBytes: 4096, padding: 8192);
            byte[] before = File.ReadAllBytes(path);

            RomSaveIsolation.Stamp(path, 7);
            byte[] after = File.ReadAllBytes(path);

            Assert.Equal(before.Length, after.Length);
            Assert.Equal(before.AsSpan(0, 4096).ToArray(), after.AsSpan(0, 4096).ToArray());
        }

        [Fact]
        public void TheGbaHeaderIsUntouched()
        {
            // Bytes 0xA0-0xBC are covered by the header checksum; changing any would make the dump
            // fail verification on real hardware.
            string path = WriteRom("game.gba");
            byte[] before = File.ReadAllBytes(path);

            RomSaveIsolation.Stamp(path, 3);
            byte[] after = File.ReadAllBytes(path);

            Assert.Equal(before.AsSpan(0xA0, 0x20).ToArray(), after.AsSpan(0xA0, 0x20).ToArray());
        }

        [Fact]
        public void AFileWithoutPaddingIsRefused()
        {
            // Nowhere provably unused to write, so refusing beats corrupting game data.
            byte[] rom = new byte[8192];
            Random.Shared.NextBytes(rom);
            string path = Path.Combine(dir, "nopad.gba");
            File.WriteAllBytes(path, rom);

            var ex = Assert.Throws<InvalidOperationException>(() => RomSaveIsolation.Stamp(path, 1));
            Assert.Contains("padding", ex.Message);
        }

        [Fact]
        public void StampingIsDetectable()
        {
            string path = WriteRom("game.gba");

            Assert.False(RomSaveIsolation.IsStamped(path));
            RomSaveIsolation.Stamp(path, 4);
            Assert.True(RomSaveIsolation.IsStamped(path));
        }
    }
}
