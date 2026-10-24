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

        // evo.state, the forget-move list: the summary app runs (pointer-discovery.md, "The forget-move list (yes path)").
        private const byte ForgetMoveList = 23;

        // env.ctx[0..2] and ctx.mode (pointer-discovery.md, "Field scripts").
        private const int ScriptContexts = 3;
        private const int ContextPointerSize = 4;
        private const byte WaitingInNative = 2;

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
        private const int EvoBlock = 7;
        private const int EnvBlock = 8;
        private const int FirstScriptContextBlock = 9;
        private const int SummaryBlock = 12;
        private const int PrinterBlock = 13;

        // The one sTextPrinterTasks slot read; every message observed used it (pointer-discovery.md, "Text printer state").
        private const byte ReadPrinterSlot = 0;

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
                new(a.Anchor, [a.AppSlotsOffset, a.SubAppOffset, a.AppSetupOffset, a.EvolutionTaskOffset, StructStart], a.EvolutionPrinterOffset + 1),
                new(a.Anchor, [a.TaskManagerOffset, a.ScriptEnvironmentOffset, StructStart], a.ScriptIdOffset + 2),
                new(a.Anchor, [a.TaskManagerOffset, a.ScriptEnvironmentOffset, a.ScriptContextsOffset, StructStart], a.ScriptContextNativeOffset + 4),
                new(a.Anchor, [a.TaskManagerOffset, a.ScriptEnvironmentOffset, a.ScriptContextsOffset + ContextPointerSize, StructStart], a.ScriptContextNativeOffset + 4),
                new(a.Anchor, [a.TaskManagerOffset, a.ScriptEnvironmentOffset, a.ScriptContextsOffset + 2 * ContextPointerSize, StructStart], a.ScriptContextNativeOffset + 4),
                new(a.Anchor, [a.AppSlotsOffset, a.SubAppOffset, a.AppSetupOffset, a.EvolutionTaskOffset, a.EvolutionSummaryOffset, a.OverlayProcStateOffset], 4),
                new(a.TextPrinterTasks, [a.TextPrinterTaskDataOffset, a.TextPrinterStateOffset], 1),
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
                    if (!Ok(r[TaskBlock])) return Snapshot(GameScreen.Transitioning);
                    uint function = U32(r[TaskBlock].Bytes, addresses.TaskFunctionOffset);
                    // A child task on top of the script (the fade, the heal animation) carries only its function.
                    return function == addresses.RunScriptsTask
                        ? FieldScript(r, function)
                        : Snapshot(GameScreen.FieldTask) with { TaskFunction = function };
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
            if (state == EvolutionMain)
            {
                if (!Ok(r[EvoBlock])) return Snapshot(GameScreen.Transitioning);
                byte[] evo = r[EvoBlock].Bytes;
                if (!TryTextPrinter(r[PrinterBlock], evo[addresses.EvolutionPrinterOffset], out int? printer))
                    return Snapshot(GameScreen.Transitioning);
                byte evoState = evo[addresses.EvolutionStateOffset];
                GameSnapshot evolution = Snapshot(GameScreen.Evolution) with { Prompt = evoState, TextPrinter = printer };
                // The summary app runs only at 23; elsewhere evo.summary is NULL.
                if (evoState != ForgetMoveList) return evolution;
                return Ok(r[SummaryBlock])
                    ? evolution with { SummaryState = (int)U32(r[SummaryBlock].Bytes, 0) }
                    : Snapshot(GameScreen.Transitioning);
            }
            // 13 included: every battle end passes through the evolution check.
            if (state > BattleMain) return BattleOver(r[OutcomeBlock]);

            if (!Ok(r[CtxBlock])) return Snapshot(GameScreen.Transitioning);
            byte[] ctx = r[CtxBlock].Bytes;

            // Precedence from the state table: battle done, then the getter prompts, then a script, then the menu.
            // The getter goes before the script because the experience script 276 runs while the stat boxes and
            // move-learn prompts wait for input, and before the menu because the stat boxes appear while the
            // command is not 5.
            uint command = U32(ctx, addresses.ServerCommandOffset);
            if (command == BattleDone) return BattleOver(r[OutcomeBlock]);
            if (U32(ctx, addresses.GetterOffset) != 0)
            {
                if (!Ok(r[GetterBlock])) return Snapshot(GameScreen.Transitioning);
                uint getterState = U32(r[GetterBlock].Bytes, 0);
                if (getterState is StatBoxes or StatBoxesTotals)
                    return Snapshot(GameScreen.BattleExpPrompt) with { Prompt = (int)getterState };
                if (getterState is ForgetAMove or ChooseMoveToForget or GiveUpLearning)
                    return Snapshot(GameScreen.BattleMoveLearnPrompt) with { Prompt = (int)getterState };
            }
            if (command == ScriptRunning) return Snapshot(GameScreen.BattleScript) with { Script = (int)U32(ctx, addresses.ScriptFileOffset) };
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

        // Native comes from the innermost live context, the highest one whose pointer is set: the map's NPC script
        // waits in WaitStd in ctx 0 while the standard script it called (the nurse) works in ctx 1.
        private GameSnapshot FieldScript(ChainBlockResult[] r, uint function)
        {
            ChainBlockResult env = r[EnvBlock];
            if (!Ok(env) || U32(env.Bytes, addresses.ScriptEnvironmentCheckOffset) != addresses.ScriptEnvironmentMagic)
                return Snapshot(GameScreen.Transitioning);
            if (!TryTextPrinter(r[PrinterBlock], env.Bytes[addresses.ScriptPrinterOffset], out int? printer))
                return Snapshot(GameScreen.Transitioning);
            GameSnapshot script = Snapshot(GameScreen.FieldTask) with
            {
                TaskFunction = function,
                Script = BitConverter.ToUInt16(env.Bytes, addresses.ScriptIdOffset),
                TextPrinter = printer,
            };
            for (int i = ScriptContexts - 1; i >= 0; i--)
            {
                ChainBlockResult ctx = r[FirstScriptContextBlock + i];
                if (ctx.Status == ChainBlockStatus.NullPointer) continue;
                if (!Ok(ctx)) return Snapshot(GameScreen.Transitioning);
                return ctx.Bytes[addresses.ScriptContextModeOffset] == WaitingInNative
                    ? script with { Native = U32(ctx.Bytes, addresses.ScriptContextNativeOffset) }
                    : script;
            }
            return script;
        }

        // printer is pr.state of the screen's slot, null when that slot is not the one read or holds no printer; false when
        // the printer block failed otherwise (FR-3).
        private static bool TryTextPrinter(ChainBlockResult block, byte slot, out int? printer)
        {
            printer = null;
            if (slot != ReadPrinterSlot || block.Status == ChainBlockStatus.NullPointer) return true;
            if (!Ok(block)) return false;
            printer = block.Bytes[0];
            return true;
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
