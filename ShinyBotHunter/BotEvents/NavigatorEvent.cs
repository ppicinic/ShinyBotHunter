using ShinyBotHunter.Bot;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Util;
using System.Diagnostics;

namespace ShinyBotHunter.BotEvents
{
    public class NavigatorEvent : AbstractBotEvent<GameState>
    {
        private readonly PokemonStateReader reader;

        // How often the party is re-read while walking to catch a poison faint. Each tick costs a
        // party read plus decrypt, so this is deliberately far slower than the battle poll.
        private const int FaintPollIntervalMs = 1000;

        // Gap between a candidate poison event and the read that confirms it. The walk macro keeps
        // running through this window -- nothing is interrupted until both reads agree.
        private const int PoisonConfirmDelayMs = 250;

        // How many times a party read is retried before giving up on getting a plausible one.
        private const int PartyReadAttempts = 3;

        // Highest HP any gen 4 pokemon can have (Blissey at level 100 is 714). Anything above this
        // means the read landed mid-rewrite.
        private const int MaxPossibleHp = 999;

        // Tile directly below the nurse's counter, where the heal dialogue is started. Every HGSS
        // pokemon center is its own map id but reuses one interior layout (internal name *PC0101),
        // so this single tile is correct in all of them -- the same reason the graph puts every
        // center's entrance node at (8,19).
        private const int PokemonCenterHealX = 8;
        private const int PokemonCenterHealY = 13;

        // How long the trainer can stay on one tile mid-walk before the segment counts as blocked.
        // Has to outlast a warp transition, which freezes the coordinates for around a second.
        private const int StallTimeoutMs = 2500;
        private const int StallPollIntervalMs = 250;

        // Extra cost applied to a tile the bot got stuck on. Large next to a normal step (1, or 2
        // through a turn) so A* takes any reasonable detour, but finite so the tile stays usable when
        // it is the only way through.
        private const int StuckTilePenalty = 25;

        // Recovery attempts allowed on one segment before the route is abandoned. Without a cap a
        // sign that cannot be routed around would retry forever.
        private const int MaxBlockedRetries = 3;

        // Dismisses whatever dialogue is up. B rather than A: A on a signpost just reopens it.
        private const string DismissTextMacro = "T.B;W.30;T.B;W.30";

        // A field script whose snapshot has not changed for this long parks the bot (R18).
        private static readonly TimeSpan ScriptStale = TimeSpan.FromSeconds(20);

        // The prompt-press values of LevelUpFlow: a press that has not landed after a second is pressed again.
        private static readonly TimeSpan ScriptPressConfirm = TimeSpan.FromMilliseconds(1000);
        private const int ScriptPressAttempts = 3;

        // The 20-frame settle means the next read already sees the press's effect.
        private const string PressA = "T.A;W.20";
        private const string PressB = "T.B;W.20";

        // Faces the nurse and talks to her. The counter above the heal tile is a wall, so Up only turns.
        private const string StartNurseScriptMacro = "T.Up;W.5;T.A";

        // How a walk macro ended.
        private enum SegmentOutcome
        {
            Completed,
            Battle,
            PoisonEvent,
            Blocked,
        }

        public NavigatorEvent(PokemonStateReader reader)
        {
            this.reader = reader;
        }

        public override async Task<GameState> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            EventState topState = state.State.Peek();

            try
            {
                if (topState == EventState.GoToPokemonCenter)
                    await GoToPokemonCenterAsync(state, cancellationToken);
                else if (topState == EventState.HealAtPokemonCenter)
                    await HealAtPokemonCenterAsync(state, cancellationToken);
                else if (topState == EventState.ReturnToStart)
                    await ReturnToStartAsync(state, cancellationToken);
            }
            catch (GameStateTimeoutException ex)
            {
                // A stale-timeout parks the bot for the operator rather than crashing the event.
                Console.WriteLine($"[Navigator] {ex}");
                state.SwapState(EventState.Waiting);
            }

            return state;
        }

        private async Task GoToPokemonCenterAsync(GameState state, CancellationToken cancellationToken)
        {
            await WaitForOverworldReadyAsync(state.Bot, cancellationToken);
            if (state.NavigationPath is null)
            {
                var mapId = (int)await state.Bot.Overworld.GetMapIdAsync(cancellationToken);
                var (x, y) = await state.Bot.Overworld.GetPositionAsync(cancellationToken);
                var facing = await state.Bot.Overworld.GetFacingAsync(cancellationToken);
                var nodePath = await state.Bot.Profile.Pathfinding!.FindClosestPokemonCenter(mapId, x, y, new Capability(), (byte)facing);
                if (nodePath is null)
                {
                    Console.WriteLine($"[Navigator] No pokemon center found from map={mapId} ({x},{y})");
                    state.SwapState(EventState.Complete);
                    return;
                }
                state.NavigationPath = nodePath.ToList();
                // The graph only gets us to the center's entrance node. The walk from there to the
                // counter used to be baked into the heal macro, where a poison faint left it pressing
                // buttons into the faint dialogue. As a final path segment it goes through the same
                // A* and the same faint detection as every other leg.
                state.NavigationPath.Add(BuildHealTileNode(state.NavigationPath[^1].Node.MapId));
                state.NavigationPathIndex = 0;
                Console.WriteLine($"[Navigator] Path found: {string.Join(" -> ", state.NavigationPath.Select(n => $"map={n.Node.MapId}({n.Node.X},{n.Node.Y})"))}");
                Console.WriteLine($"[Navigator] Starting from map={mapId} ({x},{y})");
            }

            bool battleOccurred = await ExecuteNavigationAsync(state, cancellationToken);

            if (battleOccurred)
            {
                state.State.Push(EventState.BattleStart);
            }
            else
            {
                ClearNavigation(state, "arrived at pokemon center");
                state.SwapState(EventState.HealAtPokemonCenter);
            }
        }

        // Synthetic end-of-route node for the tile in front of the nurse. Only MapId/X/Y are read
        // back out during navigation, and the id follows the convention the pathfinder already uses
        // for nodes that aren't in the graph (-1 start, -2 end).
        private static NodePoint BuildHealTileNode(int pokemonCenterMapId)
        {
            return new NodePoint()
            {
                Node = new Node()
                {
                    Id = -3,
                    MapId = pokemonCenterMapId,
                    X = PokemonCenterHealX,
                    Y = PokemonCenterHealY,
                },
            };
        }

        private static async Task HealAtPokemonCenterAsync(GameState state, CancellationToken cancellationToken)
        {
            // Navigation has already walked us onto the counter tile.
            Console.WriteLine("[Navigator] Healing with the script press loop");
            var addresses = (HgssPointerService)state.Bot.Addresses;
            Func<string, Task> press = Press(state.Bot, cancellationToken);
            // Retried rather than settled first: an A eaten by the last step's animation is pressed again.
            await GameStateWait.PressUntilAsync(state.Bot.State, () => press(StartNurseScriptMacro), "the nurse's script to start",
                s => s.Screen == GameScreen.FieldTask && s.TaskFunction == addresses.RunScriptsTask,
                ScriptPressConfirm, ScriptPressAttempts, cancellationToken, settleMs: 0);
            await PressThroughScriptAsync(state.Bot.State, addresses, press, "the nurse's script", ScriptStale, cancellationToken);
            state.SwapState(EventState.ReturnToStart);
        }

        private static Func<string, Task> Press(IBot bot, CancellationToken cancellationToken) => async macro =>
        {
            bot.RunMacroString(macro);
            await bot.WaitForMacroAsync(cancellationToken);
        };

        /// <summary>
        /// Presses through a field script until the field is free (R18, R19): A while a context waits in the
        /// printing or wait-button natives, one A at the menu native (Yes), then B for every later wait, since B
        /// closes a message as well as A and cannot start a second conversation or answer a second menu. The printing
        /// native gets one press per arrow of its printer, confirmed by the printer leaving the arrow, and none while
        /// the text draws or no printer is read (env.printer is not slot 0: the stale-timeout then names the script
        /// and its snapshot). Nothing is pressed during movement, WaitStd, the touch menu, bytecode, child tasks or a transition. The Yes also
        /// counts as given once the heal animation task is seen, because it only runs after Yes, so an A that
        /// landed on the menu by race is never followed by another A. Throws when the snapshot has not changed for
        /// stale; the clock is not restarted by a press, so a box that never closes under presses still times out.
        /// </summary>
        internal static async Task PressThroughScriptAsync(
            IGameStateReader game, HgssPointerService addresses, Func<string, Task> press, string waitingFor, TimeSpan stale, CancellationToken ct)
        {
            GameSnapshot? last = null;
            Stopwatch unchanged = Stopwatch.StartNew();
            bool yesGiven = false;
            while (true)
            {
                GameSnapshot snapshot = await game.ReadAsync(ct);
                if (snapshot != last)
                {
                    Console.WriteLine($"[GameState] {waitingFor}: {snapshot}");
                    last = snapshot;
                    unchanged.Restart();
                }
                if (snapshot.Screen == GameScreen.FieldFree)
                {
                    return;
                }
                if (unchanged.Elapsed >= stale)
                {
                    throw new GameStateTimeoutException(waitingFor, snapshot);
                }
                if (snapshot.TaskFunction == addresses.HealAnimationTask)
                {
                    yesGiven = true;
                }

                uint? native = snapshot.Native;
                bool decompOnly = native == addresses.WaitButtonOrDelayNative || native == addresses.WaitButtonOrDpadNative;
                if (native == addresses.MenuChoiceNative && !yesGiven)
                {
                    await GameStateWait.PressUntilAsync(game, () => press(PressA), $"{waitingFor} Yes",
                        s => s.Native != addresses.MenuChoiceNative, ScriptPressConfirm, ScriptPressAttempts, ct);
                    Console.WriteLine($"[Navigator] {waitingFor}: Yes at the menu native");
                    yesGiven = true;
                }
                else if (native == addresses.PrintingNative)
                {
                    if (snapshot.AtTextArrow())
                    {
                        int presses = 0;
                        await GameStateWait.PressUntilAsync(game, () => { presses++; return press(yesGiven ? PressB : PressA); }, $"{waitingFor} box",
                            s => s.Native != addresses.PrintingNative || !s.AtTextArrow(), ScriptPressConfirm, ScriptPressAttempts, ct, settleMs: 0);
                        Console.WriteLine($"[Navigator] {waitingFor}: {(yesGiven ? "T.B" : "T.A")} at native 0x{native:X8} (printer {snapshot.TextPrinter}) after {presses} press(es)");
                    }
                }
                else if (native == addresses.WaitButtonNative || decompOnly || native == addresses.MenuChoiceNative)
                {
                    await press(yesGiven ? PressB : PressA);
                    Console.WriteLine($"[Navigator] {waitingFor}: {(yesGiven ? "T.B" : "T.A")} at native 0x{native:X8}{(decompOnly ? " (decomp-only)" : "")}");
                }
                await Task.Delay(Constant.PollIntervalMs, ct);
            }
        }

        private async Task ReturnToStartAsync(GameState state, CancellationToken cancellationToken)
        {
            await WaitForOverworldReadyAsync(state.Bot, cancellationToken);
            if (state.NavigationPath is null)
            {
                var mapId = (int)await state.Bot.Overworld.GetMapIdAsync(cancellationToken);
                var (x, y) = await state.Bot.Overworld.GetPositionAsync(cancellationToken);

                // Already home -- pathfinding a zero-length route would either fail or emit an
                // empty macro, so short circuit. Common when the run ends between encounters.
                if (mapId == state.StartMapId && x == state.StartX && y == state.StartY)
                {
                    FinishReturn(state, "already at start");
                    return;
                }

                var facing = await state.Bot.Overworld.GetFacingAsync(cancellationToken);
                var nodePath = await state.Bot.Profile.Pathfinding!.FindNodePath(
                    mapId, x, y,
                    state.StartMapId, state.StartX, state.StartY,
                    new Capability(), (byte)facing);
                if (nodePath is null)
                {
                    Console.WriteLine($"[Navigator] No path back to start from map={mapId} ({x},{y}) -- completing in place");
                    state.CompleteAfterReturn = false;
                    state.SwapState(EventState.Complete);
                    return;
                }
                state.NavigationPath = nodePath.ToList();
                state.NavigationPathIndex = 0;
                Console.WriteLine($"[Navigator] Return path found: {string.Join(" -> ", state.NavigationPath.Select(n => $"map={n.Node.MapId}({n.Node.X},{n.Node.Y})"))}");
                Console.WriteLine($"[Navigator] Returning to start map={state.StartMapId} ({state.StartX},{state.StartY}) from map={mapId} ({x},{y})");
            }

            bool battleOccurred = await ExecuteNavigationAsync(state, cancellationToken);

            if (battleOccurred)
            {
                state.State.Push(EventState.BattleStart);
            }
            else
            {
                FinishReturn(state, "arrived back at start");
            }
        }

        /// <summary>
        /// Ends a return-to-start leg. A run whose end condition is already met finishes here rather
        /// than at the point the condition was noticed, so the next event always begins from the
        /// same tile.
        /// </summary>
        private static void FinishReturn(GameState state, string reason)
        {
            ClearNavigation(state, reason);
            if (state.CompleteAfterReturn)
            {
                state.CompleteAfterReturn = false;
                state.SwapState(EventState.Complete);
            }
            else
            {
                state.SwapState(EventState.DecideOverworldAction);
            }
        }

        // Single place navigation state is dropped, so the log always says which leg finished
        // and how far through the path it got.
        private static void ClearNavigation(GameState state, string reason)
        {
            int segments = state.NavigationPath is null ? 0 : state.NavigationPath.Count - 1;
            Console.WriteLine($"[Navigator] Clearing navigation path ({reason}) at segment {state.NavigationPathIndex}/{segments}");
            state.NavigationPath = null;
            state.NavigationPathIndex = 0;
        }

        // Walks the NodePath segment by segment, running A* for each leg.
        // Returns true if a battle was encountered (NavigationPathIndex is left at the
        // in-progress segment so the A* can be recomputed on resume).
        private async Task<bool> ExecuteNavigationAsync(GameState state, CancellationToken cancellationToken)
        {
            // Covers resuming navigation right after a mid-route battle, where the first
            // segment's position read would otherwise hit the post-battle (0,0).
            await WaitForOverworldReadyAsync(state.Bot, cancellationToken);

            var path = state.NavigationPath!;

            // Party state at the start of this navigation. A field poison tick resolving mid-walk
            // puts dialogue on screen, which swallows the rest of the macro.
            var partyAtStart = await ReadPartySnapshotAsync(state.Bot, cancellationToken);

            int blockedRetries = 0;
            int retriesForSegment = state.NavigationPathIndex;

            while (state.NavigationPathIndex < path.Count - 1)
            {
                // The cap is per segment, so progress resets the budget.
                if (state.NavigationPathIndex != retriesForSegment)
                {
                    retriesForSegment = state.NavigationPathIndex;
                    blockedRetries = 0;
                }

                var toNode = path[state.NavigationPathIndex + 1].Node;

                var mapId = (int)await state.Bot.Overworld.GetMapIdAsync(cancellationToken);
                var (x, y) = await state.Bot.Overworld.GetPositionAsync(cancellationToken);
                var facing = await state.Bot.Overworld.GetFacingAsync(cancellationToken);

                // If the node after toNode is on a different map, toNode is a portal tile
                // (door/cave entrance). Pass the destination node's internal map id so the
                // final M. command stops the instant the warp fires and the live map id
                // becomes one of that internal map's raw game ids. This prevents the
                // infinite button hold caused by coordinates resetting on the new map.
                bool toNodeIsPortal = state.NavigationPathIndex + 2 < path.Count
                    && path[state.NavigationPathIndex + 2].Node.MapId != toNode.MapId;
                int? destBotMapId = toNodeIsPortal
                    ? path[state.NavigationPathIndex + 2].Node.MapId
                    : null;

                // When crossing a portal, the graph node may point to the spawn position
                // rather than the actual warp trigger tile. Eagerly scan adjacent tiles
                // and move toward the portal tile, prioritising the direction of travel.
                int destX = toNode.X, destY = toNode.Y;
                bool overshoot = false;
                if (toNodeIsPortal)
                {
                    var portalTile = state.Bot.Profile.Pathfinding!.GetAdjacentPortalTarget(mapId, toNode.X, toNode.Y, x, y);
                    if (portalTile.HasValue)
                    {
                        destX = portalTile.Value.X;
                        destY = portalTile.Value.Y;
                    }
                    else
                    {
                        // Building exit — no portal tile marker, so overshoot by 1 to press
                        // through the exit tile and trigger the warp.
                        overshoot = true;
                    }
                }

                Console.WriteLine($"[Navigator] Segment {state.NavigationPathIndex}: map={mapId} ({x},{y}) -> ({destX},{destY}) portal={toNodeIsPortal} overshoot={overshoot}");
                string? macro = state.Bot.Profile.Pathfinding!.Pathfind(mapId, x, y, destX, destY, new Capability(), (byte)facing, destBotMapId, overshoot, PenaltiesForMap(state, mapId));
                Console.WriteLine($"[Navigator] Macro: {(macro ?? "null")}");

                if (macro is not null)
                {
                    var outcome = await ExecuteSegmentMacroAsync(state, macro, partyAtStart, cancellationToken);
                    if (outcome == SegmentOutcome.Battle)
                        return true;
                    if (outcome == SegmentOutcome.PoisonEvent)
                    {
                        await ClearPoisonScriptAsync(state, cancellationToken);
                        // The mon that just resolved is now fainted or no longer poisoned, so
                        // re-snapshotting stops it from tripping the poll again -- each event is
                        // handled exactly once.
                        partyAtStart = await ReadPartySnapshotAsync(state.Bot, cancellationToken);
                        // Retry this same segment: the walk stopped somewhere short of the node, so
                        // the loop re-reads our real position and recomputes the A* from there.
                        continue;
                    }
                    if (outcome == SegmentOutcome.Blocked)
                    {
                        blockedRetries++;
                        if (blockedRetries > MaxBlockedRetries)
                        {
                            Console.WriteLine($"[Navigator] Segment {state.NavigationPathIndex} blocked {MaxBlockedRetries} times; abandoning the route.");
                            return false;
                        }
                        await RecoverFromBlockedSegmentAsync(state, cancellationToken);
                        // Retry with the penalty in place, so the recomputed A* prefers another way.
                        continue;
                    }
                }

                // After a portal crossing, poll until we're on the destination map
                // and coordinates are valid.
                var mapIdAfter = (int)await state.Bot.Overworld.GetMapIdAsync(cancellationToken);
                if (toNodeIsPortal)
                {
                    var warpDest = path[state.NavigationPathIndex + 2].Node;
                    await WaitForWarpAsync(state.Bot, mapIdAfter, warpDest.X, warpDest.Y, cancellationToken);
                    // The warp wait accepts a landing within one tile of the spawn node, and walking
                    // back onto the spawn node can step onto the door again, so the next segment goes
                    // from where we landed to the node after it. When the spawn node is the last
                    // node, this ends the walk.
                    var (landX, landY) = await state.Bot.Overworld.GetPositionAsync(cancellationToken);
                    Console.WriteLine($"[Navigator] Portal crossed: at ({landX},{landY}) on map {mapIdAfter}, skipping the spawn node ({warpDest.X},{warpDest.Y})");
                    state.NavigationPathIndex += 2;
                    continue;
                }
                else if (mapIdAfter != mapId)
                {
                    await WaitForWarpAsync(state.Bot, mapIdAfter, null, null, cancellationToken);
                }

                state.NavigationPathIndex++;
            }

            return false;
        }

        // Runs macro and races it against battle detection and a party faint.
        // Returns what interrupted the macro, if anything.
        private async Task<SegmentOutcome> ExecuteSegmentMacroAsync(GameState state, string macro, PartySnapshot partyAtStart, CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            state.Bot.RunMacroString(macro);

            Task macroTask = state.Bot.WaitForMacroAsync(cancellationToken);
            Task battleTask = WaitForBattleAsync(state.Bot, cts.Token);
            Task poisonTask = WaitForPoisonEventAsync(state.Bot, partyAtStart, cts.Token);
            Task stallTask = WaitForStallAsync(state.Bot, cts.Token);

            Task first = await Task.WhenAny(macroTask, battleTask, poisonTask, stallTask);

            cancellationToken.ThrowIfCancellationRequested();

            cts.Cancel();

            if (!ReferenceEquals(first, macroTask))
            {
                state.Bot.ClearMacro();
                await state.Bot.WaitForMacroAsync(cancellationToken);
                // A faulted racer (a stale-timeout or a read failure) rethrows here rather than being
                // read as its outcome.
                await first;
                if (ReferenceEquals(first, battleTask)) return SegmentOutcome.Battle;
                if (ReferenceEquals(first, poisonTask)) return SegmentOutcome.PoisonEvent;
                return SegmentOutcome.Blocked;
            }

            return SegmentOutcome.Completed;
        }

        /// <summary>
        /// Completes when the trainer has stopped moving for <see cref="StallTimeoutMs"/> while a walk
        /// macro is still running and the field is free. The macro holds a direction until the
        /// coordinate reaches its target, so anything that blocks movement holds that direction forever
        /// with nothing else noticing. A script or a warp is not a stall: while the screen is not
        /// <see cref="GameScreen.FieldFree"/> the timer is held, bounded by the overworld-ready wait.
        /// </summary>
        private async Task WaitForStallAsync(IBot bot, CancellationToken cancellationToken)
        {
            (short lastX, short lastY) = await bot.Overworld.GetPositionAsync(cancellationToken);
            var still = Stopwatch.StartNew();
            while (true)
            {
                await Task.Delay(StallPollIntervalMs, cancellationToken);
                GameSnapshot snapshot = await bot.State.ReadAsync(cancellationToken);
                if (snapshot.Screen != GameScreen.FieldFree)
                {
                    Console.WriteLine($"[Navigator] Stall timer held: {snapshot}");
                    await Overworld.WaitForReadyAsync(bot.State, cancellationToken);
                    (lastX, lastY) = await bot.Overworld.GetPositionAsync(cancellationToken);
                    still.Restart();
                    continue;
                }
                (short x, short y) = await bot.Overworld.GetPositionAsync(cancellationToken);
                if (x != lastX || y != lastY)
                {
                    lastX = x;
                    lastY = y;
                    still.Restart();
                    continue;
                }
                if (still.ElapsedMilliseconds >= StallTimeoutMs)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Clears whatever locked movement and makes the tile we are standing on expensive, so the
        /// recomputed route prefers to go around it. The tile is the trainer's own position, not the
        /// obstacle it is facing: the obstacle is already impassable to A*, and it is standing here
        /// and pressing into it that opens the message.
        /// </summary>
        private async Task RecoverFromBlockedSegmentAsync(GameState state, CancellationToken cancellationToken)
        {
            state.Bot.RunMacroString(DismissTextMacro);
            await state.Bot.WaitForMacroAsync(cancellationToken);

            int mapId = (int)await state.Bot.Overworld.GetMapIdAsync(cancellationToken);
            (int x, int y) = await state.Bot.Overworld.GetPositionAsync(cancellationToken);

            if (!state.TilePenalties.TryGetValue(mapId, out var penalties))
            {
                penalties = new Dictionary<(int X, int Y), int>();
                state.TilePenalties[mapId] = penalties;
            }
            penalties.TryGetValue((x, y), out int existing);
            penalties[(x, y)] = existing + StuckTilePenalty;

            Console.WriteLine($"[Navigator] Movement blocked at map={mapId} ({x},{y}); dismissed the dialogue and set tile cost {penalties[(x, y)]}.");
        }

        private static IReadOnlyDictionary<(int X, int Y), int>? PenaltiesForMap(GameState state, int mapId)
        {
            return state.TilePenalties.TryGetValue(mapId, out var penalties) ? penalties : null;
        }

        // Presses through std_survive_poisoning so the overworld accepts input again (R19). Seeing the script first
        // means the party diff saw the real message, not a garbage read.
        private static async Task ClearPoisonScriptAsync(GameState state, CancellationToken cancellationToken)
        {
            var addresses = (HgssPointerService)state.Bot.Addresses;
            await GameStateWait.UntilAsync(state.Bot.State, "the survive-poisoning script",
                s => s.Screen == GameScreen.FieldTask && s.Script == addresses.SurvivePoisoningScript, ScriptStale, cancellationToken);
            await PressThroughScriptAsync(state.Bot.State, addresses, Press(state.Bot, cancellationToken), "the survive-poisoning script",
                ScriptStale, cancellationToken);
        }

        // Party members that could still produce a field poison message, keyed by PID rather than
        // slot so a party reorder can't be read as a state change.
        private record PartySnapshot(HashSet<uint> Alive, HashSet<uint> Poisoned);

        // A party member whose state says a field poison tick just resolved.
        private record PoisonEvent(uint Pid, string Name, int Hp, int MaxHp, byte Status, string Reason)
        {
            public override string ToString() => $"{Name} pid={Pid:X8} hp={Hp}/{MaxHp} status=0x{Status:X2} ({Reason})";
        }

        /// <summary>
        /// Rejects a party entry whose live stats can't be real. The battle stats block sits past the
        /// range the parser checksums, so a read landing while the game rewrites it comes back
        /// "valid" with garbage -- HP above max, or a status byte with a sleep counter and poison set
        /// at once. Acting on those is what aborted routes mid-walk for no reason.
        /// </summary>
        private static bool IsPlausible(PokemonData pokemon)
        {
            if (pokemon.Level < 1 || pokemon.Level > 100)
                return false;
            if (pokemon.MaxHP < 1 || pokemon.MaxHP > MaxPossibleHp)
                return false;
            if (pokemon.HP > pokemon.MaxHP)
                return false;
            return IsValidStatus(pokemon.Status);
        }

        // Only one major status at a time, so the byte is either clear, a sleep counter, or exactly
        // one of the single-status bits.
        private static bool IsValidStatus(byte status)
        {
            if (status == 0)
                return true;
            if ((status & 0x07) != 0)
                return (status & 0xF8) == 0;
            return status == 0x08 || status == 0x10 || status == 0x20 || status == 0x40 || status == 0x80;
        }

        // A party read with every live entry plausible, or null when the read caught a rewrite.
        private async Task<PokemonState?> ReadValidatedPartyAsync(IBot bot)
        {
            PokemonState party;
            try
            {
                party = await reader.GetPartyState(bot);
            }
            catch (PartyReadUnavailableException ex)
            {
                // The emulator already retried per frame and gave up. Nothing to add by retrying here.
                Console.WriteLine($"[Navigator] Party read unavailable: {ex.Message}");
                return null;
            }
            for (int i = 0; i < party.PartySize; i++)
            {
                var pokemon = party.PartyPokemon[i];
                if (pokemon is not null && !IsPlausible(pokemon))
                {
                    Console.WriteLine($"[Navigator] Discarded implausible party read: slot {i + 1} {pokemon.Name} lv={pokemon.Level} hp={pokemon.HP}/{pokemon.MaxHP} status=0x{pokemon.Status:X2}");
                    return null;
                }
            }
            return party;
        }

        private async Task<PokemonState?> ReadValidatedPartyWithRetriesAsync(IBot bot, CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt < PartyReadAttempts; attempt++)
            {
                var party = await ReadValidatedPartyAsync(bot);
                if (party is not null)
                    return party;
                await Task.Delay(PoisonConfirmDelayMs, cancellationToken);
            }
            return null;
        }

        /// <summary>
        /// The baseline every later poll is compared against, so a single bad read here would make
        /// every subsequent good read look like a state change. Two agreeing reads are required
        /// before a member counts as alive or poisoned; anything they disagree on is left out, which
        /// costs at most a missed event rather than a route aborted on garbage.
        /// </summary>
        private async Task<PartySnapshot> ReadPartySnapshotAsync(IBot bot, CancellationToken cancellationToken)
        {
            var first = await ReadValidatedPartyWithRetriesAsync(bot, cancellationToken);
            await Task.Delay(PoisonConfirmDelayMs, cancellationToken);
            var second = await ReadValidatedPartyWithRetriesAsync(bot, cancellationToken);
            if (first is null || second is null)
            {
                // Nothing trustworthy to watch. An empty snapshot simply means no poison event can
                // fire for this leg -- a real faint still stops the walk when the battle check or
                // the next segment's snapshot catches up.
                Console.WriteLine("[Navigator] Party snapshot unavailable (reads kept coming back implausible); poison watch disabled for this leg");
                return new PartySnapshot([], []);
            }

            var alive = new HashSet<uint>();
            var poisoned = new HashSet<uint>();
            var poisonedNames = new List<string>();
            var secondById = new Dictionary<uint, PokemonData>();
            for (int i = 0; i < second.PartySize; i++)
            {
                var pokemon = second.PartyPokemon[i];
                if (pokemon is not null)
                    secondById[pokemon.PID] = pokemon;
            }
            for (int i = 0; i < first.PartySize; i++)
            {
                var pokemon = first.PartyPokemon[i];
                if (pokemon is null || !secondById.TryGetValue(pokemon.PID, out var confirm))
                    continue;
                if (pokemon.HP > 0 && confirm.HP > 0)
                    alive.Add(pokemon.PID);
                if (pokemon.IsPoisoned && confirm.IsPoisoned)
                {
                    poisoned.Add(pokemon.PID);
                    poisonedNames.Add($"{pokemon.Name} (0x{pokemon.Status:X2})");
                }
                else if (pokemon.IsPoisoned != confirm.IsPoisoned)
                {
                    Console.WriteLine($"[Navigator] Snapshot reads disagree on {pokemon.Name} poison (0x{pokemon.Status:X2} vs 0x{confirm.Status:X2}); not watching it");
                }
            }
            Console.WriteLine($"[Navigator] Party snapshot: {alive.Count} alive, poisoned: {(poisonedNames.Count == 0 ? "none" : string.Join(", ", poisonedNames))}");
            return new PartySnapshot(alive, poisoned);
        }

        /// <summary>
        /// A field poison tick resolving on a party member -- either someone who was alive is now at
        /// 0 HP, or someone who was poisoned no longer is (the game clears the status when the tick
        /// is survived). Both put dialogue on screen. Only members that were already poisoned are
        /// watched for the status change, since nothing else clears poison mid-walk.
        /// </summary>
        private static PoisonEvent? FindPoisonEvent(PokemonState party, PartySnapshot partyAtStart)
        {
            for (int i = 0; i < party.PartySize; i++)
            {
                var pokemon = party.PartyPokemon[i];
                if (pokemon is null)
                    continue;
                if (pokemon.HP == 0 && partyAtStart.Alive.Contains(pokemon.PID))
                    return new PoisonEvent(pokemon.PID, pokemon.Name, pokemon.HP, pokemon.MaxHP, pokemon.Status, "fainted");
                if (!pokemon.IsPoisoned && partyAtStart.Poisoned.Contains(pokemon.PID))
                    return new PoisonEvent(pokemon.PID, pokemon.Name, pokemon.HP, pokemon.MaxHP, pokemon.Status, "poison cleared");
            }
            return null;
        }

        // Completes once a poison event is seen by two separate party reads. The battle stats block
        // -- status, HP, level -- sits past the range the parser checksums, so a read that lands
        // while the game is rewriting it comes back "valid" with garbage status. One such read used
        // to be enough to abort a route mid-walk. The macro keeps running across the confirmation
        // window, so an unconfirmed candidate costs nothing but a log line.
        private async Task WaitForPoisonEventAsync(IBot bot, PartySnapshot partyAtStart, CancellationToken cancellationToken)
        {
            // Bounded by the segment's stall racer (R12), which ends or times out the race.
            while (true)
            {
                // Cancels out of the loop once another racer wins, so a losing poll can never
                // complete successfully and be mistaken for a poison event.
                await Task.Delay(FaintPollIntervalMs, cancellationToken);
                var candidateParty = await ReadValidatedPartyAsync(bot);
                if (candidateParty is null)
                    continue;
                var candidate = FindPoisonEvent(candidateParty, partyAtStart);
                if (candidate is null)
                    continue;

                await Task.Delay(PoisonConfirmDelayMs, cancellationToken);
                var confirmParty = await ReadValidatedPartyAsync(bot);
                if (confirmParty is null)
                {
                    Console.WriteLine($"[Navigator] Discarded unconfirmed poison event: saw {candidate}, re-read was implausible");
                    continue;
                }
                var confirm = FindPoisonEvent(confirmParty, partyAtStart);
                if (confirm is not null && confirm.Pid == candidate.Pid && confirm.Reason == candidate.Reason)
                {
                    Console.WriteLine($"[Navigator] Poison event confirmed: {confirm}");
                    return;
                }

                Console.WriteLine($"[Navigator] Discarded unconfirmed poison event: saw {candidate}, re-read gave {(confirm is null ? "nothing" : confirm.ToString())}");
            }
        }

        // Waits until the field is free on the destination map (and, when given, within a tile of
        // the destination). Throws after 15 s with neither the screen nor the map id changing (R11).
        private async Task WaitForWarpAsync(IBot bot, int destMapId, int? destX, int? destY, CancellationToken cancellationToken)
        {
            (GameScreen Screen, int MapId)? last = null;
            var unchanged = Stopwatch.StartNew();
            while (true)
            {
                GameSnapshot snapshot = await bot.State.ReadAsync(cancellationToken);
                int mapId = await bot.Overworld.GetMapIdAsync(cancellationToken);
                if (last != (snapshot.Screen, mapId))
                {
                    Console.WriteLine($"[Navigator] Warp to map {destMapId}: on map {mapId}, {snapshot}");
                    last = (snapshot.Screen, mapId);
                    unchanged.Restart();
                }
                if (mapId == destMapId && snapshot.Screen == GameScreen.FieldFree)
                {
                    if (!destX.HasValue || !destY.HasValue)
                        break;
                    var (x, y) = await bot.Overworld.GetPositionAsync(cancellationToken);
                    if (Math.Abs(x - destX.Value) <= 1 && Math.Abs(y - destY.Value) <= 1)
                        break;
                }
                if (unchanged.Elapsed >= TimeSpan.FromSeconds(15))
                    throw new GameStateTimeoutException($"the warp to map {destMapId} (still on map {mapId})", snapshot);
                await Task.Delay(Constant.PollIntervalMs, cancellationToken);
            }
            bot.RunMacroString("W.60");
            await bot.WaitForMacroAsync(cancellationToken);
        }

        // Wait until the field is free: no app, no task, map up, so we don't pathfind from a bogus origin.
        private static Task WaitForOverworldReadyAsync(IBot bot, CancellationToken cancellationToken)
            => Overworld.WaitForReadyAsync(bot.State, cancellationToken);

        private async Task WaitForBattleAsync(IBot bot, CancellationToken cancellationToken)
        {
            await GameStateWait.UntilAsync(bot.State, "a battle",
                s => s.Screen is not (GameScreen.Transitioning or GameScreen.FieldFree or GameScreen.FieldTask or GameScreen.OtherApp),
                stale: null, cancellationToken);
        }
    }
}
