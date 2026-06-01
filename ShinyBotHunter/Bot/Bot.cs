using ShinyBotHunter.Emulators;
using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Bot
{
    public class Bot : IBot

    {
        private ILauncher launcher;
        private ICommandClient commandClient;
        private IMemoryClient memoryClient;
        private IDataReader dataReader;
        public Bot(ILauncher launcher, ICommandClient commandClient, IMemoryClient memoryClient, IDataReader reader)
        {
            this.launcher = launcher;
            this.commandClient = commandClient;
            this.memoryClient = memoryClient;
            this.dataReader = reader;
        }

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

        public IDataReader Reader => throw new NotImplementedException();

        public void HoldFrames(string button, int frames)
        {
            this.commandClient.HoldFrames(button, frames);
        }

        public async Task LaunchAsync()
        {
            await this.launcher.LaunchAsync();
            await Task.Delay(5000);
            Connect();
        }

        public async Task<byte> ReadByteAsync(uint address)
        {
            return await this.memoryClient.ReadByteAsync(address);
        }

        public async Task<byte[]> ReadBytesAsync(uint address, int length)
        {
            return await this.memoryClient.ReadBytesAsync(address, length);
        }

        public async Task<uint> ReadUIntAsync(uint address)
        {
            return await this.memoryClient.ReadUIntAsync(address);
        }

        public async Task<ushort> ReadUShortAsync(uint address)
        {
            return await this.memoryClient.ReadUShortAsync(address);
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

        //public async Task<IBot> Run()
        //{
        //    if (!File.Exists($"C:\\ROMs\\{this.game}{this.gameId}.nds"))
        //    {
        //        File.Copy($"C:\\ROMs\\{this.game}.nds", $"C:\\ROMs\\{this.game}{this.gameId}.nds");
        //    }
        //    await launcher.Launch();
        //    Console.WriteLine($"Waiting on emu: {this.gameId}");
        //    await Task.Delay(16000);
        //    Console.WriteLine($"Connecting to emu on: {this.gameId} via port: {this.commandPort}");
        //    commandClient.Connect();
        //    memoryClient.Connect();
        //    Console.WriteLine($"Executing Create Save Macro on emu: {this.gameId}");
        //    commandClient.RunMacroString(MacroRegistry.Instance.GetMacro(MacroFlavor.HGSS, "CreateNewSave"));
        //    await commandClient.WaitForMacroAsync();
        //    return this;
        //}

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
            await this.commandClient.FlushSave();
        }

        public async Task WaitForMacroAsync(CancellationToken ct = default)
        {
            await this.commandClient.WaitForMacroAsync(ct);
        }

        public async Task WriteByteAsync(uint address, byte value)
        {
            await this.memoryClient.WriteByteAsync(address, value);
        }

        public void RunMacros(params MacroStruct[] macro)
        {
            this.commandClient.RunMacros(macro);
        }

        public async Task<byte> GetBattleMenuState()
        {
            return await dataReader.GetBattleMenuState();
        }

        public async Task<byte> GetBattleValue()
        {
            return await dataReader.GetBattleValue();
        }
        public async Task<byte> GetBattleState()
        {
            return await dataReader.GetBattleState();
        }

        public async Task<ushort> GetMapId()
        {
            return await dataReader.GetMapId();
        }

        public async Task<short> GetTrainerXPos()
        {
            return await dataReader.GetTrainerXPos();
        }

        public async Task<short> GetTrainerYPos()
        {
            return await dataReader.GetTrainerYPos();
        }

        public async Task<byte> GetFacing()
        {
            return await dataReader.GetFacing();
        }
    }
}
