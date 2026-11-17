using ShinyBotHunter.Emulators;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Games.Rs
{
    /// <summary>
    /// Classifies the RS screen from seven direct reads made on one frame (docs/specs/rs-wild-encounter.md FR-7,
    /// docs/research/rs-wild-encounter.md "What memory shows through one cycle"). Built with trainingReads it reads
    /// fifteen blocks on one frame, plus one sprite on the party screen, and also classifies the training and field
    /// script screens (docs/specs/rs-navigation-and-training.md); the flee path's reader is built without it.
    /// </summary>
    public class RsStateReader : IGameStateReader
    {
        // gMain.inBattle is bit 1 of its byte: pokeruby struct Main has oamLoadDisabled:1 then inBattle:1.
        private const byte InBattleBit = 0x02;

        // The battle window's states that wait for a button (research doc, "Memory read by the spike"); 5 and 9 decomp only.
        private static readonly int[] ButtonWaits = [5, 8, 9];

        // Positions in blocks.
        private const int LockBlock = 0;
        private const int Callback1Block = 1;
        private const int InBattleBlock = 2;
        private const int ControllerBlock = 3;
        private const int WindowBlock = 4;
        private const int OutcomeBlock = 5;
        private const int CursorBlock = 6;
        private const int Callback2Block = 7;
        private const int BattleStructBlock = 8;
        private const int MoveCursorBlock = 9;
        private const int ForgetCursorBlock = 10;
        private const int PartyIndexBlock = 11;
        private const int FieldWindowBlock = 12;
        private const int NativeBlock = 13;
        private const int TasksBlock = 14;

        // struct Task (include/task.h): func u32 at + 0, isActive u8 at + 4, s16 data[16] at + 8; 16 tasks of 0x28.
        // Struct offsets, not addresses; decomp only.
        private const int TaskSize = 0x28;
        private const int TaskCount = 16;
        private const int TaskIsActive = 4;
        private const int TaskData = 8;

        // struct Sprite: 0x44 bytes, s16 data[0] at + 0x2E (include/sprite.h:207). Struct offsets; decomp only.
        private const int SpriteSize = 0x44;
        private const int SpriteData = 0x2E;

        // The evolution scene's move prompt: data[0] 21 and data[8] 4, with the question in data[9] (research R134).
        private const int EvolutionMovePromptState = 21;
        private const int EvolutionMovePromptStep = 4;

        // atk5A_StateTracker on the forget-a-move screen and at "Delete a move?" (research R130, R131).
        private const int ForgetMoveScreen = 3;
        private const int DeleteMovePrompt = 1;

        private readonly IMemoryClient memory;
        private readonly RsPointerService addresses;
        private readonly bool trainingReads;
        private readonly ChainBlockRequest[] blocks;

        // One block from ExpGetterMonId to LevelUpBoxState inclusive (0x85 bytes); these are the trackers' places in it.
        private readonly int moveLearnPromptOffset;
        private readonly int levelUpBoxOffset;

        public RsStateReader(IMemoryClient memory, RsPointerService addresses, bool trainingReads = false)
        {
            this.memory = memory;
            this.addresses = addresses;
            this.trainingReads = trainingReads;
            moveLearnPromptOffset = (int)(addresses.MoveLearnPromptState - addresses.ExpGetterMonId);
            levelUpBoxOffset = (int)(addresses.LevelUpBoxState - addresses.ExpGetterMonId);
            int[] direct = [];
            blocks =
            [
                new(addresses.FieldControlsLock, direct, 1),
                new(addresses.Callback1, direct, 4),
                new(addresses.InBattle, direct, 1),
                new(addresses.PlayerControllerFunction, direct, 4),
                new(addresses.BattleWindowState, direct, 2),
                new(addresses.BattleOutcome, direct, 1),
                new(addresses.ActionSelectionCursor, direct, 1),
            ];
            if (trainingReads)
            {
                blocks =
                [
                    .. blocks,
                    new(addresses.Callback2, direct, 4),
                    new(addresses.ExpGetterMonId, direct, levelUpBoxOffset + 1),
                    new(addresses.MoveSelectionCursor, direct, 1),
                    new(addresses.ForgetMoveCursor, direct, 1),
                    new(addresses.ActiveBattlerPartyIndex, direct, 2),
                    new(addresses.FieldMessageWindowState, direct, 2),
                    new(addresses.ScriptNative, direct, 4),
                    new(addresses.Tasks, direct, TaskSize * TaskCount),
                ];
            }
        }

        public async Task<GameSnapshot> ReadAsync(CancellationToken ct)
        {
            ChainBlockResult[] read = await memory.ReadChainBlocksAsync(blocks, ct: ct);

            if (BitConverter.ToUInt32(read[Callback1Block].Bytes, 0) == 0)
            {
                return new GameSnapshot(GameScreen.Transitioning, null, null, null, null);
            }

            byte[] tasks = trainingReads ? read[TasksBlock].Bytes : [];
            if (FindTask(tasks, addresses.TaskEvolutionScene) is int evolution)
            {
                int state = TaskDatum(tasks, evolution, 0);
                int? question = state == EvolutionMovePromptState && TaskDatum(tasks, evolution, 8) == EvolutionMovePromptStep
                    ? TaskDatum(tasks, evolution, 9)
                    : null;
                return new GameSnapshot(GameScreen.Evolution, null, null, state, null, Question: question);
            }
            if ((FindTask(tasks, addresses.BattlePartyMenuTask) ?? FindTask(tasks, addresses.BattlePartyPopupTask)) is int party)
            {
                return new GameSnapshot(GameScreen.OtherApp, null, null, null, BitConverter.ToUInt32(tasks, party),
                    PartyMonIndex: await ReadPartyCursorAsync(tasks, party, ct));
            }

            if ((read[InBattleBlock].Bytes[0] & InBattleBit) == 0)
            {
                if (read[LockBlock].Bytes[0] == 0)
                {
                    return new GameSnapshot(GameScreen.FieldFree, null, null, null, null);
                }
                if (!trainingReads)
                {
                    return new GameSnapshot(GameScreen.FieldTask, null, null, null, null);
                }
                uint? yesNo = FindTask(tasks, addresses.TaskHandleYesNoInput) is null ? null : addresses.TaskHandleYesNoInput;
                return new GameSnapshot(GameScreen.FieldTask, null, null, null, yesNo,
                    Native: BitConverter.ToUInt32(read[NativeBlock].Bytes, 0),
                    TextPrinter: BitConverter.ToUInt16(read[FieldWindowBlock].Bytes, 0));
            }

            BattleOutcome? outcome = Map(read[OutcomeBlock].Bytes[0]);
            uint controller = BitConverter.ToUInt32(read[ControllerBlock].Bytes, 0);
            int window = BitConverter.ToUInt16(read[WindowBlock].Bytes, 0);
            byte[] battleStruct = trainingReads ? read[BattleStructBlock].Bytes : [];
            int? moveLearn = trainingReads ? battleStruct[moveLearnPromptOffset] : null;
            if (trainingReads && moveLearn == ForgetMoveScreen
                && BitConverter.ToUInt32(read[Callback2Block].Bytes, 0) != addresses.BattleMainCallback2)
            {
                return new GameSnapshot(GameScreen.OtherApp, outcome, null, read[ForgetCursorBlock].Bytes[0], null);
            }
            if (controller == addresses.HandleInputChooseAction)
            {
                int? slot = trainingReads ? BitConverter.ToUInt16(read[PartyIndexBlock].Bytes, 0) : null;
                return new GameSnapshot(GameScreen.BattleMenu, outcome, null, read[CursorBlock].Bytes[0], null, PartyMonIndex: slot);
            }
            if (controller == addresses.HandleInputChooseMove)
            {
                int? moveCursor = trainingReads ? read[MoveCursorBlock].Bytes[0] : null;
                return new GameSnapshot(GameScreen.BattleMoveList, outcome, null, moveCursor, null);
            }
            if (trainingReads && battleStruct[levelUpBoxOffset] is 1 or 2)
            {
                return new GameSnapshot(GameScreen.BattleExpPrompt, outcome, null, battleStruct[levelUpBoxOffset], null,
                    PartyMonIndex: battleStruct[0]);
            }
            if (moveLearn == DeleteMovePrompt)
            {
                return new GameSnapshot(GameScreen.BattleMoveLearnPrompt, outcome, null, DeleteMovePrompt, null,
                    PartyMonIndex: battleStruct[0]);
            }
            if (ButtonWaits.Contains(window))
            {
                return new GameSnapshot(GameScreen.BattleMessage, outcome, null, null, null, TextPrinter: window);
            }
            return new GameSnapshot(GameScreen.BattleTurn, outcome, null, null, null);
        }

        // The offset in the task list of the first active task running function, or null. An empty list (the flee
        // reader's) has none.
        private static int? FindTask(byte[] tasks, uint function)
        {
            for (int at = 0; at < tasks.Length; at += TaskSize)
            {
                if (tasks[at + TaskIsActive] != 0 && BitConverter.ToUInt32(tasks, at) == function)
                {
                    return at;
                }
            }
            return null;
        }

        private static short TaskDatum(byte[] tasks, int task, int index) => BitConverter.ToInt16(tasks, task + TaskData + index * 2);

        // The party cursor is data[0] of the sprite the party task's data[3] names in its high byte (research R142); one
        // more request, on the party screen only.
        private async Task<int> ReadPartyCursorAsync(byte[] tasks, int party, CancellationToken ct)
        {
            int sprite = (ushort)TaskDatum(tasks, party, 3) >> 8;
            uint address = (uint)(addresses.Sprites + sprite * SpriteSize + SpriteData);
            ChainBlockResult[] read = await memory.ReadChainBlocksAsync([new(address, [], 2)], ct: ct);
            return BitConverter.ToInt16(read[0].Bytes, 0);
        }

        // gBattleOutcome to the shared enum; 0 (during the battle), 5 (teleported) and 6 (the wild Pokémon fled) have no
        // shared value.
        private static BattleOutcome? Map(byte outcome) => outcome switch
        {
            1 => BattleOutcome.Won,
            2 => BattleOutcome.Lost,
            3 => BattleOutcome.Draw,
            4 => BattleOutcome.Fled,
            7 => BattleOutcome.Caught,
            _ => null,
        };
    }
}
