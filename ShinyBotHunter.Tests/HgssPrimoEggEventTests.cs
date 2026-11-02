using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Context;
using ShinyBotHunter.Controllers;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    // The Primo egg hunt's steps over a fake memory and scripted snapshots; values from docs/research/primo-slugma-egg.md
    // (trainer ID 60647, password 1085 1337 / 981 1062, Primo's tile (12, 12) on map 158).
    [Collection("macro-registry")]
    public class HgssPrimoEggEventTests
    {
        private static readonly HgssPointerService Table = new();

        // Where the anchor, saveData, the task manager and the script environment point; any RAM values work.
        private const uint FieldSystem = 0x02300000;
        private const uint SaveData = 0x0227C254;
        private const uint TaskManager = 0x02310000;
        private const uint ScriptEnvironment = 0x02320000;

        private const uint RunScripts = 0x0203FF45;
        private const uint Printing = 0x021EF349;
        private const uint MenuChoice = 0x020477C1;
        private const uint Fade = 0x02041CA9;

        public HgssPrimoEggEventTests()
        {
            Directory.SetCurrentDirectory(RepoPaths.AppProject);
        }

        // Each read takes the next snapshot; the last one repeats.
        private sealed class ScriptedReader(params GameSnapshot[] snapshots) : IGameStateReader
        {
            private readonly Queue<GameSnapshot> queue = new(snapshots);

            public Task<GameSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(queue.Count > 1 ? queue.Dequeue() : queue.Peek());
        }

        private static GameSnapshot Primo(uint? native, int? textPrinter = null)
            => new(GameScreen.FieldTask, null, Table.PrimoScript, null, RunScripts, native, TextPrinter: textPrinter);

        private static void Stand(FakeMemoryClient memory, int map, int x, int y, Direction facing)
        {
            memory.Place(Table.Anchor, BitConverter.GetBytes(FieldSystem));
            memory.Place((uint)(FieldSystem + Table.MapIdOffset), BitConverter.GetBytes((ushort)map));
            memory.Place((uint)(FieldSystem + Table.FacingOffset), [(byte)facing]);
            memory.Place(Table.TrainerX, BitConverter.GetBytes((uint)x << 16));
            memory.Place(Table.TrainerY, BitConverter.GetBytes((uint)y << 16));
        }

        [Fact]
        public async Task TrainerIdIsReadThroughTheSaveData()
        {
            FakeMemoryClient memory = new();
            memory.Place(Table.Anchor, BitConverter.GetBytes(FieldSystem));
            memory.Place(FieldSystem + 0x0C, BitConverter.GetBytes(SaveData));
            memory.Place(SaveData + 0x84, BitConverter.GetBytes((ushort)60647));

            ushort id = await HgssPrimoEggEvent.ReadTrainerIdAsync(memory, Table, CancellationToken.None);

            Assert.Equal(60647, id);
            var (blocks, _) = Assert.Single(memory.ChainReads);
            ChainBlockRequest block = Assert.Single(blocks);
            Assert.Equal(Table.Anchor, block.Anchor);
            Assert.Equal([0x0C, 0x84], block.Hops);
        }

        [Fact]
        public async Task TrainerIdReadWithANullSaveDataNamesTheHop()
        {
            FakeMemoryClient memory = new();
            memory.Place(Table.Anchor, BitConverter.GetBytes(FieldSystem));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => HgssPrimoEggEvent.ReadTrainerIdAsync(memory, Table, CancellationToken.None));

            Assert.Contains("NullPointer at hop 1", ex.Message);
        }

        [Fact]
        public async Task SpecialVariablesAreReadFromTheScriptEnvironment()
        {
            FakeMemoryClient memory = new();
            memory.Place(Table.Anchor, BitConverter.GetBytes(FieldSystem));
            memory.Place(FieldSystem + 0x10, BitConverter.GetBytes(TaskManager));
            memory.Place(TaskManager + 0x0C, BitConverter.GetBytes(ScriptEnvironment));
            memory.Place(ScriptEnvironment + 0x8C, [.. BitConverter.GetBytes((ushort)1085), .. BitConverter.GetBytes((ushort)1337),
                .. BitConverter.GetBytes((ushort)981), .. BitConverter.GetBytes((ushort)1062)]);
            memory.Place(ScriptEnvironment + 0xA4, BitConverter.GetBytes((ushort)5));

            ushort[] vars = await HgssPrimoEggEvent.ReadSpecialVarsAsync(memory, Table, CancellationToken.None);

            Assert.Equal(new ushort[] { 1085, 1337, 981, 1062, 0, 0, 0, 0, 0, 0, 0, 0, 5, 0 }, vars);
            Assert.Equal(5, vars[12]);
        }

        [Fact]
        public void WordsThatDoNotMatchStopNamingBoth()
        {
            ushort[] password = [1085, 1337, 981, 1062];
            ushort[] vars = [1085, 1337, 981, 1063, 0, 0, 0, 0, 0, 0, 0, 0, 5, 0];

            HgssPrimoEggEvent.CheckWords(vars, 1, password);
            var ex = Assert.Throws<InvalidOperationException>(() => HgssPrimoEggEvent.CheckWords(vars, 2, password));

            Assert.Equal("after prompt 2, 0x8002 and 0x8003 read 981, 1063; entered 981, 1062", ex.Message);
        }

        [Theory]
        [InlineData(1, null)]
        [InlineData(3, null)]
        [InlineData(5, null)]
        [InlineData(255, "refused")]
        [InlineData(0, "refused")]
        [InlineData(10, "already received")]
        [InlineData(6, "party is full")]
        [InlineData(7, "result 7")]
        public void OutcomeClassification(int result, string? reason)
        {
            string? refusal = HgssPrimoEggEvent.Refusal((ushort)result, 10);

            if (reason is null) Assert.Null(refusal);
            else Assert.Contains(reason, refusal);
        }

        [Fact]
        public async Task PrimosTextIsPressedThroughUntilTheEasyChatAppOpens()
        {
            ScriptedReader game = new(
                Primo(Printing, 0), Primo(Printing, 2), Primo(Printing), Primo(MenuChoice), Primo(Fade),
                new GameSnapshot(GameScreen.Transitioning, null, null, null, null),
                new GameSnapshot(GameScreen.OtherApp, null, null, null, null));
            List<string> presses = [];

            GameSnapshot end = await NavigatorEvent.PressThroughScriptAsync(game, Table, m => { presses.Add(m); return Task.CompletedTask; },
                "Primo's script", TimeSpan.FromSeconds(5), CancellationToken.None, HgssPrimoEggEvent.EasyChatOpened);

            Assert.Equal(["T.A;W.20", "W.5;T.A;W.20"], presses);
            Assert.Equal(GameScreen.OtherApp, end.Screen);
        }

        [Fact]
        public async Task AfterAPromptTheLoopStopsAtTheNextMessageWithoutAPress()
        {
            ScriptedReader game = new(
                new GameSnapshot(GameScreen.OtherApp, null, null, null, null),
                new GameSnapshot(GameScreen.Transitioning, null, null, null, null),
                Primo(Fade), Primo(Printing, 0));
            List<string> presses = [];

            GameSnapshot end = await NavigatorEvent.PressThroughScriptAsync(game, Table, m => { presses.Add(m); return Task.CompletedTask; },
                "Primo's script after prompt 1", TimeSpan.FromSeconds(5), CancellationToken.None, HgssPrimoEggEvent.AtMessage(Table));

            Assert.Empty(presses);
            Assert.Equal(Primo(Printing, 0), end);
        }

        [Fact]
        public async Task OnPrimosTileFacingUpIsTheStart()
        {
            FakeMemoryClient memory = new();
            Stand(memory, 158, 12, 12, Direction.Up);

            string? where = await HgssPrimoEggEvent.WrongPlaceAsync(new HgssOverworldReader(memory, Table), PrimoEggConfig.Load(PrimoEggConfig.DefaultPath), CancellationToken.None);

            Assert.Null(where);
        }

        [Theory]
        [InlineData(157, 12, 12, Direction.Up, "map 157, tile (12, 12), facing up")]
        [InlineData(158, 12, 13, Direction.Up, "map 158, tile (12, 13), facing up")]
        [InlineData(158, 12, 12, Direction.Left, "map 158, tile (12, 12), facing left")]
        public async Task WrongPlaceNamesWhereThePlayerIs(int map, int x, int y, Direction facing, string expected)
        {
            FakeMemoryClient memory = new();
            Stand(memory, map, x, y, facing);

            string? where = await HgssPrimoEggEvent.WrongPlaceAsync(new HgssOverworldReader(memory, Table), PrimoEggConfig.Load(PrimoEggConfig.DefaultPath), CancellationToken.None);

            Assert.Equal(expected, where);
        }

        [Fact]
        public void TheSentenceIsTheSpecs()
        {
            Assert.Equal("Stand facing Primo in the Violet City Pokémon Center: map 158, tile (12, 12), facing up.",
                HgssPrimoEggEvent.StandFacingPrimo(PrimoEggConfig.Load(PrimoEggConfig.DefaultPath)));
        }

        [Fact]
        public void RouteLegMacrosFromPrimosTile()
        {
            // Each leg from where the one before ended; the door leg ends outside, at (497, 272) in Violet City.
            (short X, short Y)[] from = [(12, 12), (12, 19), (8, 19), (497, 272), (488, 272), (488, 264), (469, 264)];
            var legs = PrimoEggConfig.Load(PrimoEggConfig.DefaultPath).Legs;

            string[] macros = legs.Select((leg, i) => HgssPrimoEggEvent.LegMacro(leg, from[i].X, from[i].Y, Table)).ToArray();

            Assert.Equal(
            [
                $"M.Down.19.{Table.TrainerY}", $"M.Left.8.{Table.TrainerX}", $"M.Down.272.{Table.TrainerY}", $"M.Left.488.{Table.TrainerX}",
                $"M.Up.264.{Table.TrainerY}", $"M.Left.469.{Table.TrainerX}", $"M.Up.258.{Table.TrainerY}",
            ], macros);
        }

        [Fact]
        public async Task RouteLegEndsWhenTheCoordinateIsReached()
        {
            FakeMemoryClient memory = new();
            Stand(memory, 158, 12, 19, Direction.Down);
            FakeBot commands = new(Task.CompletedTask);

            await HgssPrimoEggEvent.WalkLegAsync(new HgssOverworldReader(memory, Table), new ScriptedReader(FieldFree), commands, Table, ('y', 19),
                TimeSpan.FromMilliseconds(100), CancellationToken.None);

            Assert.Empty(commands.Macros);
        }

        private static readonly GameSnapshot FieldFree = new(GameScreen.FieldFree, null, null, null, null);

        // Violet City's sign at (488, 263) as observed: script 9, the sign's two natives, no text printer.
        private static GameSnapshot Sign(uint? native) => new(GameScreen.FieldTask, null, 9, null, RunScripts, native);

        [Fact]
        public async Task RouteLegThatStopsMovingWithTheFieldFreeNamesTheTile()
        {
            FakeMemoryClient memory = new();
            Stand(memory, 158, 8, 19, Direction.Down);
            FakeBot commands = null!;
            commands = new(Task.CompletedTask) { Pressed = m => commands.Macros.Add(m) };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => HgssPrimoEggEvent.WalkLegAsync(
                new HgssOverworldReader(memory, Table), new ScriptedReader(FieldFree), commands, Table, ('y', 272), TimeSpan.FromMilliseconds(100),
                CancellationToken.None));

            Assert.Equal("Route leg y:272 stopped at map 158, tile (8, 19)", ex.Message);
            Assert.Equal([$"M.Down.272.{Table.TrainerY}", "clear"], commands.Macros);
        }

        [Fact]
        public async Task RouteLegInterruptedByTheSignIsPressedThroughAndResumed()
        {
            FakeMemoryClient memory = new();
            Stand(memory, 73, 488, 264, Direction.Up);
            int legs = 0;
            FakeBot commands = null!;
            commands = new(Task.CompletedTask)
            {
                Pressed = m =>
                {
                    commands.Macros.Add(m);
                    // The re-issued leg walks; the first one is stuck under the sign.
                    if (m.StartsWith("M.") && ++legs == 2) memory.Place(Table.TrainerX, BitConverter.GetBytes((uint)469 << 16));
                }
            };
            ScriptedReader game = new(Sign(0x02041455), Sign(0x020415E1), Sign(0x020415E1), Sign(0x02040ECD), FieldFree);

            await HgssPrimoEggEvent.WalkLegAsync(new HgssOverworldReader(memory, Table), game, commands, Table, ('x', 469),
                TimeSpan.FromSeconds(5), CancellationToken.None);

            string leg = $"M.Left.469.{Table.TrainerX}";
            Assert.Equal([leg, "clear", "H.A.4;W.20", "H.A.4;W.20", leg], commands.Macros);
        }

        // Every party block parses, so the party check counts the blocks the bot returns.
        private sealed class StubParser : IPokemonParser
        {
            public PokemonData? Parse(byte[] raw) => new PokemonData();
        }

        private static readonly GameProfile EggProfile = new(
            GameFlavor.HGSS, new GameCapabilities(false, false, true, false, false, false, false), [0], _ => Table, new StubParser(), battleParser: null);

        // A launched bot at position 0 with the given party, and the service whose Run checks see it.
        private static async Task<(BotService Service, FakeBot Bot)> LaunchWithParty(int partySize)
        {
            var settings = Options.Create(new ApplicationSettings { MaxEmulators = new() { ["DS"] = 1 } });
            var factory = new FakeBotFactory(settings, Task.CompletedTask, EggProfile);
            var provider = new ServiceCollection().AddSingleton(new PokemonStateReader()).BuildServiceProvider();
            var service = new BotService(settings, factory, provider, null!, null!, null!);
            await service.ExecuteNewBotOrSpecialEvent(new NewEventArguments { EventName = "Launch Bot", GameName = "fake.nds" });
            FakeBot bot = factory.Bots[0];
            bot.PartyBlocks = Enumerable.Range(0, partySize).Select(_ => new byte[0xEC]).ToArray();
            return (service, bot);
        }

        private static readonly NewEventArguments RunPrimo = new() { EventName = "HGSS Primo Egg", PhaseName = "Slugma" };

        [Fact]
        public async Task RunOffPrimosTileIs400WithTheSentence()
        {
            var (service, bot) = await LaunchWithParty(5);
            FakeMemoryClient memory = new();
            Stand(memory, 158, 12, 13, Direction.Up);
            bot.Overworld = new HgssOverworldReader(memory, Table);

            var result = await new BotController(service).StartAction(0, RunPrimo);

            var content = Assert.IsType<ContentResult>(result.Result);
            Assert.Equal(StatusCodes.Status400BadRequest, content.StatusCode);
            Assert.Equal("Stand facing Primo in the Violet City Pokémon Center: map 158, tile (12, 12), facing up.", content.Content);
            Assert.Equal(EventState.None, service.GetBotDashboardState()[0].EventState);
        }

        [Fact]
        public async Task RunWithAFullPartyIs400()
        {
            var (service, _) = await LaunchWithParty(6);

            var result = await new BotController(service).StartAction(0, RunPrimo);

            var content = Assert.IsType<ContentResult>(result.Result);
            Assert.Equal(StatusCodes.Status400BadRequest, content.StatusCode);
            Assert.Contains("Party is full (6/6)", content.Content);
            Assert.Equal(EventState.None, service.GetBotDashboardState()[0].EventState);
        }
    }
}
