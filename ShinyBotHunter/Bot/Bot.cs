using System.Text;
using ShinyBotHunter.Context;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Bot
{
    public class Bot : IBot

    {
        private ILauncher launcher;
        private ICommandClient commandClient;
        private IMemoryClient memoryClient;
        private IDataReader? dataReader;
        private readonly IOverworldReader? overworld;
        private GameContext? context;
        public Bot(ILauncher launcher, ICommandClient commandClient, IMemoryClient memoryClient, IDataReader? reader, GameProfile profile, IPointerService addresses, GameContext? context = null)
        {
            this.launcher = launcher;
            this.commandClient = commandClient;
            this.memoryClient = memoryClient;
            this.dataReader = reader;
            this.Profile = profile;
            this.Addresses = addresses;
            this.context = context;
            this.overworld = profile.CreateOverworldReader?.Invoke(memoryClient, addresses);
        }

        public GameProfile Profile { get; }

        public IPointerService Addresses { get; }

        public IOverworldReader Overworld => overworld ?? throw new InvalidOperationException($"{Profile.Flavor} has no overworld reader.");

        private IDataReader DataReader => dataReader ?? throw new InvalidOperationException($"{Profile.Flavor} has no battle reads.");

        public void ClearMacro()
        {
            this.commandClient.ClearMacro();
        }

        public void Connect()
        {
            this.commandClient.Connect();
            this.memoryClient.Connect();
        }

        public async ValueTask DisposeAsync()
        {
            await FlushSave(); // we force save cause we can't trust Window Server to close properly.
            try
            {
                await memoryClient.DisposeAsync();
            }
            catch { }
            try
            {
                await commandClient.DisposeAsync();
            }
            catch { }
            try
            {
                await launcher.DisposeAsync();
            }
            catch { }
        }

        public void Hold(string button)
        {
            this.commandClient.Hold(button);
        }

        public string GameFileName => this.launcher.GameFileName;

        public GameFlavor GameFlavor => Profile.Flavor;

        public void HoldFrames(string button, int frames)
        {
            this.commandClient.HoldFrames(button, frames);
        }

        public async Task LaunchAsync()
        {
            await this.launcher.LaunchAsync();
            await Task.Delay(5000);
            Connect();
            await VerifyLoadedGameAsync();
        }

        /// <summary>
        /// Confirms the core is running the ROM we launched, by reading the game code back out of
        /// emulator memory. The file header says what is on disk; only this says what actually loaded.
        ///
        /// A mismatch throws, because the alternative is a bot reading a wrong game's memory map and
        /// persisting believable garbage. A read that comes back as non-ASCII is treated as "this
        /// console's runtime header is not reachable through the default memory domain" and only
        /// warns — that is a capability gap, not evidence of a wrong game.
        /// </summary>
        private async Task VerifyLoadedGameAsync()
        {
            if (context?.Rom is null) return;

            string expected = context.Rom.GameCode;
            string actual;
            try
            {
                uint address = RomInfoService.RuntimeGameCodeAddress(context.ConsoleType);
                byte[] raw = await this.memoryClient.ReadBytesAsync(address, 4);
                if (raw.Length != 4 || !raw.All(b => b >= 0x20 && b < 0x7F))
                {
                    Console.WriteLine(
                        $"[{context.SourceGameName}] Runtime game-code check skipped: read at 0x{address:X8} " +
                        $"returned non-ASCII bytes, so this core's header is not reachable via the default memory domain.");
                    return;
                }
                actual = Encoding.ASCII.GetString(raw);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{context.SourceGameName}] Runtime game-code check skipped: {ex.Message}");
                return;
            }

            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Emulator at position {context.DisplayPosition} loaded game code '{actual}', but ROM " +
                    $"'{context.SourceGameName}' has header game code '{expected}' ({context.Rom.DisplayName}). " +
                    "Refusing to run against an unexpected game.");
            }
        }

        public async Task<byte> ReadByteAsync(uint address)
        {
            return await this.memoryClient.ReadByteAsync(address);
        }

        public async Task<byte[]> ReadBytesAsync(uint address, int length, CancellationToken ct = default)
        {
            return await this.memoryClient.ReadBytesAsync(address, length, ct);
        }

        public async Task<uint> ReadUIntAsync(uint address)
        {
            return await this.memoryClient.ReadUIntAsync(address);
        }

        public async Task<ushort> ReadUShortAsync(uint address)
        {
            return await this.memoryClient.ReadUShortAsync(address);
        }

        public async Task<byte[]> ReadBytesViaPointerAsync(uint anchorAddress, int length, CancellationToken ct, params int[] hopOffsets)
        {
            return await this.memoryClient.ReadBytesViaPointerAsync(anchorAddress, length, ct, hopOffsets);
        }

        public async Task<byte> ReadByteViaPointerAsync(uint anchorAddress, params int[] hopOffsets)
        {
            return await this.memoryClient.ReadByteViaPointerAsync(anchorAddress, hopOffsets);
        }

        public async Task<ushort> ReadUShortViaPointerAsync(uint anchorAddress, params int[] hopOffsets)
        {
            return await this.memoryClient.ReadUShortViaPointerAsync(anchorAddress, hopOffsets);
        }

        public async Task<uint> ReadUIntViaPointerAsync(uint anchorAddress, params int[] hopOffsets)
        {
            return await this.memoryClient.ReadUIntViaPointerAsync(anchorAddress, hopOffsets);
        }

        public async Task<byte[][]> ReadValidatedPartyAsync(uint anchorAddress, int countOffset, int slotsOffset, int slotSize, int slotStride, int maxSlots, int maxRetryFrames,
            PartyAddressing addressing = PartyAddressing.Anchor, PokemonScheme scheme = PokemonScheme.Gen4, CancellationToken ct = default)
        {
            return await this.memoryClient.ReadValidatedPartyAsync(anchorAddress, countOffset, slotsOffset, slotSize, slotStride, maxSlots, maxRetryFrames, addressing, scheme, ct);
        }

        public void Release()
        {
            this.commandClient.Release();
        }

        public void Release(string button)
        {
            this.commandClient.Release(button);
        }

        public async Task RepositionAsync(int x, int y)
        {
            await this.launcher.RepositionAsync(x, y);
        }

        public async Task RepositionAsync(int placement)
        {
            await this.launcher.RepositionAsync(placement);
        }

        public void RunMacroString(string macro)
        {
            this.commandClient.RunMacroString(macro);
        }

        public void RunRepeatMacro(string macro)
        {
            this.commandClient.RunRepeatMacro(macro);
        }

        public void Tap(string button)
        {
            this.commandClient.Tap(button);
        }

        public async Task FlushSave(CancellationToken ct = default)
        {
            await this.commandClient.FlushSave(ct);
        }

        public async Task WaitForMacroAsync(CancellationToken ct = default)
        {
            await this.commandClient.WaitForMacroAsync(ct);
        }

        public async Task StartRecordingAsync(CancellationToken ct = default)
        {
            await this.commandClient.StartRecordingAsync(ct);
        }

        public async Task<string> StopRecordingAsync(CancellationToken ct = default)
        {
            return await this.commandClient.StopRecordingAsync(ct);
        }

        public async Task<string> GetRecordingStatusAsync(CancellationToken ct = default)
        {
            return await this.commandClient.GetRecordingStatusAsync(ct);
        }

        public async Task WriteByteAsync(uint address, byte value)
        {
            await this.memoryClient.WriteByteAsync(address, value);
        }

        public void RunMacros(params MacroStruct[] macro)
        {
            this.commandClient.RunMacros(macro);
        }

        public async Task<byte> GetBattleMenuState(CancellationToken ct = default)
        {
            return await DataReader.GetBattleMenuState(ct);
        }

        public async Task<byte> GetBattleValue(CancellationToken ct = default)
        {
            return await DataReader.GetBattleValue(ct);
        }
        public async Task<byte> GetBattleState()
        {
            return await DataReader.GetBattleState();
        }
    }
}
