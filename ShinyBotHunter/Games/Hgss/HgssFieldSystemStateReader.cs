using ShinyBotHunter.Emulators;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Games.Hgss
{
    public class HgssFieldSystemStateReader : IGameStateReader
    {
        // DS main RAM (hgss-state-reader.md FR-3); a pointer outside it is a snapshot taken mid-transition.
        private const uint MainRamStart = 0x02000000;
        private const uint MainRamEnd = 0x02400000;

        // app.state, battle_022378C0.c:25-42 (pointer-discovery.md, "Where the game is").
        private const uint BattleMain = 9;
        private const uint EvolutionStart = 13;
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

        private const int StrikesBeforeRootReread = 3;

        private readonly IMemoryClient memory;
        private readonly HgssPointerService addresses;

        // Held for the whole walk; protects roots and transitioningInARow. Once the profile selects
        // this reader, WaitForMoveEnd's PollBattleEnded and PollMenuUp call ReadAsync concurrently.
        private readonly SemaphoreSlim walk = new(1, 1);
        private (uint FieldSystem, uint AppSlots)? roots;
        private int transitioningInARow;

        public HgssFieldSystemStateReader(IMemoryClient memory, HgssPointerService addresses)
        {
            this.memory = memory;
            this.addresses = addresses;
        }

        public async Task<GameSnapshot> ReadAsync(CancellationToken ct)
        {
            await walk.WaitAsync(ct);
            try
            {
                GameSnapshot snapshot = await WalkAsync(ct);
                if (snapshot.Screen != GameScreen.Transitioning)
                {
                    transitioningInARow = 0;
                }
                else if (++transitioningInARow == StrikesBeforeRootReread)
                {
                    // The only recovery path: the next snapshot reads both roots again.
                    roots = null;
                    transitioningInARow = 0;
                }
                return snapshot;
            }
            finally
            {
                walk.Release();
            }
        }

        private async Task<GameSnapshot> WalkAsync(CancellationToken ct)
        {
            if (roots is null)
            {
                uint fieldSystem = await ReadU32Async(addresses.Anchor, ct);
                if (!InMainRam(fieldSystem)) return Snapshot(GameScreen.Transitioning);
                uint appSlots = await ReadU32Async(Add(fieldSystem, addresses.AppSlotsOffset), ct);
                if (!InMainRam(appSlots)) return Snapshot(GameScreen.Transitioning);
                roots = (fieldSystem, appSlots);
            }
            (uint fs, uint sub0) = roots.Value;

            byte[] slots = await memory.ReadBytesAsync(sub0, addresses.PausedOffset + 4, ct);
            uint app = U32(slots, addresses.SubAppOffset);
            GameSnapshot snapshot = app == 0
                ? await FieldAsync(fs, U32(slots, addresses.PausedOffset), ct)
                : await AppAsync(app, ct);
            if (snapshot.Screen == GameScreen.Transitioning) return snapshot;

            // FR-2: the app slots moved while the walk ran, so the fields above may mix two screens.
            byte[] again = await memory.ReadBytesAsync(sub0, slots.Length, ct);
            return again.AsSpan().SequenceEqual(slots) ? snapshot : Snapshot(GameScreen.Transitioning);
        }

        private async Task<GameSnapshot> FieldAsync(uint fs, uint paused, CancellationToken ct)
        {
            byte[] block = await memory.ReadBytesAsync(fs, addresses.MapReadyOffset + 4, ct);
            uint taskman = U32(block, addresses.TaskManagerOffset);
            bool mapReady = U32(block, addresses.MapReadyOffset) == 1;
            if (mapReady && taskman != 0)
            {
                if (!InMainRam(taskman)) return Snapshot(GameScreen.Transitioning);
                byte[] task = await memory.ReadBytesAsync(taskman, addresses.TaskFunctionOffset + 4, ct);
                return Snapshot(GameScreen.FieldTask) with { TaskFunction = U32(task, addresses.TaskFunctionOffset) };
            }
            return mapReady && taskman == 0 && paused == 0
                ? Snapshot(GameScreen.FieldFree)
                : Snapshot(GameScreen.Transitioning);
        }

        private async Task<GameSnapshot> AppAsync(uint app, CancellationToken ct)
        {
            if (!InMainRam(app)) return Snapshot(GameScreen.Transitioning);
            byte[] block = await memory.ReadBytesAsync(app, addresses.BattleSystemOffset + 4, ct);
            if (U32(block, addresses.AppOverlayIdOffset) != addresses.BattleOverlayId) return Snapshot(GameScreen.OtherApp);
            uint state = U32(block, addresses.AppStateOffset);
            uint bs = U32(block, addresses.BattleSystemOffset);
            if (state < BattleMain) return Snapshot(GameScreen.BattleStarting);
            if (state is EvolutionStart or EvolutionMain) return Snapshot(GameScreen.Evolution);
            if (state > BattleMain) return await BattleOverAsync(bs, ct);

            if (!InMainRam(bs)) return Snapshot(GameScreen.Transitioning);
            uint ctxPointer = await ReadU32Async(Add(bs, addresses.BattleContextOffset), ct);
            if (!InMainRam(ctxPointer)) return Snapshot(GameScreen.Transitioning);
            byte[] ctx = await memory.ReadBytesAsync(ctxPointer, addresses.GetterOffset + 4, ct);

            // Precedence from the state table: battle done, then a script, then the getter prompts
            // (the stat boxes appear while the command is not 5), then the menu.
            uint command = U32(ctx, addresses.ServerCommandOffset);
            if (command == BattleDone) return await BattleOverAsync(bs, ct);
            if (command == ScriptRunning) return Snapshot(GameScreen.BattleScript) with { Script = (int)U32(ctx, addresses.ScriptFileOffset) };
            uint getter = U32(ctx, addresses.GetterOffset);
            if (getter != 0)
            {
                if (!InMainRam(getter)) return Snapshot(GameScreen.Transitioning);
                uint getterState = await ReadU32Async(Add(getter, addresses.GetterStateOffset), ct);
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

        // The outcome byte is read only while BS is in range; past that the snapshot is still BattleOver.
        private async Task<GameSnapshot> BattleOverAsync(uint bs, CancellationToken ct)
        {
            BattleOutcome? outcome = null;
            if (InMainRam(bs))
            {
                byte value = (await memory.ReadBytesAsync(Add(bs, addresses.OutcomeOffset), 1, ct))[0];
                // 1 to 5 only; the 0x40 and 0x80 flee-in-progress bits make it null.
                if (value is >= (byte)BattleOutcome.Won and <= (byte)BattleOutcome.Fled) outcome = (BattleOutcome)value;
            }
            return Snapshot(GameScreen.BattleOver) with { Outcome = outcome };
        }

        private async Task<uint> ReadU32Async(uint address, CancellationToken ct)
            => U32(await memory.ReadBytesAsync(address, 4, ct), 0);

        private static bool InMainRam(uint pointer) => pointer >= MainRamStart && pointer < MainRamEnd;

        private static uint Add(uint pointer, int offset) => (uint)(pointer + offset);

        private static uint U32(byte[] bytes, int offset) => BitConverter.ToUInt32(bytes, offset);

        private static GameSnapshot Snapshot(GameScreen screen) => new(screen, null, null, null, null);
    }
}
