using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Target;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    // The RS battle event over scripted snapshots and scripted chain reads; values from docs/research/rs-wild-encounter.md.
    [Collection("macro-registry")]
    public class RsBattleEventTests
    {
        // The addresses the event reads, as literals so a wrong table member fails here.
        private const uint BattleTypeFlags = 0x020239F8;
        private const uint VBlankCounter = 0x03001790;
        private const uint EnemyParty = 0x030045C0;
        private const uint LeadHp = 0x03004360 + 0x56;
        private const uint FieldControlsLock = 0x030006A4;
        private const uint InBattle = 0x03001BAD;
        private const uint ScriptContextStatus = 0x030005B0;
        private const uint TrainerBattleOpponent = 0x0202FF5E;
        private const uint EventFlags = 0x02026954;
        private const uint PlayerLocation = 0x02025738;

        private const ushort Wild = 0x0004;

        // The trainer read's bytes, from docs/research/rs-navigation-and-training.md "A trainer's intro speech": the lock,
        // the in-battle byte, the script status, the opponent and the event flags that are set. The player is on Route 102,
        // map (0, 17).
        public sealed record TrainerBytes(byte Lock, byte InBattle, byte Status, ushort Opponent, params int[] FlagsSet);

        private static readonly TrainerBytes NoScriptRunning = new(1, 0, 2, 0);
        private const string Route102 = "A trainer battle started on map 0.17.";

        // Short for the tests that time out; Long for those whose snapshots repeat over a few polls on the way.
        private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(50);
        private static readonly TimeSpan Long = TimeSpan.FromSeconds(5);

        // Wurmple's internal species index (national 265).
        private const ushort Wurmple = 290;

        private static readonly GameSnapshot Free = new(GameScreen.FieldFree, null, null, null, null);
        private static readonly GameSnapshot Locked = new(GameScreen.FieldTask, null, null, null, null);
        private static readonly GameSnapshot Turn = new(GameScreen.BattleTurn, null, null, null, null);

        public RsBattleEventTests()
        {
            Directory.SetCurrentDirectory(RepoPaths.AppProject);
        }

        private static string Macro(string name) => MacroRegistry.Instance.GetMacro(GameFlavor.RS, name);

        private static string Confirm => Macro("BattleConfirm");

        // BattleAdvance and BattleConfirm are the same text, so a press is named by its macro and the screen it was sent at.
        private static string Name(string macro, GameSnapshot at) => (macro, at.Screen) switch
        {
            ("H.A.2", GameScreen.BattleMessage) => "Advance",
            ("H.A.2", GameScreen.BattleMenu) => "Confirm",
            ("H.Right.2;H.Down.2", GameScreen.BattleMenu) => "ToRun",
            ("H.Right.2", GameScreen.BattleMenu) => "Right",
            ("H.Down.2", GameScreen.BattleMenu) => "Down",
            _ => $"{macro} at {at}",
        };

        private static GameSnapshot Message(BattleOutcome? outcome = null) => new(GameScreen.BattleMessage, outcome, null, null, null, TextPrinter: 8);

        private static GameSnapshot Menu(int cursor) => new(GameScreen.BattleMenu, null, null, cursor, null);

        // Serves each stage's snapshots in turn, the last repeating; every press moves to the next stage, so a test does not
        // depend on how many polls a step makes. A press past the last stage throws, so a doubled press fails the test.
        private sealed class PressedReader(params GameSnapshot[][] stages) : IGameStateReader
        {
            private Queue<GameSnapshot> queue = new(stages[0]);

            public int Stage { get; private set; }

            // The snapshot last served.
            public GameSnapshot Last { get; private set; }

            public void Advance() => queue = new(stages[++Stage]);

            public Task<GameSnapshot> ReadAsync(CancellationToken ct)
            {
                Last = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
                return Task.FromResult(Last);
            }
        }

        // A party-sized Gen 3 record with only the species and level set. pid % 24 == 0 keeps the substructures in order; the
        // key pid ^ otId is XORed over the twelve words. otId == pid is shiny (key 0, shiny value 0); otId 0 is not.
        private static byte[] Encode(uint pid, uint otId, ushort species, byte level, bool corrupt = false)
        {
            byte[] block = new byte[Gen3PokemonParser.PartyBlockSize];
            BitConverter.GetBytes(pid).CopyTo(block, 0x00);
            BitConverter.GetBytes(otId).CopyTo(block, 0x04);
            BitConverter.GetBytes((ushort)(corrupt ? species + 1 : species)).CopyTo(block, 0x1C);
            uint key = pid ^ otId;
            for (int i = 0; i < 48; i += 4)
            {
                uint word = i == 0 ? species : 0u;
                BitConverter.GetBytes(word ^ key).CopyTo(block, 0x20 + i);
            }
            block[0x54] = level;
            return block;
        }

        private static readonly byte[] Plain = Encode(0x30, 0, Wurmple, 3);
        private static readonly byte[] Shiny = Encode(0x30, 0x30, Wurmple, 2);

        // One battle: the bot, its state at BattleStart, and what it pressed, logged, alerted and read.
        private sealed class Battle
        {
            public required FakeBot Bot { get; init; }
            public required GameState State { get; init; }
            public required PressedReader Reader { get; init; }
            public List<string> Presses { get; } = [];
            public List<PersistPokemonData> Logged { get; } = [];
            public List<PersistPokemonData> Alerted { get; } = [];
            // The snapshot last served at each read of the wild Pokémon's record.
            public List<GameSnapshot> EnemyReads { get; } = [];
            // PKHeX's verdict for every record analysed, and the records it was asked about.
            public LegalityResult Verdict { get; set; } = new(true, "legal");
            public List<byte[]> Analysed { get; } = [];
            // The trainer messages sent, and every chain request made, in order.
            public List<string> TrainerMessages { get; } = [];
            public List<IReadOnlyList<ChainBlockRequest>> Requests { get; } = [];

            // afterHandOff defaults to battleStart.
            public Task<string?> RunAsync(TimeSpan? battleStart = null, TimeSpan? stale = null, TimeSpan? checksum = null, TimeSpan? cursorWait = null,
                bool trainerNotified = false)
                => RsBattleEvent.RunAsync(State, new RsPointerService(),
                    p => { Logged.Add(p); return Task.CompletedTask; },
                    p => { Alerted.Add(p); return Task.CompletedTask; },
                    s => { TrainerMessages.Add(s); return Task.CompletedTask; }, trainerNotified,
                    raw => { Analysed.Add(raw); return Verdict; },
                    battleStart ?? Long, battleStart ?? Long, stale ?? Long, checksum ?? Long, cursorWait ?? Long, CancellationToken.None);
        }

        // The records come from the queue, the last repeating; the lead's HP by the reader's stage; the counter from its queue;
        // the trainer read from trainer, by default a locked field with no script.
        private static Battle Start(PressedReader reader, ITarget? target = null, ushort flags = Wild, byte[][]? enemies = null,
            Func<int, ushort>? leadHp = null, uint[]? counter = null, TrainerBytes? trainer = null)
        {
            Queue<byte[]> records = new(enemies ?? [Plain]);
            Queue<uint> frames = new(counter ?? [1000]);
            TrainerBytes read = trainer ?? NoScriptRunning;
            Battle? battle = null;
            ChainBlockResult Answer(ChainBlockRequest block)
            {
                byte[] bytes;
                switch (block.Anchor)
                {
                    case FieldControlsLock:
                        bytes = [read.Lock];
                        break;
                    case InBattle:
                        bytes = [read.InBattle];
                        break;
                    case ScriptContextStatus:
                        bytes = [read.Status];
                        break;
                    case TrainerBattleOpponent:
                        bytes = BitConverter.GetBytes(read.Opponent);
                        break;
                    case EventFlags:
                        bytes = new byte[block.Length];
                        foreach (int flag in read.FlagsSet)
                        {
                            bytes[flag >> 3] |= (byte)(1 << (flag & 7));
                        }
                        break;
                    case PlayerLocation:
                        bytes = [0, 17];
                        break;
                    case BattleTypeFlags:
                        bytes = BitConverter.GetBytes(flags);
                        break;
                    case VBlankCounter:
                        bytes = BitConverter.GetBytes(frames.Count > 1 ? frames.Dequeue() : frames.Peek());
                        break;
                    case EnemyParty:
                        battle!.EnemyReads.Add(reader.Last);
                        bytes = records.Count > 1 ? records.Dequeue() : records.Peek();
                        break;
                    case LeadHp:
                        bytes = BitConverter.GetBytes((leadHp ?? (_ => 20))(reader.Stage));
                        break;
                    default:
                        throw new InvalidOperationException($"unexpected read at 0x{block.Anchor:X8}");
                }
                return new ChainBlockResult(ChainBlockStatus.Ok, 0, bytes);
            }
            FakeBot bot = new(Task.CompletedTask, RsProfile.Create(new PokemonMetadataService(null!)))
            {
                ChainBlocks = blocks =>
                {
                    battle!.Requests.Add(blocks);
                    return blocks.Select(Answer).ToArray();
                },
                Pressed = macro =>
                {
                    // A at the action menu only ever with the cursor at 3, in every test.
                    if (macro == Confirm && reader.Last.Screen == GameScreen.BattleMenu)
                    {
                        Assert.Equal(3, reader.Last.Prompt);
                    }
                    battle!.Presses.Add(Name(macro, reader.Last));
                    reader.Advance();
                },
                State = reader,
            };
            GameState state = new() { Bot = bot, Profile = bot.Profile, Target = target ?? new NoTarget(), Milestone = "Route 101" };
            state.State.Push(EventState.BattleStart);
            battle = new Battle { Bot = bot, State = state, Reader = reader };
            return battle;
        }

        // Every stage after a press opens with the snapshot the press was sent at, so only that press's confirm wait gets the
        // event past it: a missing confirm shows as a doubled press in the exact press list.

        // The spike's cycle: "Wild ... appeared!", the menu at FIGHT, the cursor on RUN, "Got away safely!", the free field.
        private static PressedReader Fled() => new(
            [Locked, Turn, Message()],
            [Message(), Turn, Menu(0)],
            [Menu(0), Menu(3)],
            [Menu(3), Turn, Message(BattleOutcome.Fled)],
            [Message(BattleOutcome.Fled), Turn, Locked, Free]);

        // One "Can't escape!" before getting away; the menu comes back with the cursor still on RUN.
        private static PressedReader FailedEscape() => new(
            [Locked, Turn, Message()],
            [Message(), Turn, Menu(0)],
            [Menu(0), Menu(3)],
            [Menu(3), Turn, Message()],
            [Message(), Turn, Menu(3)],
            [Menu(3), Turn, Message(BattleOutcome.Fled)],
            [Message(BattleOutcome.Fled), Turn, Locked, Free]);

        [Fact]
        public void MacrosAreTheResearchedHolds()
        {
            Assert.Equal("H.A.2", Macro("BattleAdvance"));
            Assert.Equal("H.Right.2;H.Down.2", Macro("BattleCursorToRun"));
            Assert.Equal("H.A.2", Macro("BattleConfirm"));
            // FR-16: no one-frame tap.
            Assert.DoesNotContain("T.", Macro("BattleAdvance") + Macro("BattleCursorToRun") + Macro("BattleConfirm"));
        }

        [Fact]
        public void MissingPressesMakeTheCursorMacro()
        {
            Assert.Equal($"{RsBattleEvent.Right};{RsBattleEvent.Down}", Macro("BattleCursorToRun"));
        }

        [Fact]
        public async Task FledBattle_FivePressesEachOnceEachConfirmed()
        {
            Battle battle = Start(Fled());

            string? stop = await battle.RunAsync();

            Assert.Null(stop);
            // Five presses: BattleCursorToRun is Right and Down.
            Assert.Equal(["Advance", "ToRun", "Confirm", "Advance"], battle.Presses);
            Assert.Equal([EventState.BattleComplete], battle.State.State);
            Assert.Equal(0x30u, Assert.Single(battle.Logged).PokemonData.PID);
            Assert.Empty(battle.Alerted);
        }

        [Fact]
        public async Task ConfirmIsOnlyPressedWithTheCursorAtThree()
        {
            // Start's press callback asserts the cursor at every A at the menu; these are the scripts that press it.
            PressedReader repair = new([Locked, Turn, Message()], [Message(), Turn, Menu(0)], [Menu(0), Menu(1)], [Menu(1), Menu(3)],
                [Menu(3), Turn, Message(BattleOutcome.Fled)], [Message(BattleOutcome.Fled), Turn, Free]);
            foreach (PressedReader reader in new[] { Fled(), FailedEscape(), repair })
            {
                Battle battle = Start(reader);

                await battle.RunAsync(cursorWait: Short);

                Assert.Contains("Confirm", battle.Presses);
                Assert.Equal([EventState.BattleComplete], battle.State.State);
            }
        }

        [Theory]
        [InlineData(1, "Down")]
        [InlineData(2, "Right")]
        [InlineData(0, "ToRun")]
        public async Task CursorShortOfRunGetsTheMissingPressAlone(int cursor, string missing)
        {
            Battle battle = Start(new PressedReader(
                [Locked, Turn, Message()],
                [Message(), Turn, Menu(0)],
                [Menu(0), Menu(cursor)],
                [Menu(cursor), Menu(3)],
                [Menu(3), Turn, Message(BattleOutcome.Fled)],
                [Message(BattleOutcome.Fled), Turn, Free]));

            string? stop = await battle.RunAsync(cursorWait: Short);

            Assert.Null(stop);
            Assert.Equal(["Advance", "ToRun", missing, "Confirm", "Advance"], battle.Presses);
        }

        [Fact]
        public async Task CursorReadAFrameEarly_GetsNoRepairPress()
        {
            // The cursor reads 1 on the first poll after the macro and 3 on the next.
            Battle battle = Start(new PressedReader(
                [Locked, Turn, Message()],
                [Message(), Turn, Menu(0)],
                [Menu(0), Menu(1), Menu(3)],
                [Menu(3), Turn, Message(BattleOutcome.Fled)],
                [Message(BattleOutcome.Fled), Turn, Free]));

            string? stop = await battle.RunAsync(cursorWait: TimeSpan.FromSeconds(1));

            Assert.Null(stop);
            Assert.Equal(["Advance", "ToRun", "Confirm", "Advance"], battle.Presses);
        }

        [Fact]
        public async Task FailedEscape_ConfirmsAgainOnRun()
        {
            Battle battle = Start(FailedEscape());

            string? stop = await battle.RunAsync();

            Assert.Null(stop);
            Assert.Equal(["Advance", "ToRun", "Confirm", "Advance", "Confirm", "Advance"], battle.Presses);
            Assert.Equal([EventState.BattleComplete], battle.State.State);
        }

        [Fact]
        public async Task LeadFaintedAtTheMenu_Stops()
        {
            // Stage 4 is the menu after "Can't escape!": the wild Pokémon's move took the lead's last HP.
            Battle battle = Start(FailedEscape(), leadHp: stage => stage >= 4 ? (ushort)0 : (ushort)20);

            string? stop = await battle.RunAsync();

            Assert.Equal(RsBattleEvent.Fainted, stop);
            Assert.Equal([EventState.Waiting], battle.State.State);
            Assert.Equal(["Advance", "ToRun", "Confirm", "Advance"], battle.Presses);
        }

        [Fact]
        public async Task OutcomeLost_Stops()
        {
            Battle battle = Start(new PressedReader(
                [Locked, Turn, Message()],
                [Message(), Turn, Menu(0)],
                [Menu(0), Menu(3)],
                [Menu(3), Turn, Message(BattleOutcome.Lost)]));

            string? stop = await battle.RunAsync();

            Assert.Equal(RsBattleEvent.Fainted, stop);
            Assert.Equal([EventState.Waiting], battle.State.State);
            // No A at the lost battle's message.
            Assert.Equal(["Advance", "ToRun", "Confirm"], battle.Presses);
        }

        [Fact]
        public async Task PreviousBattlesOutcomeBeforeTheMenu_IsIgnored()
        {
            GameSnapshot lostTurn = Turn with { Outcome = BattleOutcome.Lost };
            Battle battle = Start(new PressedReader(
                [Locked, lostTurn, Message(BattleOutcome.Lost)],
                [Message(BattleOutcome.Lost), lostTurn, Menu(0)],
                [Menu(0), Menu(3)],
                [Menu(3), Turn, Message(BattleOutcome.Fled)],
                [Message(BattleOutcome.Fled), Turn, Free]));

            string? stop = await battle.RunAsync();

            Assert.Null(stop);
            Assert.Equal(["Advance", "ToRun", "Confirm", "Advance"], battle.Presses);
        }

        [Fact]
        public async Task TwelveTries_Stops()
        {
            List<GameSnapshot[]> stages = [[Locked, Turn, Message()], [Message(), Turn, Menu(0)], [Menu(0), Menu(3)]];
            for (int i = 0; i < RsBattleEvent.MaxRunPresses; i++)
            {
                stages.Add([Menu(3), Turn, Message()]);
                stages.Add([Message(), Turn, Menu(3)]);
            }
            Battle battle = Start(new PressedReader([.. stages]));

            string? stop = await battle.RunAsync();

            Assert.Equal(RsBattleEvent.Trapped, stop);
            Assert.Equal([EventState.Waiting], battle.State.State);
            // Twelve A on RUN, each after "Can't escape!" was answered; then the thirteenth menu stops.
            List<string> expected = ["Advance", "ToRun", "Confirm"];
            for (int i = 1; i < RsBattleEvent.MaxRunPresses; i++)
            {
                expected.AddRange(["Advance", "Confirm"]);
            }
            expected.Add("Advance");
            Assert.Equal(expected, battle.Presses);
        }

        // Safari (0x0084) and a battle with no wild bit keep today's stop.
        [Theory]
        [InlineData((ushort)(0x0080 | 0x0004))]
        [InlineData((ushort)0x0000)]
        public async Task NotAWildBattle_Stops(ushort flags)
        {
            Battle battle = Start(new PressedReader([Locked, Turn]), flags: flags);

            string? stop = await battle.RunAsync();

            Assert.Equal(RsBattleEvent.NotWild, stop);
            Assert.Equal([EventState.Waiting], battle.State.State);
            Assert.Empty(battle.Presses);
            Assert.Empty(battle.EnemyReads);
            Assert.Empty(battle.TrainerMessages);
        }

        [Fact]
        public async Task LockWithNoBattle_Stops()
        {
            Battle battle = Start(new PressedReader([Locked]));

            string? stop = await battle.RunAsync(battleStart: Short);

            Assert.Equal(RsBattleEvent.NotWild, stop);
            Assert.Equal([EventState.Waiting], battle.State.State);
            Assert.Empty(battle.Presses);
            Assert.Empty(battle.TrainerMessages);
        }

        // The seek handed over a lock: Calvin (318) with his flag 1598 clear, at the walk-up (status 1) and while the
        // speech prints (status 0); and Rick (615) talked to, with Calvin beaten and Rick's flag 1895 clear.
        [Theory]
        [InlineData((byte)0, (ushort)318, new int[0])]
        [InlineData((byte)1, (ushort)318, new int[0])]
        [InlineData((byte)0, (ushort)615, new[] { 1598 })]
        public async Task TrainerChallenge_TheHandedOverLock_StopsWithTheSentenceOneMessageAndNoPress(byte status, ushort opponent, int[] flagsSet)
        {
            Battle battle = Start(new PressedReader([Locked]), trainer: new TrainerBytes(1, 0, status, opponent, flagsSet));

            string? stop = await battle.RunAsync();

            Assert.Equal(Route102, stop);
            Assert.Equal([EventState.Waiting], battle.State.State);
            Assert.Empty(battle.Presses);
            Assert.Equal([Route102], battle.TrainerMessages);
            Assert.Empty(battle.Alerted);
            Assert.Empty(battle.EnemyReads);
            // One read on one frame: the one request with the script status holds every block the rule needs.
            IReadOnlyList<ChainBlockRequest> read = Assert.Single(battle.Requests, r => r.Any(b => b.Anchor == ScriptContextStatus));
            Assert.Equal([FieldControlsLock, InBattle, ScriptContextStatus, TrainerBattleOpponent, EventFlags, PlayerLocation], read.Select(b => b.Anchor));
            Assert.Equal(0x120, read.Single(b => b.Anchor == EventFlags).Length);
            Assert.Single(battle.Requests);
        }

        // The research table's rows where the rule is false.
        public static TheoryData<TrainerBytes> NotAChallenge => new()
        {
            new TrainerBytes(1, 0, 0, 0),             // a sign read before any trainer: no id
            new TrainerBytes(1, 0, 0, 318, 1598),     // the beaten Calvin talked to, or a sign after winning: his flag set
            new TrainerBytes(1, 0, 1, 318, 1598),     // the return from the won battle
            new TrainerBytes(1, 0, 2, 318),           // a wild battle starting with an unbeaten stale id: no script
            new TrainerBytes(1, 0x02, 1, 318),        // in the battle
        };

        [Theory]
        [MemberData(nameof(NotAChallenge))]
        public async Task TrainerChallenge_StaleOrNoScript_IsNotATrainer(TrainerBytes read)
        {
            Battle battle = Start(new PressedReader([Locked]), trainer: read);

            string? stop = await battle.RunAsync(battleStart: Short);

            Assert.Equal(RsBattleEvent.NotWild, stop);
            Assert.Equal([EventState.Waiting], battle.State.State);
            Assert.Empty(battle.TrainerMessages);
            Assert.Empty(battle.Presses);
        }

        [Fact]
        public async Task TrainerMiss_ThenTheBattleBit_FleesAsBefore()
        {
            Battle battle = Start(Fled(), trainer: new TrainerBytes(1, 0, 2, 318));

            string? stop = await battle.RunAsync();

            Assert.Null(stop);
            Assert.Equal(["Advance", "ToRun", "Confirm", "Advance"], battle.Presses);
            Assert.Equal([EventState.BattleComplete], battle.State.State);
            Assert.Empty(battle.TrainerMessages);
        }

        // The battle type is the backstop: an operator's press at the speech before the hand-off, or a Resume while the
        // battle is on. After a trainer stop's message it sends none.
        [Theory]
        [InlineData(false, 1)]
        [InlineData(true, 0)]
        public async Task TrainerBattleType_Backstop_SameSentence(bool trainerNotified, int messages)
        {
            Battle battle = Start(new PressedReader([Turn]), flags: 0x000C);

            string? stop = await battle.RunAsync(trainerNotified: trainerNotified);

            Assert.Equal(Route102, stop);
            Assert.Equal([EventState.Waiting], battle.State.State);
            Assert.Equal(messages, battle.TrainerMessages.Count);
            Assert.Empty(battle.Presses);
            Assert.Empty(battle.EnemyReads);
        }

        // A wild battle that reaches the seek's battle screen makes no trainer read: the entry request is today's two blocks.
        [Fact]
        public async Task NormalWildBattle_MakesNoTrainerRead()
        {
            Battle battle = Start(new PressedReader(
                [Turn, Message()],
                [Message(), Turn, Menu(0)],
                [Menu(0), Menu(3)],
                [Menu(3), Turn, Message(BattleOutcome.Fled)],
                [Message(BattleOutcome.Fled), Turn, Locked, Free]));

            string? stop = await battle.RunAsync();

            Assert.Null(stop);
            Assert.Equal([EventState.BattleComplete], battle.State.State);
            Assert.Equal([BattleTypeFlags, VBlankCounter], battle.Requests[0].Select(b => b.Anchor));
            Assert.DoesNotContain(battle.Requests, r => r.Any(b => b.Anchor is ScriptContextStatus or TrainerBattleOpponent or EventFlags));
        }

        [Fact]
        public async Task ChecksumRetry_ReadsAgainUntilItPasses()
        {
            byte[] good = Encode(0x48, 0, Wurmple, 2);
            Battle battle = Start(Fled(), enemies: [Encode(0x48, 0, Wurmple, 2, corrupt: true), Encode(0x48, 0, Wurmple, 2, corrupt: true), good]);

            string? stop = await battle.RunAsync();

            Assert.Null(stop);
            Assert.Equal(0x48u, Assert.Single(battle.Logged).PokemonData.PID);
            Assert.Equal(3, battle.EnemyReads.Count(s => s == Menu(0)));
        }

        [Fact]
        public async Task ChecksumNeverPasses_Stops()
        {
            Battle battle = Start(Fled(), enemies: [Encode(0x48, 0, Wurmple, 2, corrupt: true)]);

            string? stop = await battle.RunAsync(checksum: Short);

            Assert.Equal(RsBattleEvent.Corrupt, stop);
            Assert.Equal([EventState.Waiting], battle.State.State);
            Assert.Empty(battle.Logged);
            Assert.Equal(["Advance"], battle.Presses);
        }

        [Fact]
        public async Task StaleTimeout_NamesTheSnapshot()
        {
            Battle battle = Start(new PressedReader([Locked, Turn, Message()], [Message(), Turn]));

            string? stop = await battle.RunAsync(stale: Short);

            Assert.Contains(Turn.ToString(), stop);
            Assert.Equal([EventState.Waiting], battle.State.State);
            Assert.Equal(["Advance"], battle.Presses);
        }

        [Fact]
        public async Task Target_LogsAlertsOnceWaitsAndPressesNothingAfterTheRead()
        {
            Battle battle = Start(Fled(), target: new ShinyTarget(), enemies: [Shiny]);

            string? stop = await battle.RunAsync();

            Assert.Null(stop);
            PersistPokemonData logged = Assert.Single(battle.Logged);
            Assert.Same(logged, Assert.Single(battle.Alerted));
            Assert.Same(logged, battle.State.TargetPokemon);
            Assert.True(logged.PokemonData.IsShiny);
            Assert.False(logged.LegalityFailed);
            Assert.Equal([EventState.Waiting], battle.State.State);
            Assert.Equal(["Advance"], battle.Presses);
            Assert.Equal(Shiny, Assert.Single(battle.Analysed));
        }

        [Fact]
        public async Task IllegalShiny_IsLoggedOnceMarkedAndFledUnalerted()
        {
            Battle battle = Start(Fled(), target: new ShinyTarget(), enemies: [Shiny]);
            battle.Verdict = new(false, "PID-IV mismatch");
            TextWriter console = Console.Out;
            StringWriter log = new();
            Console.SetOut(log);
            string? stop;
            try
            {
                stop = await battle.RunAsync();
            }
            finally
            {
                Console.SetOut(console);
            }

            Assert.Null(stop);
            Assert.Equal(Shiny, Assert.Single(battle.Analysed));
            PersistPokemonData logged = Assert.Single(battle.Logged);
            Assert.True(logged.LegalityFailed);
            Assert.Equal(0x30u, logged.PokemonData.PID);
            Assert.Empty(battle.Alerted);
            Assert.Null(battle.State.TargetPokemon);
            Assert.Equal([EventState.BattleComplete], battle.State.State);
            // Five presses: BattleCursorToRun is Right and Down.
            Assert.Equal(["Advance", "ToRun", "Confirm", "Advance"], battle.Presses);
            Assert.Contains("[RsBattle] legality FAIL", log.ToString());
            Assert.Contains("logged as not shiny", log.ToString());
            Assert.Contains("PID-IV mismatch", log.ToString());
        }

        [Fact]
        public async Task NotShiny_IsNeverAnalysed()
        {
            // In the failed escape the menu comes twice.
            foreach (PressedReader reader in new[] { Fled(), FailedEscape() })
            {
                Battle battle = Start(reader, enemies: [Plain]);

                await battle.RunAsync();

                Assert.Empty(battle.Analysed);
                Assert.Single(battle.Logged);
            }
        }

        [Fact]
        public async Task TheReadHappensAtTheMenuNeverBefore()
        {
            foreach (PressedReader reader in new[] { Fled(), FailedEscape() })
            {
                Battle battle = Start(reader);

                await battle.RunAsync();

                Assert.NotEmpty(battle.EnemyReads);
                Assert.All(battle.EnemyReads, s => Assert.Equal(GameScreen.BattleMenu, s.Screen));
            }
        }

        [Fact]
        public async Task BattleFramesAreTheCounterDifference()
        {
            Battle battle = Start(Fled(), counter: [1000, 1692]);
            TextWriter console = Console.Out;
            StringWriter log = new();
            Console.SetOut(log);
            try
            {
                await battle.RunAsync();
            }
            finally
            {
                Console.SetOut(console);
            }

            Assert.Contains("[RsBattle] battle over: 692 frames from the battle bit", log.ToString());
            // A, Right, Down, A on RUN, A: BattleCursorToRun is two presses.
            Assert.Contains("5 presses, 1 on RUN", log.ToString());
        }

        [Fact]
        public void RsProfileBattleEventIsRsBattleEvent()
        {
            // The event's constructor only stores what it is given, so the services it never reaches here are null.
            var provider = new ServiceCollection()
                .AddSingleton(new PokemonPersistenceService(null!, null!, null!))
                .AddSingleton(new ShinyAlertService(null!, Options.Create(new ApplicationSettings())))
                .AddSingleton(new LegalityService(NullLogger<LegalityService>.Instance))
                .BuildServiceProvider();

            Assert.IsType<RsBattleEvent>(RsProfile.Create(new PokemonMetadataService(null!)).CreateBattleEvent!(provider));
        }
    }
}
