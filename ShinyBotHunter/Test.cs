using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Target;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Scripts;
using ShinyBotHunter.Service;
using System.Diagnostics;
using YamlDotNet.Core;
using YamlDotNet.Core.Tokens;

namespace ShinyBotHunter
{
    public class Test
    {
        public async Task TestBattleStateReader(IServiceProvider provider)
        {
            var reader = provider.GetRequiredService<PokemonStateReader>();
            var parser = provider.GetRequiredService<HgssPokemonParser>();
            var metadata = provider.GetRequiredService<PokemonMetadataService>();
            var dmgService = provider.GetRequiredService<SmogonDamageService>();
            var mem = new BizHawkMemoryClient(port: 50001);
            var bot = new BizHawkCommandClient(port: 50000);
            mem.Connect();
            bot.Connect();
            Console.WriteLine("Press to pick best move");
            string c = Console.ReadLine()!;
            while (c != "c")
            {
                var state = await reader.GetPokemonBattleState(mem);
                var state2 = await reader.GetPokemonState(mem);
                c = Console.ReadLine()!;
            }
        }

        public async Task TestBattleEvent(IServiceProvider provider)
        {
            var mem = new BizHawkMemoryClient(port: 50001);
            var cmd = new BizHawkCommandClient(port: 50000);
            var bot = new Bot.Bot(new EmptyTestLauncher("Pokemon Soulsilver"), cmd, mem, new HgssDataReader(mem));
            await bot.LaunchAsync();
            CancellationTokenSource cts = new CancellationTokenSource();
            var e = new OrchestratorEvent(provider, new BotEvents.Factories.EncounterParams(BotEvents.Factories.EncounterType.Run, 0, 2));
            Console.WriteLine("Press to start");
            Console.ReadKey();

            var _ = e.ExecuteWithResultAsync(new GameState() { Bot = bot, Target = new ShinyTarget(), OriginalBattleMode = BattleMode.Battle }, cts.Token);
            Console.WriteLine("Press to cancel");
            Console.ReadKey();
            await cts.CancelAsync();
            try
            {
                await _;
            }
            catch { }
        }

        public async Task TestPointerSearchingAsync(IServiceProvider provider)
        {
            var reader = provider.GetRequiredService<PokemonStateReader>();
            var parser = provider.GetRequiredService<HgssPokemonParser>();
            var metadata = provider.GetRequiredService<PokemonMetadataService>();
            var mem = new BizHawkMemoryClient(port: 50001);
            var bot = new BizHawkCommandClient(port: 50000);
            mem.Connect();
            bot.Connect();
            var startState= await reader.GetPokemonState(mem);
            for(int i = 0; i < startState.PartySize; i++)
            {
                Console.WriteLine(startState.PartyPokemon[i]!.PID);
                Console.WriteLine(startState.PartyPokemon[i]!.HP);
            }
            uint addr = 0x2AAB0 + await mem.ReadUIntAsync(0x21D4158);
            for(uint i = 0; i < 6; i++)
            {
                byte[] data1 = await mem.ReadBytesAsync(addr + (i*236), 236);
                var pk = parser.Parse(data1);
                if (pk is not null)
                {
                    Console.WriteLine($"Level: {pk.Level}, HP: {pk.HP}, Id: {pk.HP}/{pk.MaxHP}, Name: {pk.Name}");
                }
            }
            string c = "a";
            var dmgService = provider.GetRequiredService<SmogonDamageService>();
            Console.WriteLine("Press to debug damage service");
            c = Console.ReadLine()!;
            HashSet<int> values = new HashSet<int>();
            HashSet<int> ppValues = new HashSet<int>();
            bool first = true;
            while (c != "c")
            {
                try
                {
                    string[] parts = c.Split(",");
                    int searchValue = Int32.Parse(parts[0]);
                    int[] ppSearchValues = [Int32.Parse(parts[1]), Int32.Parse(parts[2]), Int32.Parse(parts[3]), Int32.Parse(parts[4])];
                    var state = await reader.GetPokemonState(mem);
                    var anchor = await mem.ReadUIntAsync(0x21D4158);
                    var foeAnchor = await mem.ReadUIntAsync(anchor + 0x6930);
                    var pokemon = parser.Parse(await mem.ReadBytesAsync(foeAnchor + 0xC18, 0xEC));
                    var data = await mem.ReadBytesAsync(0x2000000, 0x1000000);
                    HashSet<int> newValues = new HashSet<int>();
                    HashSet<int> newValues2 = new HashSet<int>();
                    for (int i = 0; i < data.Length - 4; i++)
                    {
                        if (BitConverter.ToUInt16(data, i) == searchValue)
                        {
                            if (first)
                            {
                                newValues.Add(i);
                            }
                            else
                            {
                                if (values.Contains(i))
                                {
                                    newValues.Add(i);
                                }
                            }
                        }
                        if (data[i] == ppSearchValues[0] && data[i+1] == ppSearchValues[1] && data[i+2] == ppSearchValues[2] && data[i+3] == ppSearchValues[3])
                        {
                            if (first)
                            {
                                newValues2.Add(i);
                            }
                            else
                            {
                                if (ppValues.Contains(i))
                                {
                                    newValues2.Add(i);
                                }
                            }
                        }
                    }
                    Console.WriteLine("HP Addresses");
                    if (newValues.Count() < 20)
                    {
                        Console.WriteLine(String.Join(", ", newValues.Select(x => $"{x:X6}")));
                        Console.WriteLine($"{anchor:X8}");
                    }
                    else
                    {
                        Console.WriteLine(newValues.Count());
                    }
                    Console.WriteLine("PP Addresses");
                    if (newValues2.Count() <= 20)
                    {
                        Console.WriteLine(String.Join(", ", newValues2.Select(x => $"{x:X6}")));
                        Console.WriteLine($"{anchor:X8}");
                    }
                    else
                    {
                        Console.WriteLine(newValues2.Count());
                    }
                    first = false;
                    values = newValues;
                    ppValues = newValues2;
                    //var result = dmgService.PickBestMove(state.PartyPokemon[0]!, pokemon!);
                    //Console.WriteLine(metadata.GetPokemonMoveMetadata(state.PartyPokemon[0]!.Moves[result].Id).Name);
                }
                catch
                {

                }
                Console.WriteLine("Enter new value");
                c = Console.ReadLine()!;
            }
        }
    }
}
