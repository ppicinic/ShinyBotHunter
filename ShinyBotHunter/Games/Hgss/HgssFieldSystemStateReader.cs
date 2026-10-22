using ShinyBotHunter.Emulators;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Games.Hgss
{
    public class HgssFieldSystemStateReader : IGameStateReader
    {
        // app.state, battle_022378C0.c:25-42 (pointer-discovery.md, "Where the game is").
        private const uint BattleMain = 9;
        private const uint EvolutionMain = 14;

        // ctx.command, constants/battle.h:594-642 (pointer-discovery.md, "Server command").
        private const uint WaitingForChoice = 5;
        private const uint ScriptRunning = 22;
        private const uint BattleDone = 45;

        // ctx.sel, battle_controller_player.c:243-262 (pointer-discovery.md, "Player menu state").
        private const byte MainMenu = 1;
        private const byte MoveList = 4;
        private const byte MessageShown = 15;

        // getter.state, battle_command.c:5871-5911 (pointer-discovery.md, "Experience, level-up and move learning").
        private const uint StatBoxes = 11;
        private const uint StatBoxesTotals = 13;
        private const uint ForgetAMove = 21;
        private const uint ChooseMoveToForget = 23;
        private const uint GiveUpLearning = 34;

        // The trailing hop of a block that starts at the struct its last pointer names.
        private const int StructStart = 0;

        // Positions in blocks, as in the spec's block table ("The walk").
        private const int FsBlock = 0;
        private const int Sub0Block = 1;
        private const int TaskBlock = 2;
        private const int AppBlock = 3;
        private const int CtxBlock = 4;
        private const int GetterBlock = 5;
        private const int OutcomeBlock = 6;

        private readonly IMemoryClient memory;
        private readonly HgssPointerService addresses;
        private readonly ChainBlockRequest[] blocks;

        public HgssFieldSystemStateReader(IMemoryClient memory, HgssPointerService addresses)
        {
            this.memory = memory;
            this.addresses = addresses;
            HgssPointerService a = addresses;
            blocks =
            [
                new(a.Anchor, [StructStart], a.MapReadyOffset + 4),
                new(a.Anchor, [a.AppSlotsOffset, a.MapAppOffset], a.PausedOffset + 4),
                new(a.Anchor, [a.TaskManagerOffset, StructStart], a.TaskFunctionOffset + 4),
                new(a.Anchor, [a.AppSlotsOffset, a.SubAppOffset, StructStart], a.BattleSystemOffset + 4),
                new(a.Anchor, [a.AppSlotsOffset, a.SubAppOffset, a.BattleSystemOffset, a.BattleContextOffset, a.MenuStateOffset], a.GetterOffset + 4),
                new(a.Anchor, [a.AppSlotsOffset, a.SubAppOffset, a.BattleSystemOffset, a.BattleContextOffset, a.GetterOffset, a.GetterStateOffset], 4),
                new(a.Anchor, [a.AppSlotsOffset, a.SubAppOffset, a.BattleSystemOffset, a.OutcomeOffset], 1),
            ];
        }

        // One request, every block from one frame (FR-2); no retry count (NFR-3).
        public async Task<GameSnapshot> ReadAsync(CancellationToken ct)
            => Classify(await memory.ReadChainBlocksAsync(blocks, ct: ct));

        private GameSnapshot Classify(ChainBlockResult[] r)
        {
            if (!Ok(r[FsBlock]) || !Ok(r[Sub0Block])) return Snapshot(GameScreen.Transitioning);
            byte[] fs = r[FsBlock].Bytes;
            byte[] sub0 = r[Sub0Block].Bytes;

            if (U32(sub0, addresses.SubAppOffset) == 0)
            {
                uint taskman = U32(fs, addresses.TaskManagerOffset);
                bool mapReady = U32(fs, addresses.MapReadyOffset) == 1;
                if (mapReady && taskman != 0)
                {
                    return Ok(r[TaskBlock])
                        ? Snapshot(GameScreen.FieldTask) with { TaskFunction = U32(r[TaskBlock].Bytes, addresses.TaskFunctionOffset) }
                        : Snapshot(GameScreen.Transitioning);
                }
                return mapReady && taskman == 0 && U32(sub0, addresses.PausedOffset) == 0
                    ? Snapshot(GameScreen.FieldFree)
                    : Snapshot(GameScreen.Transitioning);
            }

            if (!Ok(r[AppBlock])) return Snapshot(GameScreen.Transitioning);
            byte[] app = r[AppBlock].Bytes;
            if (U32(app, addresses.AppOverlayIdOffset) != addresses.BattleOverlayId) return Snapshot(GameScreen.OtherApp);
            uint state = U32(app, addresses.AppStateOffset);
            if (state < BattleMain) return Snapshot(GameScreen.BattleStarting);
            if (state == EvolutionMain) return Snapshot(GameScreen.Evolution);
            // 13 included: every battle end passes through the evolution check.
            if (state > BattleMain) return BattleOver(r[OutcomeBlock]);

            if (!Ok(r[CtxBlock])) return Snapshot(GameScreen.Transitioning);
            byte[] ctx = r[CtxBlock].Bytes;

            // Precedence from the state table: battle done, then a script, then the getter prompts
            // (the stat boxes appear while the command is not 5), then the menu.
            uint command = U32(ctx, addresses.ServerCommandOffset);
            if (command == BattleDone) return BattleOver(r[OutcomeBlock]);
            if (command == ScriptRunning) return Snapshot(GameScreen.BattleScript) with { Script = (int)U32(ctx, addresses.ScriptFileOffset) };
            if (U32(ctx, addresses.GetterOffset) != 0)
            {
                if (!Ok(r[GetterBlock])) return Snapshot(GameScreen.Transitioning);
                uint getterState = U32(r[GetterBlock].Bytes, 0);
                if (getterState is StatBoxes or StatBoxesTotals)
                    return Snapshot(GameScreen.BattleExpPrompt) with { Prompt = (int)getterState };
                if (getterState is ForgetAMove or ChooseMoveToForget or GiveUpLearning)
                    return Snapshot(GameScreen.BattleMoveLearnPrompt) with { Prompt = (int)getterState };
            }
            if (command == WaitingForChoice)
            {
                switch (ctx[addresses.MenuStateOffset])
                {
                    case MainMenu: return Snapshot(GameScreen.BattleMenu);
                    case MoveList: return Snapshot(GameScreen.BattleMoveList);
                    case MessageShown: return Snapshot(GameScreen.BattleMessage);
                }
            }
            return Snapshot(GameScreen.BattleTurn);
        }

        // Outcome is null when its block failed or the byte is outside 1 to 5 (the 0x40 and 0x80
        // flee-in-progress bits included).
        private static GameSnapshot BattleOver(ChainBlockResult outcome)
        {
            BattleOutcome? value = null;
            if (Ok(outcome) && outcome.Bytes[0] is >= (byte)BattleOutcome.Won and <= (byte)BattleOutcome.Fled)
            {
                value = (BattleOutcome)outcome.Bytes[0];
            }
            return Snapshot(GameScreen.BattleOver) with { Outcome = value };
        }

        private static bool Ok(ChainBlockResult block) => block.Status == ChainBlockStatus.Ok;

        private static uint U32(byte[] bytes, int offset) => BitConverter.ToUInt32(bytes, offset);

        private static GameSnapshot Snapshot(GameScreen screen) => new(screen, null, null, null, null);
    }
}
