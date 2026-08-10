using ShinyBotHunter.Bot;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Service;
using ShinyBotHunter.Util;
using System.Diagnostics;

namespace ShinyBotHunter.BotEvents
{
    public class NavigatorEvent : AbstractBotEvent<GameState>
    {
        public override EventState EventState => throw new NotImplementedException();

        private readonly PathfindingService pathfindingService;
        private readonly PokemonStateReader reader;

        // Upper bound on how long we wait for the overworld coordinates to populate after a
        // battle before giving up and proceeding anyway.
        private const int OverworldReadyTimeoutMs = 3000;

        // How often the party is re-read while walking to catch a poison faint. Each tick costs a
        // party read plus decrypt, so this is deliberately far slower than the battle poll.
        private const int FaintPollIntervalMs = 1000;

        // Gap between a candidate poison event and the read that confirms it. The walk macro keeps
        // running through this window -- nothing is interrupted until both reads agree.
        private const int PoisonConfirmDelayMs = 250;

        // Tile directly below the nurse's counter, where the heal dialogue is started. Every HGSS
        // pokemon center is its own map id but reuses one interior layout (internal name *PC0101),
        // so this single tile is correct in all of them -- the same reason the graph puts every
        // center's entrance node at (8,19).
        private const int PokemonCenterHealX = 8;
        private const int PokemonCenterHealY = 13;

        // How a walk macro ended.
        private enum SegmentOutcome
        {
            Completed,
            Battle,
            PoisonEvent,
        }

        public NavigatorEvent(PathfindingService pathfindingService, PokemonStateReader reader)
        {
            this.pathfindingService = pathfindingService;
            this.reader = reader;
        }

        public override async Task<GameState> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            EventState topState = state.State.Peek();

            if (topState == EventState.GoToPokemonCenter)
                await GoToPokemonCenterAsync(state, cancellationToken);
            else if (topState == EventState.HealAtPokemonCenter)
                await HealAtPokemonCenterAsync(state, cancellationToken);
            else if (topState == EventState.ReturnToStart)
                await ReturnToStartAsync(state, cancellationToken);

            return state;
        }

        private async Task GoToPokemonCenterAsync(GameState state, CancellationToken cancellationToken)
        {
            await WaitForOverworldReadyAsync(state.Bot, cancellationToken);
            if (state.NavigationPath is null)
            {
                var mapId = (int)await state.Bot.GetMapId();
                var x = await state.Bot.GetTrainerXPos();
                var y = await state.Bot.GetTrainerYPos();
                var facing = await state.Bot.GetFacing();
                var nodePath = await pathfindingService.FindClosestPokemonCenter(mapId, x, y, new Capability(), facing);
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

        private async Task HealAtPokemonCenterAsync(GameState state, CancellationToken cancellationToken)
        {
            // Navigation has already walked us onto the counter tile.
            string macro = MacroRegistry.Instance.GetMacro(GameFlavor.HGSS, "HealPokemon");
            state.Bot.RunMacroString(macro);
            await state.Bot.WaitForMacroAsync(cancellationToken);
            state.SwapState(EventState.ReturnToStart);
        }

        private async Task ReturnToStartAsync(GameState state, CancellationToken cancellationToken)
        {
            await WaitForOverworldReadyAsync(state.Bot, cancellationToken);
            if (state.NavigationPath is null)
            {
                var mapId = (int)await state.Bot.GetMapId();
                var x = await state.Bot.GetTrainerXPos();
                var y = await state.Bot.GetTrainerYPos();
                var facing = await state.Bot.GetFacing();
                var nodePath = await pathfindingService.FindNodePath(
                    mapId, x, y,
                    state.StartMapId, state.StartX, state.StartY,
                    new Capability(), facing);
                if (nodePath is null)
                { 
                    state.SwapState(EventState.Complete);
                    return;
                }
                state.NavigationPath = nodePath.ToList();
                state.NavigationPathIndex = 0;
            }

            bool battleOccurred = await ExecuteNavigationAsync(state, cancellationToken);

            if (battleOccurred)
            {
                state.State.Push(EventState.BattleStart);
            }
            else
            {
                ClearNavigation(state, "arrived back at start");
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
            var partyAtStart = await ReadPartySnapshotAsync(state.Bot);

            while (state.NavigationPathIndex < path.Count - 1)
            {
                var toNode = path[state.NavigationPathIndex + 1].Node;

                var mapId = (int)await state.Bot.GetMapId();
                var x = await state.Bot.GetTrainerXPos();
                var y = await state.Bot.GetTrainerYPos();
                var facing = await state.Bot.GetFacing();

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
                    var portalTile = pathfindingService.GetAdjacentPortalTarget(mapId, toNode.X, toNode.Y, x, y);
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
                string? macro = pathfindingService.Pathfind(mapId, x, y, destX, destY, new Capability(), facing, destBotMapId, overshoot);
                Console.WriteLine($"[Navigator] Macro: {(macro ?? "null")}");

                if (macro is not null)
                {
                    var outcome = await ExecuteSegmentMacroAsync(state, macro, partyAtStart, cancellationToken);
                    if (outcome == SegmentOutcome.Battle)
                        return true;
                    if (outcome == SegmentOutcome.PoisonEvent)
                    {
                        await ClearFaintTextAsync(state, cancellationToken);
                        // The mon that just resolved is now fainted or no longer poisoned, so
                        // re-snapshotting stops it from tripping the poll again -- each event is
                        // handled exactly once.
                        partyAtStart = await ReadPartySnapshotAsync(state.Bot);
                        // Retry this same segment: the walk stopped somewhere short of the node, so
                        // the loop re-reads our real position and recomputes the A* from there.
                        continue;
                    }
                }

                // After a portal crossing, poll until we're on the destination map
                // and coordinates are valid.
                var mapIdAfter = (int)await state.Bot.GetMapId();
                if (toNodeIsPortal)
                {
                    var warpDest = path[state.NavigationPathIndex + 2].Node;
                    await WaitForWarpAsync(state.Bot, mapIdAfter, warpDest.X, warpDest.Y, cancellationToken);
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

            Task first = await Task.WhenAny(macroTask, battleTask, poisonTask);

            cancellationToken.ThrowIfCancellationRequested();

            cts.Cancel();

            if (ReferenceEquals(first, battleTask) || ReferenceEquals(first, poisonTask))
            {
                state.Bot.ClearMacro();
                await state.Bot.WaitForMacroAsync(cancellationToken);
                return ReferenceEquals(first, battleTask) ? SegmentOutcome.Battle : SegmentOutcome.PoisonEvent;
            }

            return SegmentOutcome.Completed;
        }

        // Pushes past the field poison dialogue so the overworld accepts input again.
        private async Task ClearFaintTextAsync(GameState state, CancellationToken cancellationToken)
        {
            Console.WriteLine("[Navigator] Running ClearFaintText macro");
            string macro = MacroRegistry.Instance.GetMacro(state.GameFlavor, "ClearFaintText");
            state.Bot.RunMacroString(macro);
            await state.Bot.WaitForMacroAsync(cancellationToken);
        }

        // Party members that could still produce a field poison message, keyed by PID rather than
        // slot so a party reorder can't be read as a state change.
        private record PartySnapshot(HashSet<uint> Alive, HashSet<uint> Poisoned);

        // A party member whose state says a field poison tick just resolved.
        private record PoisonEvent(uint Pid, string Name, int Hp, int MaxHp, byte Status, string Reason)
        {
            public override string ToString() => $"{Name} pid={Pid:X8} hp={Hp}/{MaxHp} status=0x{Status:X2} ({Reason})";
        }

        private async Task<PartySnapshot> ReadPartySnapshotAsync(IBot bot)
        {
            var party = await reader.GetPartyState(bot);
            var alive = new HashSet<uint>();
            var poisoned = new HashSet<uint>();
            var poisonedNames = new List<string>();
            for (int i = 0; i < party.PartySize; i++)
            {
                var pokemon = party.PartyPokemon[i];
                if (pokemon is null)
                    continue;
                if (pokemon.HP > 0)
                    alive.Add(pokemon.PID);
                if (pokemon.IsPoisoned)
                {
                    poisoned.Add(pokemon.PID);
                    poisonedNames.Add($"{pokemon.Name} (0x{pokemon.Status:X2})");
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
            while (true)
            {
                // Cancels out of the loop once another racer wins, so a losing poll can never
                // complete successfully and be mistaken for a poison event.
                await Task.Delay(FaintPollIntervalMs, cancellationToken);
                var candidate = FindPoisonEvent(await reader.GetPartyState(bot), partyAtStart);
                if (candidate is null)
                    continue;

                await Task.Delay(PoisonConfirmDelayMs, cancellationToken);
                var confirm = FindPoisonEvent(await reader.GetPartyState(bot), partyAtStart);
                if (confirm is not null && confirm.Pid == candidate.Pid && confirm.Reason == candidate.Reason)
                {
                    Console.WriteLine($"[Navigator] Poison event confirmed: {confirm}");
                    return;
                }

                Console.WriteLine($"[Navigator] Discarded unconfirmed poison event: saw {candidate}, re-read gave {(confirm is null ? "nothing" : confirm.ToString())}");
            }
        }

        private async Task WaitForWarpAsync(IBot bot, int destMapId, int? destX, int? destY, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if ((int)await bot.GetMapId() == destMapId)
                    break;
                await Task.Delay(Constant.PollIntervalMs, cancellationToken);
            }
            if (destX.HasValue && destY.HasValue)
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var x = await bot.GetTrainerXPos();
                    var y = await bot.GetTrainerYPos();
                    if (Math.Abs(x - destX.Value) <= 1 && Math.Abs(y - destY.Value) <= 1)
                        break;
                    await Task.Delay(Constant.PollIntervalMs, cancellationToken);
                }
            }
            bot.RunMacroString("W.60");
            await bot.WaitForMacroAsync(cancellationToken);
        }

        // After a battle the overworld coordinates read (0,0) for a moment while the field
        // fades back in. Wait until they populate so we don't pathfind from a bogus origin.
        private async Task WaitForOverworldReadyAsync(IBot bot, CancellationToken cancellationToken)
        {
            var sw = Stopwatch.StartNew();
            while (!cancellationToken.IsCancellationRequested && sw.ElapsedMilliseconds < OverworldReadyTimeoutMs)
            {
                var x = await bot.GetTrainerXPos();
                var y = await bot.GetTrainerYPos();
                // Neither coord can be negative. An edge tile (one == 0) is valid, but both-zero
                // and any negative are the not-loaded values (e.g. (0,0), (0,-1)).
                if (x >= 0 && y >= 0 && (x > 0 || y > 0))
                    return;
                await Task.Delay(Constant.PollIntervalMs, cancellationToken);
            }
        }

        private async Task WaitForBattleAsync(IBot bot, CancellationToken cancellationToken)
        {
            byte battleValue = await bot.GetBattleValue();
            while (battleValue != 0x41 && battleValue != 0x97 && battleValue != 0xC0
                   && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(Constant.PollIntervalMs, cancellationToken);
                battleValue = await bot.GetBattleValue();
            }
        }
    }
}
