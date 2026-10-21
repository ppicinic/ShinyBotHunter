using ShinyBotHunter.Bot;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// A bot with no emulator behind it. LaunchAsync finishes when the test says so, which is what
    /// lets a test hold a launch open and see what a second caller does meanwhile.
    /// </summary>
    public class FakeBot : IBot
    {
        private readonly Task launch;

        public FakeBot(Task launch)
        {
            this.launch = launch;
        }

        public bool Disposed { get; private set; }

        public GameFlavor GameFlavor => GameFlavor.HGSS;
        public string GameFileName => "fake.nds";

        public Task LaunchAsync() => launch;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }

        public Task RepositionAsync(int x, int y) => Task.CompletedTask;
        public Task RepositionAsync(int placement) => Task.CompletedTask;

        public void Connect() { }
        public void Tap(string button) { }
        public void Hold(string button) { }
        public void HoldFrames(string button, int frames) { }
        public void Release() { }
        public void Release(string button) { }
        public void RunMacroString(string macro) { }
        public void RunRepeatMacro(string macro) { }
        public void RunMacros(params MacroStruct[] macro) { }
        public void ClearMacro() { }
        public Task WaitForMacroAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task FlushSave(CancellationToken ct = default) => Task.CompletedTask;
        public Task StartRecordingAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> StopRecordingAsync(CancellationToken ct = default) => Task.FromResult("");
        public Task<string> GetRecordingStatusAsync(CancellationToken ct = default) => Task.FromResult("");

        public Task<byte> ReadByteAsync(uint address) => Task.FromResult((byte)0);
        public Task<ushort> ReadUShortAsync(uint address) => Task.FromResult((ushort)0);
        public Task<uint> ReadUIntAsync(uint address) => Task.FromResult(0u);
        public Task<byte[]> ReadBytesAsync(uint address, int length) => Task.FromResult(new byte[length]);
        public Task WriteByteAsync(uint address, byte value) => Task.CompletedTask;
        public Task<byte[]> ReadBytesViaPointerAsync(uint anchorAddress, int length, params int[] hopOffsets) => Task.FromResult(new byte[length]);
        public Task<byte> ReadByteViaPointerAsync(uint anchorAddress, params int[] hopOffsets) => Task.FromResult((byte)0);
        public Task<ushort> ReadUShortViaPointerAsync(uint anchorAddress, params int[] hopOffsets) => Task.FromResult((ushort)0);
        public Task<uint> ReadUIntViaPointerAsync(uint anchorAddress, params int[] hopOffsets) => Task.FromResult(0u);
        public Task<byte[][]> ReadValidatedPartyAsync(uint anchorAddress, int countOffset, int slotsOffset, int slotSize, int slotStride, int maxSlots, int maxRetryFrames,
            PartyAddressing addressing = PartyAddressing.Anchor, PokemonScheme scheme = PokemonScheme.Gen4) => Task.FromResult(Array.Empty<byte[]>());

        public Task<byte> GetBattleMenuState() => Task.FromResult((byte)0);
        public Task<byte> GetBattleValue() => Task.FromResult((byte)0);
        public Task<byte> GetBattleState() => Task.FromResult((byte)0);
        public Task<ushort> GetMapId() => Task.FromResult((ushort)0);
        public Task<short> GetTrainerXPos() => Task.FromResult((short)0);
        public Task<short> GetTrainerYPos() => Task.FromResult((short)0);
        public Task<byte> GetFacing() => Task.FromResult((byte)0);
    }
}
