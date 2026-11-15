using ShinyBotHunter.Emulators;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Games.Rs
{
    /// <summary>
    /// Classifies the RS screen from seven direct reads made on one frame (docs/specs/rs-wild-encounter.md FR-7,
    /// docs/research/rs-wild-encounter.md "What memory shows through one cycle").
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

        private readonly IMemoryClient memory;
        private readonly RsPointerService addresses;
        private readonly ChainBlockRequest[] blocks;

        public RsStateReader(IMemoryClient memory, RsPointerService addresses)
        {
            this.memory = memory;
            this.addresses = addresses;
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
        }

        public async Task<GameSnapshot> ReadAsync(CancellationToken ct)
        {
            ChainBlockResult[] read = await memory.ReadChainBlocksAsync(blocks, ct: ct);

            if (BitConverter.ToUInt32(read[Callback1Block].Bytes, 0) == 0)
            {
                return new GameSnapshot(GameScreen.Transitioning, null, null, null, null);
            }
            if ((read[InBattleBlock].Bytes[0] & InBattleBit) == 0)
            {
                GameScreen field = read[LockBlock].Bytes[0] == 0 ? GameScreen.FieldFree : GameScreen.FieldTask;
                return new GameSnapshot(field, null, null, null, null);
            }

            BattleOutcome? outcome = Map(read[OutcomeBlock].Bytes[0]);
            uint controller = BitConverter.ToUInt32(read[ControllerBlock].Bytes, 0);
            int window = BitConverter.ToUInt16(read[WindowBlock].Bytes, 0);
            if (controller == addresses.HandleInputChooseAction)
            {
                return new GameSnapshot(GameScreen.BattleMenu, outcome, null, read[CursorBlock].Bytes[0], null);
            }
            if (controller == addresses.HandleInputChooseMove)
            {
                return new GameSnapshot(GameScreen.BattleMoveList, outcome, null, null, null);
            }
            if (ButtonWaits.Contains(window))
            {
                return new GameSnapshot(GameScreen.BattleMessage, outcome, null, null, null, TextPrinter: window);
            }
            return new GameSnapshot(GameScreen.BattleTurn, outcome, null, null, null);
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
