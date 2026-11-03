using ShinyBotHunter.Bot;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// A bot with no emulator behind it. LaunchAsync finishes when the test says so, which is what
    /// lets a test hold a launch open and see what a second caller does meanwhile.
    /// </summary>
    public class FakeBot : IBot
    {
        private readonly Task launch;
        private readonly IGameStateReader? state;
        // Set by a test before the bot is used; written and read by the test's one async flow.
        private IOverworldReader? overworld;

        public FakeBot(Task launch, GameProfile? profile = null)
        {
            this.launch = launch;
            Profile = profile ?? HgssProfile.Create(new PokemonMetadataService(null!), new MapService(new Dictionary<int, Map>()), new GraphService());
        }

        public bool Disposed { get; private set; }

        public GameFlavor GameFlavor => Profile.Flavor;
        public GameProfile Profile { get; }
        public IPointerService Addresses => Profile.Addresses(0);
        public string GameFileName => "fake.nds";

        // What a test scripts: the chain reads, the party blocks, and a callback that sees each RunMacroString.
        public Func<IReadOnlyList<ChainBlockRequest>, ChainBlockResult[]> ChainBlocks { get; init; } = _ => Array.Empty<ChainBlockResult>();
        public byte[][] PartyBlocks { get; set; } = Array.Empty<byte[]>();
        public Action<string> Pressed { get; init; } = _ => { };

        // RunRepeatMacro as "RP:" + macro, ClearMacro as "clear". Written and read by the test's one async flow; no lock.
        public List<string> Macros { get; } = new();

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
        public void RunMacroString(string macro) => Pressed(macro);
        public void RunRepeatMacro(string macro) => Macros.Add("RP:" + macro);
        public void RunMacros(params MacroStruct[] macro) { }
        public void ClearMacro() => Macros.Add("clear");
        public Task WaitForMacroAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task FlushSave(CancellationToken ct = default) => Task.CompletedTask;
        public Task StartRecordingAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> StopRecordingAsync(CancellationToken ct = default) => Task.FromResult("");
        public Task<string> GetRecordingStatusAsync(CancellationToken ct = default) => Task.FromResult("");

        public Task<byte> ReadByteAsync(uint address) => Task.FromResult((byte)0);
        public Task<ushort> ReadUShortAsync(uint address) => Task.FromResult((ushort)0);
        public Task<uint> ReadUIntAsync(uint address) => Task.FromResult(0u);
        public Task<byte[]> ReadBytesAsync(uint address, int length, CancellationToken ct = default) => Task.FromResult(new byte[length]);
        public Task WriteByteAsync(uint address, byte value) => Task.CompletedTask;
        public Task<byte[]> ReadBytesViaPointerAsync(uint anchorAddress, int length, CancellationToken ct, params int[] hopOffsets) => Task.FromResult(new byte[length]);
        public Task<byte> ReadByteViaPointerAsync(uint anchorAddress, params int[] hopOffsets) => Task.FromResult((byte)0);
        public Task<ushort> ReadUShortViaPointerAsync(uint anchorAddress, params int[] hopOffsets) => Task.FromResult((ushort)0);
        public Task<uint> ReadUIntViaPointerAsync(uint anchorAddress, params int[] hopOffsets) => Task.FromResult(0u);
        public Task<byte[][]> ReadValidatedPartyAsync(uint anchorAddress, int countOffset, int slotsOffset, int slotSize, int slotStride, int maxSlots, int maxRetryFrames,
            PartyAddressing addressing = PartyAddressing.Anchor, PokemonScheme scheme = PokemonScheme.Gen4, CancellationToken ct = default) => Task.FromResult(PartyBlocks);
        public Task<ChainBlockResult[]> ReadChainBlocksAsync(IReadOnlyList<ChainBlockRequest> blocks, int retryFrames = 0, CancellationToken ct = default) => Task.FromResult(ChainBlocks(blocks));

        // Settable after construction because FakeBotFactory builds the bot; throws when no test placed one.
        public IOverworldReader Overworld { get => overworld ?? throw new NotImplementedException(); set => overworld = value; }
        public IGameStateReader State { get => state ?? throw new NotImplementedException(); init => state = value; }
    }
}
