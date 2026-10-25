using System.IO.MemoryMappedFiles;

namespace ShinyBotHunter.Emulators.Dolphin
{
    // Reads GameCube MEM1 through the shared mapping Dolphin publishes as "dolphin-emu.<pid>"
    // (the one Dolphin Memory Engine uses): 24 MiB of big-endian RAM with 0x80000000 at offset 0.
    // Bytes come back in memory order; nothing here reads multi-byte values.
    public sealed class DolphinMemory : IDisposable
    {
        private const uint Mem1Base = 0x8000_0000;
        private const long Mem1Size = 0x0180_0000;

        private readonly MemoryMappedFile _mapping;
        private readonly MemoryMappedViewAccessor _view;

        // A mapping that is not there yet throws FileNotFoundException; the launcher waits on it.
        public DolphinMemory(int processId)
        {
            _mapping = MemoryMappedFile.OpenExisting("dolphin-emu." + processId, MemoryMappedFileRights.Read);
            _view = _mapping.CreateViewAccessor(0, Mem1Size, MemoryMappedFileAccess.Read);
        }

        public byte ReadByte(uint address) => _view.ReadByte(address - Mem1Base);

        public byte[] ReadBytes(uint address, int count)
        {
            byte[] bytes = new byte[count];
            _view.ReadArray(address - Mem1Base, bytes, 0, count);
            return bytes;
        }

        public void Dispose()
        {
            _view.Dispose();
            _mapping.Dispose();
        }
    }
}
