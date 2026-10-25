using System.IO.MemoryMappedFiles;
using System.Text;
using ShinyBotHunter.Emulators.Dolphin;

namespace ShinyBotHunter.Tests
{
    public class DolphinMemoryTests
    {
        [Fact]
        public void ReadsMem1AtGameCubeAddresses()
        {
            // Named after the test process, so no real Dolphin can own the mapping.
            using MemoryMappedFile mapping = MemoryMappedFile.CreateNew("dolphin-emu." + Environment.ProcessId, 0x0180_0000);
            using (MemoryMappedViewAccessor writer = mapping.CreateViewAccessor())
            {
                writer.WriteArray(0, Encoding.ASCII.GetBytes("PC6E01"), 0, 6);
                writer.Write(0xD0_ED38, (byte)2);
            }

            using DolphinMemory memory = new(Environment.ProcessId);

            Assert.Equal("PC6E01", Encoding.ASCII.GetString(memory.ReadBytes(0x8000_0000, 6)));
            Assert.Equal(2, memory.ReadByte(0x80D0_ED38));
        }
    }
}
