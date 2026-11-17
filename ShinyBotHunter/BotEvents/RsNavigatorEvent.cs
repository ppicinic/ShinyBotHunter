using System.Diagnostics;
using System.Numerics;
using ShinyBotHunter.Bot;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Util;
using Route = ShinyBotHunter.Pathfinding.Route;

namespace ShinyBotHunter.BotEvents
{
    // The Ruby and Sapphire navigator (docs/specs/rs-navigation-and-training.md FR-9, FR-11, FR-19's [Navigator] lines, the
    // navigator side of FR-10): GoToPokemonCenter and ReturnToStart plan with RouteSearch from where the player stands and
    // walk the route segment by segment; HealAtPokemonCenter talks to the nurse by rule and checks the party's HP. Every step
    // of NavigatorEvent's states is HGSS's, so RS has its own event beside it, as RsBattleEvent sits beside
    // BattleOrchestratorEvent. The walk sends the segments' d-pad and B holds only, never a press of A or B. Readings:
    // docs/research/rs-navigation-and-training.md, "The navigator: the walk and the nurse, read frame by frame".
    public class RsNavigatorEvent : AbstractBotEvent<GameState>
    {
        private readonly PokemonStateReader pokemonStateReader;

        public RsNavigatorEvent(PokemonStateReader pokemonStateReader)
        {
            this.pokemonStateReader = pokemonStateReader;
        }

        // FR-9: a segment not ended this long after its macro was sent stops the walk; init so a test can shorten it.
        internal TimeSpan SegmentStale { get; init; } = TimeSpan.FromSeconds(20);

        // FR-11: a nurse read unchanged but for its frame this long stops; the heal holds it for 161 frames with one
        // Pokémon, longer with more. Init so a test can shorten it.
        internal TimeSpan NurseStale { get; init; } = TimeSpan.FromSeconds(10);

        // FR-11: the tile two below the nurse, the same in every Pokémon Center 1F by the data (read in Oldale's only).
        private const int CounterX = 7;
        private const int CounterY = 4;
        private const string CenterSuffix = "_PokemonCenter_1F";

        // gMain.inBattle is bit 1 of its byte (RsStateReader).
        private const byte InBattleBit = 0x02;

        // A tile at elevation 15 takes any elevation; the search starts a player on one at 0 (N2 review on #429).
        private const byte AnyElevation = 15;

        // The nurse's gates (research "The nurse", N1 to N6). The facing is the low nibble of PlayerFacing, 2 up. The field
        // message window waits for a button at 8 (5 and 9 decomp only at the nurse). The Yes/No task takes A once its
        // data[2] has counted up to 5; an A at 1 was dropped on both games. The script's own button wait is status 0
        // (running) with mode 2 (a native polled); status 2 is no script.
        private const int FacingUp = 2;
        private static readonly int[] FieldButtonWaits = [5, 8, 9];
        private const int YesNoReady = 5;
        private const byte ScriptRunning = 0;
        private const byte NoScript = 2;
        private const byte NativeMode = 2;

        // struct Task (include/task.h): func u32 at + 0, isActive u8 at + 4, s16 data[16] at + 8, so data[2] at + 0x0C;
        // 16 tasks of 0x28. Struct offsets, not addresses; decomp only.
        private const int TaskSize = 0x28;
        private const int TaskCount = 16;
        private const int TaskIsActive = 4;
        private const int TaskYesNoCounter = 0x0C;

        // One walk read: seven blocks on one frame, classified in RsStateReader's order. Callback 1 at 0 is a transition and
        // never free: after every warp it reads 0 with the lock 0 for 4 to 7 frames. Then the battle bit, then the lock.
        // The running state and the tile transition, gPlayerAvatar + 2 and + 3 in one block: at rest both read 0; through a
        // step 2 and 1, on its last frame 0 and 2; on the first frame of a step from rest 2 and 0 (W9).
        private readonly record struct WalkRead(byte Lock, uint Callback1, bool InBattle, uint Frame, int Map, int X, int Y, byte Running, byte TileTransition)
        {
            public bool Battle => Callback1 != 0 && InBattle;

            public bool Locked => Callback1 != 0 && !InBattle && Lock != 0;

            public bool Free => Callback1 != 0 && !InBattle && Lock == 0;

            public bool On(int map, int x, int y) => Map == map && X == x && Y == y;

            public override string ToString() =>
                $"(f {Frame}: map {Map >> 8}.{Map & 0xFF} ({X}, {Y}), lock {Lock}, callback 1 0x{Callback1:X8}, battle bit {(InBattle ? 1 : 0)}, running state {Running}, tile transition {TileTransition})";
        }

        // One nurse read: ten blocks on one frame. YesNo is the Yes/No task's data[2], null when that task is not active.
        private readonly record struct NurseRead(byte Lock, uint Callback1, ushort Window, byte Status, byte Mode, uint Native,
            short? YesNo, uint Frame, byte Facing, byte TileTransition)
        {
            // Free and at rest: tile transition 0 is also read through a turn in place, which is why the talk's gate adds
            // the facing (W8, W9).
            public bool AtRest => Callback1 != 0 && Lock == 0 && TileTransition == 0;

            public bool FacesUp => (Facing & 0x0F) == FacingUp;

            public bool AtPageBreak => Lock != 0 && FieldButtonWaits.Contains(Window);

            public bool SameAs(NurseRead other) => this with { Frame = 0 } == other with { Frame = 0 };

            public override string ToString() =>
                $"(f {Frame}: lock {Lock}, callback 1 0x{Callback1:X8}, window {Window}, status {Status}, mode {Mode}, native 0x{Native:X8}, "
                + $"yes/no {(YesNo is { } counter ? counter.ToString() : "none")}, facing 0x{Facing:X2}, tile transition {TileTransition})";
        }

        public override async Task<GameState> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            await RunAsync(state, cancellationToken);
            return state;
        }

        // The state on top. Returns the sentence it stopped with (Waiting), otherwise null.
        internal Task<string?> RunAsync(GameState state, CancellationToken ct) => state.State.Peek() switch
        {
            EventState.GoToPokemonCenter => WalkToAsync(state, true, ct),
            EventState.ReturnToStart => WalkToAsync(state, false, ct),
            EventState.HealAtPokemonCenter => HealAsync(state, ct),
            EventState other => throw new InvalidOperationException($"RsNavigatorEvent does not serve {other}."),
        };

        // GoToPokemonCenter or ReturnToStart, from where the player stands. Null when arrived (HealAtPokemonCenter, or
        // NavigatorEvent.FinishReturn), or when a battle or a lock was handed to the battle event with BattleStart pushed over
        // the state, which plans afresh when it is on top again.
        private async Task<string?> WalkToAsync(GameState state, bool toCenter, CancellationToken ct)
        {
            IBot bot = state.Bot;
            RsPointerService addresses = (RsPointerService)bot.Addresses;
            MapService maps = bot.Profile.Maps!;

            try
            {
                await Overworld.WaitForReadyAsync(bot.State, ct);
            }
            catch (GameStateTimeoutException ex)
            {
                return Stop(state, ex.Message, null);
            }

            // FR-9: the flags and variables once per plan; nothing about a route is kept between entries.
            WalkRead at = await ReadAsync(bot, addresses, ct);
            Stopwatch watch = Stopwatch.StartNew();
            (byte[] flags, byte[] vars) = await ((RsOverworldReader)bot.Overworld).ReadScriptStateAsync(ct);
            long scriptMs = watch.ElapsedMilliseconds;
            byte tileElevation = maps.GetMap(at.Map, true).ElevationAt(at.Y, at.X);
            int elevation = tileElevation == AnyElevation ? 0 : tileElevation;
            Func<int, int, int, bool> isGoal = toCenter
                ? (map, x, y) => x == CounterX && y == CounterY && maps.GetMapLookup(map).InternalName.EndsWith(CenterSuffix)
                : (map, x, y) => map == state.StartMapId && x == state.StartX && y == state.StartY;
            watch.Restart();
            Route? route = new RouteSearch(maps, addresses, GameFlavor.RS)
                .Find(at.Map, at.X, at.Y, elevation, isGoal, flags, vars, new Capability());
            long searchMs = watch.ElapsedMilliseconds;
            if (route is null)
            {
                return Stop(state, toCenter
                    ? $"No route to a Pokémon Center on foot from {Tile(maps, at.Map, at.X, at.Y)}."
                    : $"No route back to the start tile {Tile(maps, state.StartMapId, state.StartX, state.StartY)} from {Tile(maps, at.Map, at.X, at.Y)}.", at);
            }

            (int Map, int X, int Y) goal = route.Steps.Count == 0 ? (at.Map, at.X, at.Y) : (route.Steps[^1].Map, route.Steps[^1].X, route.Steps[^1].Y);
            string crossed = route.LiveTriggers.Count == 0 ? "none" : string.Join(", ", route.LiveTriggers.Select(t => Tile(maps, t.Map, t.X, t.Y)));
            Console.WriteLine($"[Navigator] plan to {Tile(maps, goal.Map, goal.X, goal.Y)} from {Tile(maps, at.Map, at.X, at.Y)} elevation {elevation}: "
                + $"cost {route.Cost}, {route.Steps.Count} steps, {route.Segments.Count} segments, {flags.Sum(b => BitOperations.PopCount(b))} flags set, "
                + $"live triggers crossed: {crossed}; script state {scriptMs} ms, search {searchMs} ms");

            (bool arrived, string? stopped) = route.Segments.Count == 0 ? (true, null) : await WalkAsync(state, maps, addresses, route, at, ct);
            if (!arrived)
            {
                return stopped;
            }
            if (toCenter)
            {
                state.SwapState(EventState.HealAtPokemonCenter);
            }
            else
            {
                NavigatorEvent.FinishReturn(state, route.Segments.Count == 0 ? "already at start" : "arrived back at start");
            }
            return null;
        }

        // FR-9 per segment: send its macro with no clear before it, then poll the walk read until the macro has ended and the
        // field is free on the segment's map and tile, which for a segment cut at a door's coordinate is the landing (R3).
        // The tile is read 8 or 16 frames before its step ends (W9): the next segment's macro takes that step over cleanly,
        // but after the last one the field goes to the seek or the nurse, and a direction held as a step ends is a walk, so
        // the last segment ends only once the running state and the tile transition both read 0 (#467). The
        // battle bit, or a lock with the map and tile unchanged for the profile's SeekLockHandOff, clears the macro and pushes
        // BattleStart: a lock's longest unchanged run was 70 frames at a door and 93 before a wild battle's bit, while a
        // trainer's never ends, and the battle event reads the trainer (N5). Arrived, or not with the stop sentence, or with
        // null when BattleStart was pushed.
        private async Task<(bool Arrived, string? Stopped)> WalkAsync(GameState state, MapService maps, RsPointerService addresses, Route route, WalkRead at, CancellationToken ct)
        {
            IBot bot = state.Bot;
            TimeSpan handOff = bot.Profile.SeekLockHandOff!.Value;
            uint walkStart = at.Frame;
            int count = route.Segments.Count;
            Task macroEnded = Task.CompletedTask;
            for (int i = 0; i < count; i++)
            {
                (string macro, int map, int x, int y) = route.Segments[i];
                bool last = i == count - 1;
                string segment = $"[Navigator] segment {i + 1}/{count}:";
                Console.WriteLine($"{segment} {macro} to {Tile(maps, map, x, y)}");
                bot.RunMacroString(macro);
                macroEnded = bot.WaitForMacroAsync(ct);
                uint sent = at.Frame;
                Stopwatch sinceSent = Stopwatch.StartNew();
                // The first read of the current locked run with the map and tile unchanged, and how long it has lasted.
                WalkRead? lockedFrom = null;
                Stopwatch locked = new();
                while (true)
                {
                    at = await ReadAsync(bot, addresses, ct);
                    if (at.Battle)
                    {
                        Console.WriteLine($"{segment} battle bit on {Tile(maps, at.Map, at.X, at.Y)} after {at.Frame - sent} frames; planning again after the battle");
                        await ClearAsync(bot, macroEnded);
                        state.State.Push(EventState.BattleStart);
                        return (false, null);
                    }
                    if (!at.Locked)
                    {
                        lockedFrom = null;
                    }
                    else if (lockedFrom is not { } from || !at.On(from.Map, from.X, from.Y))
                    {
                        lockedFrom = at;
                        locked.Restart();
                    }
                    else if (locked.Elapsed >= handOff)
                    {
                        Console.WriteLine($"{segment} field locked {at.Frame - from.Frame} frames on {Tile(maps, at.Map, at.X, at.Y)} with no battle; handing the lock to the battle event");
                        await ClearAsync(bot, macroEnded);
                        state.State.Push(EventState.BattleStart);
                        return (false, null);
                    }
                    bool onTile = macroEnded.IsCompleted && at.Free && at.On(map, x, y);
                    if (onTile && (!last || (at.Running == 0 && at.TileTransition == 0)))
                    {
                        Console.WriteLine($"{segment} on {Tile(maps, map, x, y)} after {at.Frame - sent} frames");
                        break;
                    }
                    if (sinceSent.Elapsed >= SegmentStale)
                    {
                        await ClearAsync(bot, macroEnded);
                        return (false, Stop(state, onTile
                            ? $"The last step to {Tile(maps, map, x, y)} did not end (running state {at.Running}, tile transition {at.TileTransition})."
                            : $"Stuck walking to {Tile(maps, map, x, y)}.", at));
                    }
                    await Task.Delay(Constant.PollIntervalMs, ct);
                }
            }
            // The last segment's hold keeps B down until the macro is cleared.
            await ClearAsync(bot, macroEnded);
            Console.WriteLine($"[Navigator] arrived on {Tile(maps, at.Map, at.X, at.Y)}: {count} segments, {at.Frame - walkStart} frames");
            return (true, null);
        }

        // FR-11, the nurse, from the counter tile with nothing held (the walk cleared its macro): each FieldAdvance (and
        // SpinUp, when the player does not face up) sent once on its gate and confirmed by the value it changes, with no
        // trainer check and no retry: every gate is the value on which the game takes the press, and the one dropped A seen
        // (at the Yes/No with data[2] 1) is the one its gate excludes. Then the party's HP must read full, or the bot stops;
        // full, the state becomes ReturnToStart.
        private async Task<string?> HealAsync(GameState state, CancellationToken ct)
        {
            IBot bot = state.Bot;
            RsPointerService addresses = (RsPointerService)bot.Addresses;
            NurseRead at = await ReadNurseAsync(bot, addresses, ct);
            uint start = at.Frame;
            string waitingFor = "the player at rest on the counter tile";
            // The press waiting for its confirming value, and what was pressed on which read; null between presses.
            Func<NurseRead, bool>? confirmed = null;
            string pressed = "";
            NurseRead pressedOn = at;
            bool spun = false, talked = false, yes = false, last = false;
            int presses = 0;
            Stopwatch unchanged = Stopwatch.StartNew();
            while (true)
            {
                if (confirmed is not null && confirmed(at))
                {
                    Console.WriteLine($"[Navigator] nurse: {pressed} at {pressedOn}, confirmed by {at} after {at.Frame - pressedOn.Frame} frames");
                    confirmed = null;
                    if (last)
                    {
                        break;
                    }
                    waitingFor = yes ? "a page break or the script's last button wait"
                        : talked ? "a page break or the Yes/No with its counter at 5"
                        : "the player at rest on the counter tile facing up";
                }
                (string Macro, string Confirming, Func<NurseRead, bool> Confirmed)? press = null;
                if (confirmed is null)
                {
                    if (!talked && at.AtRest && at.FacesUp)
                    {
                        talked = true;
                        press = ("FieldAdvance", "the field locked after the talk", r => r.Lock != 0);
                    }
                    else if (!talked && at.AtRest && !spun)
                    {
                        spun = true;
                        press = ("SpinUp", "the facing up after SpinUp", r => r.FacesUp);
                    }
                    else if (talked && at.AtPageBreak)
                    {
                        ushort window = at.Window;
                        press = ("FieldAdvance", $"the window leaving {window}", r => r.Window != window);
                    }
                    else if (talked && !yes && at.YesNo == YesNoReady)
                    {
                        yes = true;
                        press = ("FieldAdvance", "the Yes/No task gone after Yes", r => r.YesNo is null);
                    }
                    // The native alone is stale after any script ends: only after the Yes, and only with status 0 and mode 2.
                    else if (yes && at.Status == ScriptRunning && at.Mode == NativeMode && at.Native == addresses.WaitForAorBPress)
                    {
                        last = true;
                        press = ("FieldAdvance", "the lock 0 and status 2 after the last line", r => r.Lock == 0 && r.Status == NoScript);
                    }
                }
                if (press is { } p)
                {
                    bot.RunMacroString(MacroRegistry.Instance.GetMacro(GameFlavor.RS, p.Macro));
                    await bot.WaitForMacroAsync(ct);
                    presses++;
                    (pressed, pressedOn, waitingFor, confirmed) = (p.Macro, at, p.Confirming, p.Confirmed);
                }
                else if (unchanged.Elapsed >= NurseStale)
                {
                    return Stop(state, $"Waited for {waitingFor}; the game state stayed {at}.", null);
                }
                await Task.Delay(Constant.PollIntervalMs, ct);
                NurseRead next = await ReadNurseAsync(bot, addresses, ct);
                if (!next.SameAs(at))
                {
                    unchanged.Restart();
                }
                at = next;
            }
            Console.WriteLine($"[Navigator] nurse: {presses} presses, {at.Frame - start} frames");

            PokemonState party = await pokemonStateReader.GetPartyState(bot);
            PokemonData[] members = party.PartyPokemon.Take(party.PartySize).Select(p => p!).ToArray();
            PokemonData[] hurt = members.Where(p => p.HP != p.MaxHP).ToArray();
            if (hurt.Length > 0)
            {
                return Stop(state, $"The party's HP was not full after the nurse: {string.Join(", ", hurt.Select(Hp))}.", null);
            }
            Console.WriteLine($"[Navigator] nurse: party HP {string.Join(", ", members.Select(Hp))} (full)");
            state.SwapState(EventState.ReturnToStart);
            return null;
        }

        private static string Hp(PokemonData pokemon) => $"{pokemon.Name} {pokemon.HP}/{pokemon.MaxHP}";

        private static async Task<NurseRead> ReadNurseAsync(IBot bot, RsPointerService addresses, CancellationToken ct)
        {
            int[] direct = [];
            ChainBlockResult[] read = await bot.ReadChainBlocksAsync(
            [
                new(addresses.FieldControlsLock, direct, 1),
                new(addresses.Callback1, direct, 4),
                new(addresses.FieldMessageWindowState, direct, 2),
                new(addresses.ScriptContextStatus, direct, 1),
                new(addresses.ScriptContextMode, direct, 1),
                new(addresses.ScriptNative, direct, 4),
                new(addresses.Tasks, direct, TaskSize * TaskCount),
                new(addresses.VBlankCounter, direct, 4),
                new(addresses.PlayerFacing, direct, 1),
                new(addresses.PlayerTileTransition, direct, 1),
            ], ct: ct);
            byte[] tasks = read[6].Bytes;
            short? yesNo = null;
            for (int task = 0; task < tasks.Length; task += TaskSize)
            {
                if (tasks[task + TaskIsActive] != 0 && BitConverter.ToUInt32(tasks, task) == addresses.TaskHandleYesNoInput)
                {
                    yesNo = BitConverter.ToInt16(tasks, task + TaskYesNoCounter);
                    break;
                }
            }
            return new NurseRead(read[0].Bytes[0], BitConverter.ToUInt32(read[1].Bytes, 0), BitConverter.ToUInt16(read[2].Bytes, 0),
                read[3].Bytes[0], read[4].Bytes[0], BitConverter.ToUInt32(read[5].Bytes, 0), yesNo,
                BitConverter.ToUInt32(read[7].Bytes, 0), read[8].Bytes[0], read[9].Bytes[0]);
        }

        private static async Task<WalkRead> ReadAsync(IBot bot, RsPointerService addresses, CancellationToken ct)
        {
            int[] direct = [];
            ChainBlockResult[] read = await bot.ReadChainBlocksAsync(
            [
                new(addresses.FieldControlsLock, direct, 1),
                new(addresses.Callback1, direct, 4),
                new(addresses.InBattle, direct, 1),
                new(addresses.VBlankCounter, direct, 4),
                new(addresses.PlayerPosition, direct, 4),
                new(addresses.PlayerLocation, direct, 2),
                new(addresses.PlayerRunningState, direct, 2),
            ], ct: ct);
            return new WalkRead(read[0].Bytes[0], BitConverter.ToUInt32(read[1].Bytes, 0), (read[2].Bytes[0] & InBattleBit) != 0,
                BitConverter.ToUInt32(read[3].Bytes, 0), read[5].Bytes[0] * 256 + read[5].Bytes[1],
                BitConverter.ToInt16(read[4].Bytes, 0), BitConverter.ToInt16(read[4].Bytes, 2), read[6].Bytes[0], read[6].Bytes[1]);
        }

        // Clearing lets go of the held d-pad and B; the macro then ends.
        private static async Task ClearAsync(IBot bot, Task macroEnded)
        {
            bot.ClearMacro();
            await macroEnded;
        }

        private static string Tile(MapService maps, int map, int x, int y) => $"{maps.GetMapLookup(map).InternalName} ({x}, {y})";

        private static string Stop(GameState state, string sentence, WalkRead? last)
        {
            Console.WriteLine(last is null ? $"[Navigator] stopped: {sentence}" : $"[Navigator] stopped: {sentence} {last}");
            state.SwapState(EventState.Waiting);
            return sentence;
        }
    }
}
