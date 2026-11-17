using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Target;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Persistence.MongoDb;
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
        private const uint PartyCount = 0x03004350;
        private const uint PlayerParty = 0x03004360;
        private const uint LeadHp = PlayerParty + 0x56;
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
        // A on FIGHT is "Fight", A on RUN "Confirm", A on the move list "Move" and the slot.
        private static string Name(string macro, GameSnapshot at) => (macro, at.Screen) switch
        {
            ("H.A.2", GameScreen.BattleMessage) => "Advance",
            ("H.A.2", GameScreen.BattleMenu) => at.Prompt switch { 0 => "Fight", 2 => "ToPokemon", _ => "Confirm" },
            ("H.Right.2;H.Down.2", GameScreen.BattleMenu) => "ToRun",
            ("H.Right.2", GameScreen.BattleMenu) => "Right",
            ("H.Down.2", GameScreen.BattleMenu) => "Down",
            ("H.Left.2", GameScreen.BattleMenu) => "Left",
            // On the battle party list and its popup only with the fade off (the list taking keys).
            ("H.Down.2", GameScreen.OtherApp) when at.TaskFunction == PartyListTask && at.AppState == 0 => "PartyDown",
            ("H.A.2", GameScreen.OtherApp) when at.TaskFunction == PartyListTask && at.AppState == 0 => $"Slot{at.PartyMonIndex}",
            ("H.A.2", GameScreen.OtherApp) when at.TaskFunction == PartyPopupTask && at.AppState == 0 => "Shift",
            ("H.A.2", GameScreen.BattleMoveList) => $"Move{at.Prompt}",
            ("H.Left.2", GameScreen.BattleMoveList) => "Left",
            ("H.Up.2", GameScreen.BattleMoveList) => "Up",
            ("H.Right.2", GameScreen.BattleMoveList) => "Right",
            ("H.Down.2", GameScreen.BattleMoveList) => "Down",
            ("H.A.2", GameScreen.BattleExpPrompt) => $"Box{at.Prompt}",
            ("H.A.2", GameScreen.BattleMoveLearnPrompt) when at.Question == 5 => "Yes",
            ("H.B.2", GameScreen.BattleMoveLearnPrompt) when at.Question == 5 => "No",
            ("H.A.2", GameScreen.BattleMoveLearnPrompt) when at.Question == 10 => "StopYes",
            // On the forget-a-move screen only with its input task running and the fade off.
            ("H.Down.2", GameScreen.OtherApp) when at.TaskFunction == InputTask && at.AppState == 0 => "ForgetDown",
            ("H.A.2", GameScreen.OtherApp) when at.TaskFunction == InputTask && at.AppState == 0 => $"Forget{at.Prompt}",
            // In the evolution scene: A at a text wait (named by data[0]), and the scene's own yes/no. B anywhere else in
            // the scene falls through to the default name, which no expected press list holds.
            ("H.A.2", GameScreen.Evolution) when at.Question is null && at.TextPrinter == 8 => $"SceneText{at.Prompt}",
            ("H.A.2", GameScreen.Evolution) when at.Question == 5 => "SceneYes",
            ("H.B.2", GameScreen.Evolution) when at.Question == 5 => "SceneNo",
            ("H.A.2", GameScreen.Evolution) when at.Question == 10 => "SceneStopYes",
            _ => $"{macro} at {at}",
        };

        // The summary screen's input task and the task from the frame its A is taken.
        private const uint InputTask = 0x0809E281;
        private const uint DestroyTask = 0x0809E15D;
        private const uint MoveToLearn = 0x02024E82;

        // The battle party list's task function and its popup's (SHIFT / SUMMARY / CANCEL).
        private const uint PartyListTask = 0x08095139;
        private const uint PartyPopupTask = 0x08095429;

        // The party list as the reader gives it: under CB2_InitPartyMenu the task alone (switch research point 1); under
        // CB2_PartyMenuMain the cursor and the fade. The popup the same with its function.
        private static GameSnapshot PartyInit() => new(GameScreen.OtherApp, null, null, null, PartyListTask);

        private static GameSnapshot Party(int cursor, int fade = 0) => new(GameScreen.OtherApp, null, null, null, PartyListTask, AppState: fade, PartyMonIndex: cursor);

        private static GameSnapshot Popup(int cursor, int fade = 0) => new(GameScreen.OtherApp, null, null, null, PartyPopupTask, AppState: fade, PartyMonIndex: cursor);

        // The move prompt for the getter's slot: question 5 "Delete a move?", 10 "Stop learning?".
        private static GameSnapshot Prompt(int question, int getter = 0)
            => new(GameScreen.BattleMoveLearnPrompt, null, null, 1, null, PartyMonIndex: getter, Question: question);

        // The forget-a-move screen: its cursor, the summary task running, the palette fade.
        private static GameSnapshot Forget(int cursor, uint? task = InputTask, int fade = 0)
            => new(GameScreen.OtherApp, null, null, cursor, task, AppState: fade);

        // The evolution scene of the slot (data[12]): the task's data[0], the battle window, and data[9] at the yes/no.
        private static GameSnapshot Scene(int state, int window, int slot = 0, int? question = null)
            => new(GameScreen.Evolution, null, null, state, null, Question: question, TextPrinter: window, PartyMonIndex: slot);

        // The scene's yes/no (data[0] 21, data[8] 4, the window 0): 5 "Delete a move?", 10 "Stop learning?".
        private static GameSnapshot SceneAsk(int question, int slot = 0) => Scene(21, 0, slot, question);

        private static GameSnapshot Message(BattleOutcome? outcome = null) => new(GameScreen.BattleMessage, outcome, null, null, null, TextPrinter: 8);

        private static GameSnapshot Menu(int cursor) => new(GameScreen.BattleMenu, null, null, cursor, null);

        // The training reader's menu carries the battler's slot; its move list the move cursor.
        private static GameSnapshot TMenu(int cursor, int battler = 0) => new(GameScreen.BattleMenu, null, null, cursor, null, PartyMonIndex: battler);

        private static GameSnapshot MoveList(int cursor) => new(GameScreen.BattleMoveList, null, null, cursor, null);

        // The level-up box's page for the getter's slot.
        private static GameSnapshot Box(int page, int getter = 0) => new(GameScreen.BattleExpPrompt, null, null, page, null, PartyMonIndex: getter);

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

        // A party-sized Gen 3 record with the species, level, moves, PP and HP set. pid % 24 == 0 keeps the substructures in
        // order; the key pid ^ otId is XORed over the twelve words. otId == pid is shiny (key 0, shiny value 0); otId 0 is not.
        private static byte[] Encode(uint pid, uint otId, ushort species, byte level, bool corrupt = false, ushort[]? moves = null,
            byte[]? pp = null, ushort hp = 0, ushort maxHp = 0)
        {
            byte[] plain = new byte[48];
            BitConverter.GetBytes(species).CopyTo(plain, 0);
            for (int i = 0; i < (moves ?? []).Length; i++)
            {
                BitConverter.GetBytes(moves![i]).CopyTo(plain, 12 + 2 * i);
            }
            (pp ?? []).CopyTo(plain, 20);
            ushort checksum = 0;
            for (int i = 0; i < 48; i += 2)
            {
                checksum += BitConverter.ToUInt16(plain, i);
            }
            byte[] block = new byte[Gen3PokemonParser.PartyBlockSize];
            BitConverter.GetBytes(pid).CopyTo(block, 0x00);
            BitConverter.GetBytes(otId).CopyTo(block, 0x04);
            BitConverter.GetBytes((ushort)(corrupt ? checksum + 1 : checksum)).CopyTo(block, 0x1C);
            uint key = pid ^ otId;
            for (int i = 0; i < 48; i += 4)
            {
                BitConverter.GetBytes(BitConverter.ToUInt32(plain, i) ^ key).CopyTo(block, 0x20 + i);
            }
            block[0x54] = level;
            BitConverter.GetBytes(hp).CopyTo(block, 0x56);
            BitConverter.GetBytes(maxHp).CopyTo(block, 0x58);
            return block;
        }

        private static readonly byte[] Plain = Encode(0x30, 0, Wurmple, 3);
        private static readonly byte[] Shiny = Encode(0x30, 0x30, Wurmple, 2);

        // Mudkip (internal 283) at full HP with four moves and their PP: Tackle, Growl, Mud-Slap, Mud Shot.
        private const ushort Mudkip = 283;
        private static readonly ushort[] MudkipMoves = [33, 45, 189, 341];
        private static byte[] Fighter(ushort hp = 25, byte[]? pp = null, uint pid = 0x60)
            => Encode(pid, 0, Mudkip, 10, moves: MudkipMoves, pp: pp ?? [35, 40, 10, 15], hp: hp, maxHp: 25);

        // Every move in these tests is a damaging one named by its id.
        private static PokemonMoveMetadata Damaging(int id) => new() { Id = id, Name = $"move{id}", DamageClass = "physical" };

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
            // The snapshot last served at each request that reads a party record.
            public List<GameSnapshot> RecordReadsAt { get; } = [];
            // The training reader, null in Flee mode; the damage picker, the move collection and the new-move question, which
            // throw unless a test sets them.
            public IGameStateReader? TrainingReader { get; set; }
            public Func<PokemonBattleState, int> PickMove { get; set; } = _ => throw new InvalidOperationException("pickMove called");
            public Func<int, PokemonMoveMetadata> MoveMetadata { get; set; } = _ => throw new InvalidOperationException("moveMetadata called");
            public Func<PokemonData, PokemonLevelUpMoveMetadata, Task<int>> DecideMove { get; set; } = (_, _) => throw new InvalidOperationException("decideMove called");

            // afterHandOff defaults to battleStart.
            public Task<string?> RunAsync(TimeSpan? battleStart = null, TimeSpan? stale = null, TimeSpan? checksum = null, TimeSpan? cursorWait = null,
                CancellationToken ct = default)
                => RsBattleEvent.RunAsync(State, new RsPointerService(), TrainingReader,
                    p => { Logged.Add(p); return Task.CompletedTask; },
                    p => { Alerted.Add(p); return Task.CompletedTask; },
                    s => { TrainerMessages.Add(s); return Task.CompletedTask; },
                    raw => { Analysed.Add(raw); return Verdict; },
                    PickMove, MoveMetadata, DecideMove,
                    battleStart ?? Long, battleStart ?? Long, stale ?? Long, checksum ?? Long, cursorWait ?? Long, ct);
        }

        // The records come from the queue, the last repeating; the lead's HP by the reader's stage; the counter from its queue;
        // the trainer read from trainer, by default a locked field with no script. In Battle mode reader is the training reader
        // and fleeReader the bot's own, by default one past the entry at once.
        private static Battle Start(PressedReader reader, ITarget? target = null, ushort flags = Wild, byte[][]? enemies = null,
            Func<int, ushort>? leadHp = null, uint[]? counter = null, TrainerBytes? trainer = null,
            BattleMode mode = BattleMode.Flee, IGameStateReader? fleeReader = null, byte[][]? party = null,
            TrainType trainType = TrainType.None, int? switchSlot = null)
        {
            byte[][] partyRecords = party ?? [Fighter()];
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
                    case PartyCount:
                        bytes = [(byte)partyRecords.Length];
                        break;
                    case PlayerParty when block.Length == 6 * Gen3PokemonParser.PartyBlockSize:
                        bytes = new byte[block.Length];
                        for (int slot = 0; slot < partyRecords.Length; slot++)
                        {
                            partyRecords[slot].CopyTo(bytes, slot * Gen3PokemonParser.PartyBlockSize);
                        }
                        break;
                    case MoveToLearn:
                        // Bide.
                        bytes = BitConverter.GetBytes((ushort)117);
                        break;
                    case uint record when block.Length == Gen3PokemonParser.PartyBlockSize && record >= PlayerParty && record < PlayerParty + 600:
                        bytes = partyRecords[(record - PlayerParty) / Gen3PokemonParser.PartyBlockSize];
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
                    if (blocks.Any(b => b.Anchor >= PlayerParty && b.Anchor < PlayerParty + 600))
                    {
                        battle.RecordReadsAt.Add(reader.Last);
                    }
                    return blocks.Select(Answer).ToArray();
                },
                Pressed = macro =>
                {
                    // A at the action menu only ever with the cursor on RUN, in Battle mode on FIGHT, and when switching on
                    // POKéMON, in every test.
                    if (macro == Confirm && reader.Last.Screen == GameScreen.BattleMenu)
                    {
                        Assert.Contains(reader.Last.Prompt, mode != BattleMode.Battle ? [3]
                            : battle!.State.TrainType == TrainType.Switch ? new int?[] { 0, 2, 3 } : [0, 3]);
                    }
                    battle!.Presses.Add(Name(macro, reader.Last));
                    reader.Advance();
                },
                State = mode == BattleMode.Battle ? fleeReader ?? new PressedReader([Turn]) : reader,
            };
            GameState state = new()
            {
                Bot = bot, Profile = bot.Profile, Target = target ?? new NoTarget(), Milestone = "Route 101", BattleMode = mode,
                TrainType = trainType, SwitchSlot = switchSlot,
            };
            state.State.Push(EventState.BattleStart);
            battle = new Battle { Bot = bot, State = state, Reader = reader, TrainingReader = mode == BattleMode.Battle ? reader : null };
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
            Assert.True(battle.State.StoppedForTrainer);
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
            new TrainerBytes(1, 0, 0, 1024),          // an id whose flag is past the 0x120 bytes read
        };

        [Theory]
        [MemberData(nameof(NotAChallenge))]
        public async Task TrainerChallenge_StaleOrNoScript_IsNotATrainer(TrainerBytes read)
        {
            Battle battle = Start(new PressedReader([Locked]), trainer: read);

            string? stop = await battle.RunAsync(battleStart: Short);

            Assert.Equal(RsBattleEvent.NotWild, stop);
            Assert.Equal([EventState.Waiting], battle.State.State);
            Assert.False(battle.State.StoppedForTrainer);
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

        // The battle type is the backstop: an operator's press at the speech before the hand-off.
        [Fact]
        public async Task TrainerBattleType_Backstop_SameSentenceOneMessage()
        {
            Battle battle = Start(new PressedReader([Turn]), flags: 0x000C);

            string? stop = await battle.RunAsync();

            Assert.Equal(Route102, stop);
            Assert.Equal([EventState.Waiting], battle.State.State);
            Assert.True(battle.State.StoppedForTrainer);
            Assert.Equal([Route102], battle.TrainerMessages);
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

        [Fact]
        public void RsBattleEvent_FleeHuntBuildsNeitherCalculatorNorDiscord()
        {
            int built = 0;
            var provider = new ServiceCollection()
                .AddSingleton(new PokemonPersistenceService(null!, null!, null!))
                .AddSingleton(new ShinyAlertService(null!, Options.Create(new ApplicationSettings())))
                .AddSingleton(new LegalityService(NullLogger<LegalityService>.Instance))
                .AddSingleton<SmogonDamageService>(_ => { built++; throw new InvalidOperationException("the calculator was built"); })
                .AddSingleton<DiscordBotService>(_ => { built++; throw new InvalidOperationException("Discord was built"); })
                .BuildServiceProvider();

            Assert.IsType<RsBattleEvent>(RsProfile.Create(new PokemonMetadataService(null!)).CreateBattleEvent!(provider));
            // A flee battle never calls the two delegates (FleeMode_ReadsAndPressesAreTodays), so neither is built.
            Assert.Equal(0, built);
        }

        // The flee path's reads, exactly: the trainer read at the locked field the scripts start on, the entry and type read,
        // one menu read per action menu, the counter at the end.
        [Fact]
        public async Task FleeMode_ReadsAndPressesAreTodays()
        {
            (uint, int)[] trainer = [(FieldControlsLock, 1), (InBattle, 1), (ScriptContextStatus, 1), (TrainerBattleOpponent, 2), (EventFlags, 0x120), (PlayerLocation, 2)];
            (uint, int)[] entry = [(BattleTypeFlags, 2), (VBlankCounter, 4)];
            (uint, int)[] menu = [(EnemyParty, 100), (LeadHp, 2)];
            (uint, int)[] end = [(VBlankCounter, 4)];
            foreach ((PressedReader reader, string[] presses, int menus) in new[]
            {
                (Fled(), new[] { "Advance", "ToRun", "Confirm", "Advance" }, 1),
                (FailedEscape(), new[] { "Advance", "ToRun", "Confirm", "Advance", "Confirm", "Advance" }, 2),
            })
            {
                Battle battle = Start(reader);

                string? stop = await battle.RunAsync();

                Assert.Null(stop);
                Assert.Null(battle.TrainingReader);
                Assert.Equal(presses, battle.Presses);
                Assert.Equal([trainer, entry, .. Enumerable.Repeat(menu, menus), end],
                    battle.Requests.Select(r => r.Select(b => (b.Anchor, b.Length)).ToArray()));
            }
        }

        // Battle mode: the entry, the trainer read, the hand-off and the type read go through the flee reader, whose field
        // snapshot changes here as the training reader's would not; the training reader is read only after the type read.
        [Fact]
        public async Task BattleMode_EntryHandOffAndTypeReadUseTheFleeReader()
        {
            // Each read notes whether the battle type had been read by then.
            Battle? battle = null;
            bool TypeRead() => battle!.Requests.Any(r => r.Any(b => b.Anchor == BattleTypeFlags));
            List<bool> fleeReads = [];
            List<bool> trainingReads = [];
            PressedReader flee = new([Locked, Locked with { TextPrinter = 2 }, Locked with { TextPrinter = 5 }, Turn]);
            // Too hurt to fight, so the battle runs.
            PressedReader training = new(
                [Message(), Turn, TMenu(0)],
                [TMenu(0), TMenu(3)],
                [TMenu(3), Turn, Message(BattleOutcome.Fled)],
                [Message(BattleOutcome.Fled), Turn, Free]);
            battle = Start(training, mode: BattleMode.Battle, fleeReader: new LoggedReader(flee, () => fleeReads.Add(TypeRead())),
                party: [Fighter(hp: 5)]);
            battle.TrainingReader = new LoggedReader(training, () => trainingReads.Add(TypeRead()));

            string? stop = await battle.RunAsync();

            Assert.Null(stop);
            // The entry, the trainer read and the hand-off wait over the changing field.
            Assert.True(fleeReads.Count >= 4);
            Assert.All(fleeReads, Assert.False);
            Assert.NotEmpty(trainingReads);
            Assert.All(trainingReads, Assert.True);
        }

        // Train Standard: "Wild ... appeared!", the menu at FIGHT, the move list through the cursors given, the move, "...
        // fainted!" and the won battle's free field.
        private static GameSnapshot[][] FightStages(params int[] cursors)
        {
            List<GameSnapshot[]> stages = [[Turn, Message()], [Message(), Turn, TMenu(0)], [TMenu(0), Turn, MoveList(cursors[0])]];
            for (int i = 1; i < cursors.Length; i++)
            {
                stages.Add([MoveList(cursors[i - 1]), MoveList(cursors[i])]);
            }
            stages.Add([MoveList(cursors[^1]), Turn, Message(BattleOutcome.Won)]);
            stages.Add([Message(BattleOutcome.Won), Turn, Free]);
            return [.. stages];
        }

        // A Battle-mode battle whose picker answers slot and whose moves are all damaging.
        private static Battle Train(GameSnapshot[][] stages, int slot = 0, byte[][]? party = null, byte[][]? enemies = null,
            ITarget? target = null)
        {
            Battle battle = Start(new PressedReader(stages), mode: BattleMode.Battle, party: party, enemies: enemies, target: target);
            battle.MoveMetadata = Damaging;
            battle.PickMove = _ => slot;
            return battle;
        }

        private static async Task<string> CaptureAsync(Func<Task> run)
        {
            TextWriter console = Console.Out;
            StringWriter log = new();
            Console.SetOut(log);
            try
            {
                await run();
            }
            finally
            {
                Console.SetOut(console);
            }
            return log.ToString();
        }

        [Fact]
        public async Task Train_FightsWithThePickedMove_EachPressConfirmed()
        {
            Battle battle = Train(FightStages(0, 1, 3));
            List<PokemonBattleState> picked = [];
            battle.PickMove = p => { picked.Add(p); return 3; };

            string log = await CaptureAsync(async () => Assert.Null(await battle.RunAsync()));

            // A only at the menu cursor 0 (Fight) and at the list cursor 3 (Move3).
            Assert.Equal(["Advance", "Fight", "Right", "Down", "Move3", "Advance"], battle.Presses);
            Assert.Equal([EventState.BattleComplete], battle.State.State);
            IReadOnlyList<ChainBlockRequest> menu = Assert.Single(battle.Requests, r => r.Any(b => b.Anchor == EnemyParty));
            Assert.Equal([(EnemyParty, 100), (PlayerParty, 100)], menu.Select(b => (b.Anchor, b.Length)));
            PokemonBattleState state = Assert.Single(picked);
            Assert.Equal([35, 40, 10, 15], state.ActivePokemon.MovePP.Select(pp => (int)pp));
            Assert.Equal(265, state.EnemyPokemon.Pokemon.Species);
            Assert.Contains("[RsBattle] BattleConfirm on FIGHT", log);
            Assert.Contains("confirmed by the move list", log);
            Assert.Contains("[RsBattle] move 3 move341 chosen by the damage picker, lead HP 25/25 PP 15", log);
            Assert.Contains("[RsBattle] Right on the move list 0 -> 1", log);
            Assert.Contains("[RsBattle] Down on the move list 1 -> 3", log);
            Assert.Contains("confirmed by cursor 3", log);
            Assert.Contains("[RsBattle] BattleConfirm on move 3", log);
        }

        [Theory]
        [InlineData(new[] { 1, 0, 2 }, new[] { "Left", "Down" })]
        [InlineData(new[] { 2, 0, 1 }, new[] { "Up", "Right" })]
        [InlineData(new[] { 3, 2, 0 }, new[] { "Left", "Up" })]
        [InlineData(new[] { 0 }, new string[0])]
        public async Task MoveCursor_ClearsBitsFirst(int[] cursors, string[] directions)
        {
            Battle battle = Train(FightStages(cursors), slot: cursors[^1]);

            Assert.Null(await battle.RunAsync());

            Assert.Equal(["Advance", "Fight", .. directions, $"Move{cursors[^1]}", "Advance"], battle.Presses);
        }

        // Each training press with its happy script, the stage its press leads to (which opens with the snapshot it was sent
        // at), and a snapshot that differs from that one without confirming the press.
        public static TheoryData<string> RepairCases => new()
        {
            "FIGHT", "move A", "direction", "box page 1", "box page 2", "Yes", "B", "Stop learning", "Down", "forget A",
            "to POKéMON", "POKéMON A", "party Down", "slot A", "SHIFT",
        };

        private static (GameSnapshot[][] Stages, int After, GameSnapshot Changed, Action<Battle> Setup) RepairCase(string name) => name switch
        {
            "FIGHT" => (FightStages(0), 2, Turn, _ => { }),
            "move A" => (FightStages(0), 3, MoveList(0) with { Outcome = BattleOutcome.Won }, _ => { }),
            "direction" => (FightStages(0, 1), 3, Turn, b => b.PickMove = _ => 1),
            "box page 1" => (Won([Message(), Turn, Box(1)], [Box(1), Box(2)], [Box(2), Turn, Free]), 5, Turn, _ => { }),
            "box page 2" => (Won([Message(), Turn, Box(1)], [Box(1), Box(2)], [Box(2), Turn, Free]), 6, Box(2) with { Outcome = BattleOutcome.Won }, _ => { }),
            "Yes" => (ReplaceSlotOne(), 5, Prompt(5) with { Outcome = BattleOutcome.Won }, b => b.DecideMove = Answer(1)),
            "Down" => (ReplaceSlotOne(), 6, Forget(0, fade: 1), b => b.DecideMove = Answer(1)),
            "forget A" => (ReplaceSlotOne(), 7, Forget(1, fade: 1), b => b.DecideMove = Answer(1)),
            "B" => (Refuse(), 5, Prompt(5) with { Outcome = BattleOutcome.Won }, b => b.DecideMove = Answer(-1)),
            "Stop learning" => (Refuse(), 6, Prompt(10) with { Outcome = BattleOutcome.Won }, b => b.DecideMove = Answer(-1)),
            // The switch to slot 1: Down to POKéMON leads to stage 2, then A, the party Down, A on the slot and SHIFT.
            "to POKéMON" => (SwitchToFree(), 2, Turn, SwitchToSlot1),
            "POKéMON A" => (SwitchToFree(), 3, TMenu(2) with { Outcome = BattleOutcome.Won }, SwitchToSlot1),
            "party Down" => (SwitchToFree(), 4, Party(0, fade: 1), SwitchToSlot1),
            "slot A" => (SwitchToFree(), 5, Party(1, fade: 1), SwitchToSlot1),
            "SHIFT" => (SwitchToFree(), 6, Popup(1, fade: 1), SwitchToSlot1),
            _ => throw new ArgumentException(name),
        };

        // One switch try to slot 1, ended by the free field after SHIFT so the theory runs short: the presses after the one
        // under test do not matter to it.
        private static GameSnapshot[][] SwitchToFree() => [[Turn, Message()], [Message(), Turn, TMenu(0)], .. SwitchTry(1, Free)];

        private static void SwitchToSlot1(Battle battle)
        {
            battle.State.TrainType = TrainType.Switch;
            battle.State.SwitchSlot = 2;
        }

        private static Func<PokemonData, PokemonLevelUpMoveMetadata, Task<int>> Answer(int slot) => (_, _) => Task.FromResult(slot);

        // "Delete a move?" answered Yes over slot 1; and answered No, then "Stop learning?".
        private static GameSnapshot[][] ReplaceSlotOne() => Won(
            [Message(), Turn, Prompt(5)], [Prompt(5), Turn, Forget(0)], [Forget(0), Forget(1)], [Forget(1), Forget(1, DestroyTask), Turn, Message()],
            [Message(), Turn, Free]);

        private static GameSnapshot[][] Refuse() => Won([Message(), Turn, Prompt(5)], [Prompt(5), Turn, Prompt(10)], [Prompt(10), Turn, Free]);

        // Train Standard with move 0, then "... fainted!" and the stages given after it, the first opening with that message.
        private static GameSnapshot[][] Won(params GameSnapshot[][] after) =>
        [
            [Turn, Message()],
            [Message(), Turn, TMenu(0)],
            [TMenu(0), Turn, MoveList(0)],
            [MoveList(0), Turn, Message()],
            .. after,
        ];

        [Fact]
        public async Task LevelUpBox_TwoConfirmedPresses()
        {
            // Page 1 is read twice after its A before page 2: the second A waits for page 2.
            Battle battle = Train(Won([Message(), Turn, Box(1)], [Box(1), Box(1), Box(2)], [Box(2), Turn, Free]));

            string log = await CaptureAsync(async () => Assert.Null(await battle.RunAsync()));

            Assert.Equal(["Advance", "Fight", "Move0", "Advance", "Box1", "Box2"], battle.Presses);
            Assert.Equal([EventState.BattleComplete], battle.State.State);
            Assert.Contains("[RsBattle] level-up box page 1 for slot 0: BattleConfirm", log);
            Assert.Contains("confirmed by page 2", log);
            Assert.Contains("[RsBattle] level-up box page 2 for slot 0: BattleConfirm", log);
        }

        [Fact]
        public async Task TwoLevelsInOneBattle_OnePairPerBox()
        {
            // Boxes 1, 2, 3, 0, 1, 2, 3, then "... learned ...!" into a free slot.
            Battle battle = Train(Won(
                [Message(), Turn, Box(1)], [Box(1), Box(2)], [Box(2), Turn, Turn, Box(1)], [Box(1), Box(2)], [Box(2), Turn, Message()],
                [Message(), Turn, Free]));

            Assert.Null(await battle.RunAsync());

            Assert.Equal(["Advance", "Fight", "Move0", "Advance", "Box1", "Box2", "Box1", "Box2", "Advance"], battle.Presses);
            Assert.Equal([EventState.BattleComplete], battle.State.State);
        }

        [Fact]
        public async Task TwoPokemonLevel_EachBoxAndPromptIsForItsGetter()
        {
            // Slot 0's box, then slot 1's straight from 3 to 1, then slot 1's prompt.
            Battle battle = Train(Won(
                [Message(), Turn, Box(1, 0)], [Box(1, 0), Box(2, 0)], [Box(2, 0), Turn, Box(1, 1)], [Box(1, 1), Box(2, 1)],
                [Box(2, 1), Turn, Message()], [Message(), Turn, Prompt(5, 1)], [Prompt(5, 1), Turn, Prompt(10, 1)], [Prompt(10, 1), Turn, Free]),
                party: [Fighter(), Fighter(pid: 0x78)]);
            List<uint> asked = [];
            battle.DecideMove = (p, _) => { asked.Add(p.PID); return Task.FromResult(-1); };

            string log = await CaptureAsync(async () => Assert.Null(await battle.RunAsync()));

            Assert.Equal(["Advance", "Fight", "Move0", "Advance", "Box1", "Box2", "Box1", "Box2", "Advance", "No", "StopYes"], battle.Presses);
            IReadOnlyList<ChainBlockRequest> prompt = Assert.Single(battle.Requests, r => r.Any(b => b.Anchor == MoveToLearn));
            Assert.Equal([(MoveToLearn, 2), (0x030043C4u, 100)], prompt.Select(b => (b.Anchor, b.Length)));
            Assert.Equal([0x78u], asked);
            Assert.Contains("[RsBattle] level-up box page 1 for slot 0", log);
            Assert.Contains("[RsBattle] level-up box page 1 for slot 1", log);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task PromptAfterAnyBox(bool afterTheFirst)
        {
            GameSnapshot[][] refused = [[Prompt(5), Turn, Prompt(10)]];
            GameSnapshot[][] stages = afterTheFirst
                ? Won([[Message(), Turn, Box(1)], [Box(1), Box(2)], [Box(2), Turn, Prompt(5)], .. refused, [Prompt(10), Turn, Box(1)], [Box(1), Box(2)], [Box(2), Turn, Free]])
                : Won([[Message(), Turn, Box(1)], [Box(1), Box(2)], [Box(2), Turn, Box(1)], [Box(1), Box(2)], [Box(2), Turn, Prompt(5)], .. refused, [Prompt(10), Turn, Free]]);
            Battle battle = Train(stages);
            battle.DecideMove = Answer(-1);

            Assert.Null(await battle.RunAsync());

            Assert.Equal(afterTheFirst
                ? ["Advance", "Fight", "Move0", "Advance", "Box1", "Box2", "No", "StopYes", "Box1", "Box2"]
                : ["Advance", "Fight", "Move0", "Advance", "Box1", "Box2", "Box1", "Box2", "No", "StopYes"], battle.Presses);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public async Task MovePrompt_Yes_ReplacesTheSlot(int slot)
        {
            // The screen opens with the cursor at 0 and no task while it fades in, then the input task during the fade,
            // then the input task with the fade off; only then is anything pressed.
            List<GameSnapshot[]> forget = [[Prompt(5), Turn, Forget(0, null, 1), Forget(0, InputTask, 1), Forget(0)]];
            for (int cursor = 0; cursor < slot; cursor++)
            {
                forget.Add([Forget(cursor), Forget(cursor + 1)]);
            }
            forget.Add([Forget(slot), Forget(slot, DestroyTask), Forget(slot, null), Turn, Message()]);
            Battle battle = Train(Won([[Message(), Turn, Prompt(5)], .. forget, [Message(), Turn, Free]]));
            List<PokemonLevelUpMoveMetadata> offered = [];
            battle.DecideMove = (_, m) => { offered.Add(m); return Task.FromResult(slot); };

            string log = await CaptureAsync(async () => Assert.Null(await battle.RunAsync()));

            Assert.Equal(["Advance", "Fight", "Move0", "Advance", "Yes", .. Enumerable.Repeat("ForgetDown", slot), $"Forget{slot}", "Advance"], battle.Presses);
            Assert.Equal([EventState.BattleComplete], battle.State.State);
            PokemonLevelUpMoveMetadata move = Assert.Single(offered);
            Assert.Equal((117, "move117", 10), (move.Id, move.Name, move.Level));
            Assert.Contains($"[RsBattle] Delete a move? slot 0  learning move117 (117): Yes over slot {slot} move{MudkipMoves[slot]}", log);
            Assert.Contains($"[RsBattle] BattleConfirm on forget slot {slot}", log);
            Assert.Contains("confirmed by the summary task closing", log);
        }

        [Fact]
        public async Task MovePrompt_No_BThenStopLearning()
        {
            // -1 is also what the question answers when Discord is not logged in. After B the tracker reads 4, then 0, then
            // 1 with the script on "Stop learning?".
            Battle battle = Train(Won([Message(), Turn, Prompt(5)], [Prompt(5), Turn, Turn with { Outcome = BattleOutcome.Won }, Prompt(10)],
                [Prompt(10), Turn, Message()], [Message(), Turn, Free]));
            battle.DecideMove = Answer(-1);

            string log = await CaptureAsync(async () => Assert.Null(await battle.RunAsync()));

            Assert.Equal(["Advance", "Fight", "Move0", "Advance", "No", "StopYes", "Advance"], battle.Presses);
            Assert.Contains("[RsBattle] B at Delete a move?", log);
            Assert.Contains("[RsBattle] BattleConfirm (Yes) at Stop learning?", log);
        }

        [Fact]
        public async Task TwoPromptsInOneBattle_OneReplacedOneRefused()
        {
            Battle battle = Train(Won(
                [Message(), Turn, Box(1, 0)], [Box(1, 0), Box(2, 0)], [Box(2, 0), Turn, Prompt(5, 0)],
                [Prompt(5, 0), Turn, Forget(0)], [Forget(0), Forget(1)], [Forget(1), Forget(1, DestroyTask), Turn, Message()],
                [Message(), Turn, Box(1, 1)], [Box(1, 1), Box(2, 1)], [Box(2, 1), Turn, Prompt(5, 1)], [Prompt(5, 1), Turn, Prompt(10, 1)],
                [Prompt(10, 1), Turn, Free]),
                party: [Fighter(), Fighter(pid: 0x78)]);
            battle.DecideMove = (p, _) => Task.FromResult(p.PID == 0x60 ? 1 : -1);

            Assert.Null(await battle.RunAsync());

            Assert.Equal(["Advance", "Fight", "Move0", "Advance", "Box1", "Box2", "Yes", "ForgetDown", "Forget1", "Advance",
                "Box1", "Box2", "No", "StopYes"], battle.Presses);
        }

        [Fact]
        public async Task Prompt_WaitsForTheAnswer_PressesNothing_EndsOnCancel()
        {
            Battle battle = Train(Won([Message(), Turn, Prompt(5)], [Prompt(5)]));
            TaskCompletionSource<int> never = new();
            battle.DecideMove = (_, _) => never.Task;
            using CancellationTokenSource cancel = new(TimeSpan.FromMilliseconds(300));

            string log = await CaptureAsync(() => Assert.ThrowsAnyAsync<OperationCanceledException>(() => battle.RunAsync(ct: cancel.Token)));

            Assert.Equal(["Advance", "Fight", "Move0", "Advance"], battle.Presses);
            Assert.Contains("[RsBattle] asking Discord: slot 0  learning move117 (117); nothing is pressed until the answer", log);
        }

        [Fact]
        public async Task Train_PartyReadAfterTheBattle_OnceAndOnlyInBattleMode()
        {
            Battle train = Train(FightStages(0), party: [Fighter(), Fighter(pid: 0x78)]);
            Battle flee = Start(Fled());

            string log = await CaptureAsync(async () =>
            {
                Assert.Null(await train.RunAsync());
                Assert.Null(await flee.RunAsync());
            });

            IReadOnlyList<ChainBlockRequest> read = Assert.Single(train.Requests, r => r.Any(b => b.Anchor == PartyCount));
            Assert.Equal([(PartyCount, 1), (PlayerParty, 600)], read.Select(b => (b.Anchor, b.Length)));
            Assert.Same(read, train.Requests[^1]);
            Assert.DoesNotContain(flee.Requests, r => r.Any(b => b.Anchor == PartyCount));
            // Mudkip is national 258; the names are empty with no metadata seeded.
            Assert.Contains("[RsBattle] party after the battle: slot 0  (258) Lv 10 moves 33,45,189,341", log);
            Assert.Contains("[RsBattle] party after the battle: slot 1  (258) Lv 10 moves 33,45,189,341", log);
        }

        [Fact]
        public async Task ForgetScreenOutsideAYes_StaleStopNamesIt()
        {
            Battle battle = Train(Won([Message(), Turn, Forget(0)]));

            string? stop = await battle.RunAsync(stale: TimeSpan.FromMilliseconds(200));

            Assert.Contains(Forget(0).ToString(), stop);
            Assert.Equal(["Advance", "Fight", "Move0", "Advance"], battle.Presses);
        }

        private static readonly TimeSpan RepairWait = TimeSpan.FromMilliseconds(200);

        [Theory]
        [MemberData(nameof(RepairCases))]
        public async Task Repair_OnePressOnlyWhenTheSnapshotStayedTheSame(string name)
        {
            (GameSnapshot[][] stages, int after, GameSnapshot changed, Action<Battle> setup) = RepairCase(name);
            GameSnapshot at = stages[after][0];
            // Two Pokémon, so a switch case has its battler's record; the other cases read slot 0 only.
            byte[][] party = [Fighter(), Fighter(pid: 0x78)];

            // The happy path, for its press list.
            Battle happy = Train(stages, party: party);
            setup(happy);
            Assert.Null(await happy.RunAsync(cursorWait: RepairWait));
            List<string> presses = happy.Presses;
            string press = presses[after - 1];

            // Unchanged for cursorWait: exactly one press more, right after the first.
            Battle dropped = Train([.. stages[..after], [at], .. stages[after..]], party: party);
            setup(dropped);
            Assert.Null(await dropped.RunAsync(cursorWait: RepairWait));
            Assert.Equal([.. presses[..after], press, .. presses[after..]], dropped.Presses);

            // Changed but not yet confirmed, for longer than cursorWait: no second press.
            Battle moving = Train([.. stages[..after], [at, .. Enumerable.Repeat(changed, 7), .. stages[after][1..]], .. stages[(after + 1)..]], party: party);
            setup(moving);
            Assert.Null(await moving.RunAsync(cursorWait: RepairWait));
            Assert.Equal(presses, moving.Presses);

            // Changed, then back to the pressed snapshot past cursorWait: the key was read, so still no second press.
            Battle back = Train([.. stages[..after], [at, changed, .. Enumerable.Repeat(at, 12), .. stages[after][1..]], .. stages[(after + 1)..]], party: party);
            setup(back);
            Assert.Null(await back.RunAsync(cursorWait: RepairWait));
            Assert.Equal(presses, back.Presses);

            // Unchanged after the second press too: no third, and the stale stop names the snapshot.
            Battle stuck = Train([.. stages[..after], [at], [at]], party: party);
            setup(stuck);
            string? stop = await stuck.RunAsync(cursorWait: RepairWait, stale: TimeSpan.FromMilliseconds(300));
            Assert.Contains(at.ToString(), stop);
            Assert.Equal([.. presses[..after], press], stuck.Presses);
            Assert.Equal([EventState.Waiting], stuck.State.State);
        }

        [Fact]
        public async Task Train_ReadAFrameEarly_NoRepair()
        {
            // The cursor reads 0 for two polls after Right, then 1.
            GameSnapshot[][] stages = FightStages(0, 1);
            stages[3] = [MoveList(0), MoveList(0), MoveList(1)];
            Battle battle = Train(stages, slot: 1);

            Assert.Null(await battle.RunAsync(cursorWait: TimeSpan.FromSeconds(1)));

            Assert.Equal(["Advance", "Fight", "Right", "Move1", "Advance"], battle.Presses);
        }

        // The menu, then today's RUN: ToRun, Confirm on RUN, "Got away safely!".
        private static GameSnapshot[][] TrainFleeStages() =>
        [
            [Turn, Message()],
            [Message(), Turn, TMenu(0)],
            [TMenu(0), TMenu(3)],
            [TMenu(3), Turn, Message(BattleOutcome.Fled)],
            [Message(BattleOutcome.Fled), Turn, Free],
        ];

        [Fact]
        public async Task Train_TooHurt_Flees()
        {
            Battle battle = Train(TrainFleeStages(), party: [Fighter(hp: 6)]);
            battle.PickMove = _ => throw new InvalidOperationException("pickMove called");

            string log = await CaptureAsync(async () => Assert.Null(await battle.RunAsync()));

            Assert.Equal(["Advance", "ToRun", "Confirm", "Advance"], battle.Presses);
            Assert.Contains("[RsBattle] fleeing: lead HP 6/25 is too low", log);
        }

        [Fact]
        public async Task Train_NoDamagingPp_Flees()
        {
            Battle battle = Train(TrainFleeStages(), party: [Fighter(pp: [0, 40, 0, 0])]);
            // Growl (45) is the only move with PP, and it does no damage.
            battle.MoveMetadata = id => id == 45 ? new PokemonMoveMetadata { Id = 45, Name = "Growl", DamageClass = "status" } : Damaging(id);
            battle.PickMove = _ => throw new InvalidOperationException("pickMove called");

            string log = await CaptureAsync(async () => Assert.Null(await battle.RunAsync()));

            Assert.Equal(["Advance", "ToRun", "Confirm", "Advance"], battle.Presses);
            Assert.Contains("[RsBattle] fleeing: no damaging PP", log);
        }

        [Fact]
        public async Task Train_PickerFindsNothing_Flees()
        {
            Battle battle = Train(TrainFleeStages(), slot: -1);

            string log = await CaptureAsync(async () => Assert.Null(await battle.RunAsync()));

            Assert.Equal(["Advance", "ToRun", "Confirm", "Advance"], battle.Presses);
            Assert.Contains("[RsBattle] fleeing: the picker found no move", log);
        }

        [Fact]
        public async Task Train_LeadHpZero_StopsFainted()
        {
            Battle battle = Train(TrainFleeStages(), party: [Fighter(hp: 0)]);

            string? stop = await battle.RunAsync();

            Assert.Equal(RsBattleEvent.Fainted, stop);
            Assert.Equal(["Advance"], battle.Presses);
            Assert.Equal([EventState.Waiting], battle.State.State);
        }

        [Fact]
        public async Task IllegalShiny_TrainStandard_LoggedOnceUnalertedThenFights()
        {
            Battle battle = Train(FightStages(0), target: new ShinyTarget(), enemies: [Shiny]);
            battle.Verdict = new(false, "PID-IV mismatch");

            string log = await CaptureAsync(async () => Assert.Null(await battle.RunAsync()));

            PersistPokemonData logged = Assert.Single(battle.Logged);
            Assert.True(logged.LegalityFailed);
            Assert.Empty(battle.Alerted);
            Assert.Null(battle.State.TargetPokemon);
            Assert.Equal(Shiny, Assert.Single(battle.Analysed));
            Assert.Equal(["Advance", "Fight", "Move0", "Advance"], battle.Presses);
            Assert.Contains("logged as not shiny, fighting", log);
        }

        [Fact]
        public async Task LegalShiny_TrainStandard_IsTheTargetNothingPressed()
        {
            Battle battle = Train(FightStages(0), target: new ShinyTarget(), enemies: [Shiny]);
            battle.PickMove = _ => throw new InvalidOperationException("pickMove called");

            Assert.Null(await battle.RunAsync());

            PersistPokemonData logged = Assert.Single(battle.Logged);
            Assert.Same(logged, Assert.Single(battle.Alerted));
            Assert.Same(logged, battle.State.TargetPokemon);
            Assert.Equal(["Advance"], battle.Presses);
            Assert.Equal([EventState.Waiting], battle.State.State);
        }

        // The evolution scene after the won battle's last message, from the research's R5 table: data[0] 1, then 2 ("What? …
        // is evolving!", which prints and goes on by itself), the animation, the window's fanfare hold at 10, then
        // "Congratulations!" waiting at 8. Then the stages given, the first opening with that wait.
        private static GameSnapshot[][] Evolves(int slot, params GameSnapshot[][] after) => Won(
        [
            [Message(), Turn, Scene(1, 0, slot), Scene(2, 1, slot), Scene(2, 2, slot), Scene(2, 3, slot), Scene(2, 6, slot), Scene(3, 0, slot),
                Scene(13, 0, slot), Scene(14, 10, slot), Scene(14, 8, slot)],
            .. after,
        ]);

        // "… is trying to learn …" and "… can't learn more than four moves" waiting at 21, then "Delete a move?" printing
        // (the window at 4) and the yes/no.
        private static GameSnapshot[][] SceneAsks(int slot) =>
        [
            [Scene(14, 8, slot), Scene(14, 0, slot), Scene(21, 1, slot), Scene(21, 8, slot)],
            [Scene(21, 8, slot), Scene(21, 2, slot), Scene(21, 3, slot), Scene(21, 8, slot)],
            [Scene(21, 8, slot), Scene(21, 3, slot), Scene(21, 4, slot), SceneAsk(5, slot)],
        ];

        private static readonly string[] WonPresses = ["Advance", "Fight", "Move0", "Advance"];

        // The lead (0) and the Exp. Share holder (1) evolving: the prompt reads the evolving slot's record, and No is B at
        // "Delete a move?", A at "Stop learning?", then "… did not learn …" at data[0] 14.
        [Theory]
        [InlineData(0, 0x03004360u, 0x60u)]
        [InlineData(1, 0x030043C4u, 0x78u)]
        public async Task Evolution_Prompt_NoThenStopLearning_ForTheEvolvingSlot(int slot, uint record, uint pid)
        {
            Battle battle = Train(Evolves(slot,
                [.. SceneAsks(slot),
                [SceneAsk(5, slot), Scene(21, 3, slot), Scene(21, 4, slot), SceneAsk(10, slot)],
                [SceneAsk(10, slot), Scene(14, 1, slot), Scene(14, 8, slot)],
                [Scene(14, 8, slot), Scene(14, 0, slot), Free]]),
                party: [Fighter(), Fighter(pid: 0x78)]);
            List<uint> asked = [];
            battle.DecideMove = (p, _) => { asked.Add(p.PID); return Task.FromResult(-1); };

            string log = await CaptureAsync(async () => Assert.Null(await battle.RunAsync()));

            Assert.Equal([.. WonPresses, "SceneText14", "SceneText21", "SceneText21", "SceneNo", "SceneStopYes", "SceneText14"], battle.Presses);
            Assert.Equal([EventState.BattleComplete], battle.State.State);
            IReadOnlyList<ChainBlockRequest> prompt = Assert.Single(battle.Requests, r => r.Any(b => b.Anchor == MoveToLearn));
            Assert.Equal([(MoveToLearn, 2), (record, 100)], prompt.Select(b => (b.Anchor, b.Length)));
            Assert.Equal([pid], asked);
            Assert.Contains($"[RsBattle] BattleAdvance in the evolution scene of slot {slot} at data[0] 14 at window 8", log);
            Assert.Contains($"[RsBattle] asking Discord: slot {slot}  learning move117 (117)", log);
            Assert.Contains("[RsBattle] B at Delete a move?", log);
        }

        // Yes over slot 2: A at the yes/no, nothing while the scene fades out (21 with data[8] 5) and the forget screen opens,
        // Down twice and A on the screen, then "1, 2 and… Poof!" (the window held at 4), "… forgot …", "… learned …!" and
        // the last text at 20. The party read after the scene shows the evolved species.
        [Fact]
        public async Task Evolution_Prompt_Yes_ForgetScreenThenTheTexts_ThenTheEvolvedSpecies()
        {
            // Marshtomp: internal 284, national 259.
            byte[] marshtomp = Encode(0x60, 0, 284, 16, moves: MudkipMoves, pp: [35, 40, 10, 15], hp: 40, maxHp: 40);
            Battle battle = Train(Evolves(0,
                [.. SceneAsks(0),
                [SceneAsk(5), Scene(21, 0), Forget(0, null, 1), Forget(0, InputTask, 1), Forget(0)],
                [Forget(0), Forget(1)],
                [Forget(1), Forget(2)],
                [Forget(2), Forget(2, DestroyTask), Forget(2, null), Scene(21, 4), Scene(21, 8)],
                [Scene(21, 8), Scene(21, 1), Scene(21, 8)],
                [Scene(21, 8), Scene(19, 3), Scene(19, 8)],
                [Scene(19, 8), Scene(20, 10), Scene(20, 8)],
                [Scene(20, 8), Scene(20, 0), Free]]),
                party: [marshtomp]);
            battle.DecideMove = Answer(2);

            string log = await CaptureAsync(async () => Assert.Null(await battle.RunAsync()));

            Assert.Equal([.. WonPresses, "SceneText14", "SceneText21", "SceneText21", "SceneYes", "ForgetDown", "ForgetDown", "Forget2",
                "SceneText21", "SceneText21", "SceneText19", "SceneText20"], battle.Presses);
            Assert.Equal([EventState.BattleComplete], battle.State.State);
            Assert.Contains("[RsBattle] BattleConfirm (Yes) at Delete a move?", log);
            Assert.Contains("[RsBattle] forget-a-move screen closed", log);
            Assert.Contains("[RsBattle] party after the battle: slot 0  (259) Lv 16", log);
        }

        // A free move slot: the move is learned with text presses only, "… learned …!" at 19 and the text at 20.
        [Fact]
        public async Task Evolution_FreeSlot_TextOnly()
        {
            Battle battle = Train(Evolves(0,
                [Scene(14, 8), Scene(15, 0), Scene(19, 3), Scene(19, 8)],
                [Scene(19, 8), Scene(20, 10), Scene(20, 8)],
                [Scene(20, 8), Free]));

            Assert.Null(await battle.RunAsync());

            Assert.Equal([.. WonPresses, "SceneText14", "SceneText19", "SceneText20"], battle.Presses);
            Assert.Equal([EventState.BattleComplete], battle.State.State);
        }

        // Nothing is pressed while the scene animates, at its first text (data[0] 2) or while the window holds at 4 or 10:
        // the wait ends in the stale stop that names the scene.
        [Fact]
        public async Task Evolution_NoPressAtTheFirstTextTheAnimationOrTheHolds()
        {
            Battle battle = Train(Won([Message(), Turn, Scene(1, 0), Scene(2, 1), Scene(2, 2), Scene(2, 3), Scene(2, 6), Scene(5, 0),
                Scene(21, 4), Scene(21, 10)]));

            string? stop = await battle.RunAsync(stale: TimeSpan.FromMilliseconds(200));

            Assert.Contains(Scene(21, 10).ToString(), stop);
            Assert.Equal(WonPresses, battle.Presses);
        }

        // One switch try from the action menu, from the switch research (S0 to S5): the action cursor along path, A on
        // POKéMON, the party list opening (opening: by default the task under CB2_InitPartyMenu, then the fade-in), Down to the
        // slot, A, then SHIFT's stage: the battle's own turns and then next.
        private static List<GameSnapshot[]> SwitchTry(int slot, GameSnapshot next, int[]? path = null, GameSnapshot[]? opening = null)
        {
            int[] cursors = path ?? [0, 2];
            List<GameSnapshot[]> stages = [];
            for (int i = 1; i < cursors.Length; i++)
            {
                stages.Add([TMenu(cursors[i - 1]), TMenu(cursors[i])]);
            }
            stages.Add([TMenu(2), Turn, .. opening ?? [PartyInit(), Party(0, fade: 1)], Party(0)]);
            for (int cursor = 0; cursor < slot; cursor++)
            {
                stages.Add([Party(cursor), Party(cursor + 1)]);
            }
            stages.Add([Party(slot), Popup(slot)]);
            stages.Add([Popup(slot), Turn, Turn, next]);
            return stages;
        }

        // "Wild ... appeared!", the menu, one switch try that lands, then the battler fights with move 0 and wins; then the
        // stages given after "... fainted!", by default the free field.
        private static GameSnapshot[][] SwitchFight(int slot, int[]? path = null, GameSnapshot[]? opening = null, params GameSnapshot[][] after) =>
        [
            [Turn, Message()],
            [Message(), Turn, TMenu((path ?? [0])[0])],
            .. SwitchTry(slot, TMenu(0, battler: slot), path, opening),
            [TMenu(0, battler: slot), Turn, MoveList(0)],
            [MoveList(0), Turn, Message(BattleOutcome.Won)],
            .. (after.Length == 0 ? [[Message(BattleOutcome.Won), Turn, Free]] : after),
        ];

        // Train Switch to the 0-based slot, with the party up to it: slot 0 pid 0x60, the next ones 0x78, 0x90, …
        private static Battle Switch(GameSnapshot[][] stages, int slot)
        {
            byte[][] party = [.. Enumerable.Range(0, Math.Max(slot + 1, 2)).Select(i => Fighter(pid: (uint)(0x60 + 0x18 * i)))];
            Battle battle = Start(new PressedReader(stages), mode: BattleMode.Battle, party: party, trainType: TrainType.Switch, switchSlot: slot + 1);
            battle.MoveMetadata = Damaging;
            battle.PickMove = _ => 0;
            return battle;
        }

        private static readonly string[] SwitchToSlot1Presses = ["Advance", "Down", "ToPokemon", "PartyDown", "Slot1", "Shift"];

        [Fact]
        public async Task Switch_Clean_EachPressConfirmed_ThenTheBattlerFights()
        {
            Battle battle = Switch(SwitchFight(1), 1);

            string log = await CaptureAsync(async () => Assert.Null(await battle.RunAsync()));

            // Nothing between SHIFT and the next action menu.
            Assert.Equal([.. SwitchToSlot1Presses, "Fight", "Move0", "Advance"], battle.Presses);
            Assert.Equal([EventState.BattleComplete], battle.State.State);
            // The menu read before the switch and the one after it both take the battler's record.
            IReadOnlyList<ChainBlockRequest>[] menus = [.. battle.Requests.Where(r => r.Any(b => b.Anchor == EnemyParty))];
            Assert.Equal(2, menus.Length);
            Assert.All(menus, menu => Assert.Equal([(EnemyParty, 100), (PlayerParty + 100, 100)], menu.Select(b => (b.Anchor, b.Length))));
            Assert.Contains("[RsBattle] Down on the action menu 0 -> 2", log);
            Assert.Contains("confirmed by cursor 2", log);
            Assert.Contains("[RsBattle] BattleConfirm on POKéMON (switch try 1 of 3)", log);
            Assert.Contains("confirmed by the screen leaving the action menu", log);
            Assert.Contains("[RsBattle] switch try 1 of 3 to slot 1: the party list takes keys", log);
            Assert.Contains("[RsBattle] Down on the party list from 0", log);
            Assert.Contains("confirmed by the party cursor leaving 0", log);
            Assert.Contains("[RsBattle] BattleConfirm on party slot 1", log);
            Assert.Contains("confirmed by the SHIFT submenu", log);
            Assert.Contains("[RsBattle] BattleConfirm on SHIFT", log);
            Assert.Contains("confirmed by the submenu closing", log);
            Assert.Contains("[RsBattle] after SHIFT", log);
            Assert.Contains("[RsBattle] switch confirmed: 0x02024A6A reads 1 at the action menu, try 1", log);
        }

        // The task first reads under CB2_InitPartyMenu with the fade off, then the fade-in: no Down until the list takes keys.
        [Fact]
        public async Task Switch_NoDownBeforeTheListTakesKeys()
        {
            Battle battle = Switch(SwitchFight(1, opening: [PartyInit(), PartyInit(), PartyInit(), Party(0, fade: 1), Party(0, fade: 1), Party(0, fade: 1)]), 1);

            Assert.Null(await battle.RunAsync());

            Assert.Equal([.. SwitchToSlot1Presses, "Fight", "Move0", "Advance"], battle.Presses);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        public async Task Switch_DownsUntilTheSlot(int slot)
        {
            Battle battle = Switch(SwitchFight(slot), slot);

            string log = await CaptureAsync(async () => Assert.Null(await battle.RunAsync()));

            Assert.Equal(["Advance", "Down", "ToPokemon", .. Enumerable.Repeat("PartyDown", slot), $"Slot{slot}", "Shift", "Fight", "Move0", "Advance"],
                battle.Presses);
            for (int cursor = 0; cursor < slot; cursor++)
            {
                Assert.Contains($"confirmed by the party cursor leaving {cursor}", log);
            }
        }

        // The action cursor clears its bits first, as the move cursor does: 1 goes Left then Down, 3 goes Left.
        [Theory]
        [InlineData(new[] { 1, 0, 2 }, new[] { "Left", "Down" })]
        [InlineData(new[] { 3, 2 }, new[] { "Left" })]
        public async Task Switch_CursorNotOnFight_ReachesPokemon(int[] path, string[] directions)
        {
            Battle battle = Switch(SwitchFight(1, path), 1);

            Assert.Null(await battle.RunAsync());

            Assert.Equal(["Advance", .. directions, "ToPokemon", "PartyDown", "Slot1", "Shift", "Fight", "Move0", "Advance"], battle.Presses);
        }

        // Each try comes back to the menu with the trainee still out; the fourth menu runs.
        [Fact]
        public async Task Switch_ThreeFailedTries_Flees()
        {
            Battle battle = Switch(
            [
                [Turn, Message()],
                [Message(), Turn, TMenu(0)],
                .. SwitchTry(1, TMenu(0)),
                .. SwitchTry(1, TMenu(0)),
                .. SwitchTry(1, TMenu(0)),
                [TMenu(0), TMenu(3)],
                [TMenu(3), Turn, Message(BattleOutcome.Fled)],
                [Message(BattleOutcome.Fled), Turn, Free],
            ], 1);

            string log = await CaptureAsync(async () => Assert.Null(await battle.RunAsync()));

            string[] attempt = ["Down", "ToPokemon", "PartyDown", "Slot1", "Shift"];
            Assert.Equal(["Advance", .. attempt, .. attempt, .. attempt, "ToRun", "Confirm", "Advance"], battle.Presses);
            Assert.Equal([EventState.BattleComplete], battle.State.State);
            Assert.Contains("[RsBattle] switch try 3 did not land: 0x02024A6A reads 0", log);
            Assert.Contains("[RsBattle] fleeing: the switch to slot 1 failed 3 times", log);
            Assert.DoesNotContain("switch try 4", log);
        }

        // For about 22 frames after SHIFT the records are swapped (switch research point 2): from SHIFT to the next menu the
        // reader gives BattleTurn, and no record is read there.
        [Fact]
        public async Task Switch_NoPartyRecordReadBetweenShiftAndTheNextMenu()
        {
            GameSnapshot[][] stages = SwitchFight(1);
            stages[6] = [Popup(1), .. Enumerable.Repeat(Turn, 8), TMenu(0, battler: 1)];
            Battle battle = Switch(stages, 1);

            Assert.Null(await battle.RunAsync());

            Assert.Equal([.. SwitchToSlot1Presses, "Fight", "Move0", "Advance"], battle.Presses);
            Assert.NotEmpty(battle.RecordReadsAt);
            Assert.All(battle.RecordReadsAt, at => Assert.Contains(at.Screen, new[] { GameScreen.BattleMenu, GameScreen.FieldFree }));
        }

        // A refused SHIFT: the refusal's message reads as BattleTurn, unchanged, so the wait after SHIFT stops naming the switch.
        [Fact]
        public async Task Switch_RefusedShift_StaleStopNamesTheSwitch()
        {
            GameSnapshot[][] stages = SwitchFight(1);
            Battle battle = Switch([.. stages[..6], [Popup(1), Turn]], 1);

            string? stop = await battle.RunAsync(stale: TimeSpan.FromMilliseconds(200));

            Assert.Contains("the next action menu after SHIFT (the switch to slot 1)", stop);
            Assert.Contains(Turn.ToString(), stop);
            Assert.Equal(SwitchToSlot1Presses, battle.Presses);
            Assert.Equal([EventState.Waiting], battle.State.State);
        }

        // The battler's record at HP 0: the battle is fled with no try, and no Fainted stop.
        [Fact]
        public async Task Switch_BattlerFainted_FleesWithoutTrying()
        {
            Battle battle = Start(new PressedReader(TrainFleeStages()), mode: BattleMode.Battle, party: [Fighter(), Fighter(hp: 0, pid: 0x78)],
                trainType: TrainType.Switch, switchSlot: 2);

            string log = await CaptureAsync(async () => Assert.Null(await battle.RunAsync()));

            Assert.Equal(["Advance", "ToRun", "Confirm", "Advance"], battle.Presses);
            Assert.Equal([EventState.BattleComplete], battle.State.State);
            Assert.Contains("[RsBattle] fleeing: the battler in slot 1 has fainted", log);
        }

        // The orchestrator's heal trip: Switch with Flee mode runs today's flee, with today's reads.
        [Fact]
        public async Task Switch_FleeMode_DoesNotSwitch()
        {
            Battle flee = Start(Fled());
            Battle battle = Start(Fled(), trainType: TrainType.Switch, switchSlot: 2);

            Assert.Null(await flee.RunAsync());
            Assert.Null(await battle.RunAsync());

            Assert.Equal(["Advance", "ToRun", "Confirm", "Advance"], battle.Presses);
            Assert.Equal(flee.Requests.Select(r => r.Select(b => (b.Anchor, b.Length)).ToArray()),
                battle.Requests.Select(r => r.Select(b => (b.Anchor, b.Length)).ToArray()));
        }

        // Switch research S8: the benched trainee's boxes (getter 0) come first, then the battler's (getter 1).
        [Fact]
        public async Task Switch_TraineesBoxThenTheBattlers()
        {
            Battle battle = Switch(SwitchFight(1, after:
            [
                [Message(BattleOutcome.Won), Turn, Box(1, 0)], [Box(1, 0), Box(2, 0)], [Box(2, 0), Turn, Box(1, 1)], [Box(1, 1), Box(2, 1)],
                [Box(2, 1), Turn, Free],
            ]), 1);

            string log = await CaptureAsync(async () => Assert.Null(await battle.RunAsync()));

            Assert.Equal([.. SwitchToSlot1Presses, "Fight", "Move0", "Advance", "Box1", "Box2", "Box1", "Box2"], battle.Presses);
            int trainee = log.IndexOf("level-up box page 1 for slot 0", StringComparison.Ordinal);
            int battler = log.IndexOf("level-up box page 1 for slot 1", StringComparison.Ordinal);
            Assert.InRange(trainee, 0, battler - 1);
        }

        // The benched trainee's move prompt after a switch battle asks about slot 0, read beside gMoveToLearn.
        [Fact]
        public async Task Switch_BenchedTraineesPrompt_AsksAboutSlot0()
        {
            Battle battle = Switch(SwitchFight(1, after:
            [
                [Message(BattleOutcome.Won), Turn, Prompt(5, getter: 0)], [Prompt(5, 0), Turn, Prompt(10, 0)], [Prompt(10, 0), Turn, Free],
            ]), 1);
            List<uint> asked = [];
            battle.DecideMove = (p, _) => { asked.Add(p.PID); return Task.FromResult(-1); };

            Assert.Null(await battle.RunAsync());

            Assert.Equal([.. SwitchToSlot1Presses, "Fight", "Move0", "Advance", "No", "StopYes"], battle.Presses);
            Assert.Equal([0x60u], asked);
            IReadOnlyList<ChainBlockRequest> prompt = Assert.Single(battle.Requests, r => r.Any(b => b.Anchor == MoveToLearn));
            Assert.Equal([(MoveToLearn, 2), (PlayerParty, 100)], prompt.Select(b => (b.Anchor, b.Length)));
        }

        private sealed class LoggedReader(IGameStateReader inner, Action log) : IGameStateReader
        {
            public Task<GameSnapshot> ReadAsync(CancellationToken ct)
            {
                log();
                return inner.ReadAsync(ct);
            }
        }
    }
}
