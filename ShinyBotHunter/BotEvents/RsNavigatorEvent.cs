using System.Diagnostics;
using System.Numerics;
using ShinyBotHunter.Bot;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Util;
using Route = ShinyBotHunter.Pathfinding.Route;

namespace ShinyBotHunter.BotEvents
{
    // The Ruby and Sapphire navigator (docs/specs/rs-navigation-and-training.md FR-9, FR-11's nearest Center, FR-19's
    // [Navigator] lines, the navigator side of FR-10): GoToPokemonCenter and ReturnToStart plan with RouteSearch from where
    // the player stands and walk the route segment by segment. Every step of NavigatorEvent's states is HGSS's, so RS has its
    // own event beside it, as RsBattleEvent sits beside BattleOrchestratorEvent. The walk sends the segments' d-pad and B
    // holds only, never a press of A or B. Readings: docs/research/rs-navigation-and-training.md, "The navigator: the walk
    // and the nurse, read frame by frame".
    public class RsNavigatorEvent : AbstractBotEvent<GameState>
    {
        // FR-9: a segment not ended this long after its macro was sent stops the walk; init so a test can shorten it.
        internal TimeSpan SegmentStale { get; init; } = TimeSpan.FromSeconds(20);

        // FR-11: the tile two below the nurse, the same in every Pokémon Center 1F by the data (read in Oldale's only).
        private const int CounterX = 7;
        private const int CounterY = 4;
        private const string CenterSuffix = "_PokemonCenter_1F";

        // gMain.inBattle is bit 1 of its byte (RsStateReader).
        private const byte InBattleBit = 0x02;

        // A tile at elevation 15 takes any elevation; the search starts a player on one at 0 (N2 review on #429).
        private const byte AnyElevation = 15;

        // One walk read: six blocks on one frame, classified in RsStateReader's order. Callback 1 at 0 is a transition and
        // never free: after every warp it reads 0 with the lock 0 for 4 to 7 frames. Then the battle bit, then the lock.
        private readonly record struct WalkRead(byte Lock, uint Callback1, bool InBattle, uint Frame, int Map, int X, int Y)
        {
            public bool Battle => Callback1 != 0 && InBattle;

            public bool Locked => Callback1 != 0 && !InBattle && Lock != 0;

            public bool Free => Callback1 != 0 && !InBattle && Lock == 0;

            public bool On(int map, int x, int y) => Map == map && X == x && Y == y;

            public override string ToString() =>
                $"(f {Frame}: map {Map >> 8}.{Map & 0xFF} ({X}, {Y}), lock {Lock}, callback 1 0x{Callback1:X8}, battle bit {(InBattle ? 1 : 0)})";
        }

        public override async Task<GameState> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            await RunAsync(state, cancellationToken);
            return state;
        }

        // The state on top, GoToPokemonCenter or ReturnToStart, from where the player stands. Returns the sentence it stopped
        // with (Waiting), otherwise null: arrived (HealAtPokemonCenter, or NavigatorEvent.FinishReturn), or a battle or a lock
        // handed to the battle event with BattleStart pushed over the state, which plans afresh when it is on top again.
        internal async Task<string?> RunAsync(GameState state, CancellationToken ct)
        {
            IBot bot = state.Bot;
            RsPointerService addresses = (RsPointerService)bot.Addresses;
            MapService maps = bot.Profile.Maps!;
            bool toCenter = state.State.Peek() == EventState.GoToPokemonCenter;

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
        // field is free on the segment's map and tile, which for a segment cut at a door's coordinate is the landing (R3). The
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
                    if (macroEnded.IsCompleted && at.Free && at.On(map, x, y))
                    {
                        Console.WriteLine($"{segment} on {Tile(maps, map, x, y)} after {at.Frame - sent} frames");
                        break;
                    }
                    if (sinceSent.Elapsed >= SegmentStale)
                    {
                        await ClearAsync(bot, macroEnded);
                        return (false, Stop(state, $"Stuck walking to {Tile(maps, map, x, y)}.", at));
                    }
                    await Task.Delay(Constant.PollIntervalMs, ct);
                }
            }
            // The last segment's hold keeps B down until the macro is cleared.
            await ClearAsync(bot, macroEnded);
            Console.WriteLine($"[Navigator] arrived on {Tile(maps, at.Map, at.X, at.Y)}: {count} segments, {at.Frame - walkStart} frames");
            return (true, null);
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
            ], ct: ct);
            return new WalkRead(read[0].Bytes[0], BitConverter.ToUInt32(read[1].Bytes, 0), (read[2].Bytes[0] & InBattleBit) != 0,
                BitConverter.ToUInt32(read[3].Bytes, 0), read[5].Bytes[0] * 256 + read[5].Bytes[1],
                BitConverter.ToInt16(read[4].Bytes, 0), BitConverter.ToInt16(read[4].Bytes, 2));
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
