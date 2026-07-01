using ShinyBotHunter.Bot;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Util;

namespace ShinyBotHunter.BotEvents
{
    public class NavigatorEvent : AbstractBotEvent<GameState>
    {
        public override EventState EventState => throw new NotImplementedException();

        private readonly PathfindingService pathfindingService;

        public NavigatorEvent(PathfindingService pathfindingService)
        {
            this.pathfindingService = pathfindingService;
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
                state.NavigationPath = null;
                state.SwapState(EventState.HealAtPokemonCenter);
            }
        }

        private async Task HealAtPokemonCenterAsync(GameState state, CancellationToken cancellationToken)
        {
            string macro = MacroRegistry.Instance.GetMacro(GameFlavor.HGSS, "HealPokemon");
            state.Bot.RunMacroString(macro);
            await state.Bot.WaitForMacroAsync(cancellationToken);
            state.SwapState(EventState.ReturnToStart);
        }

        private async Task ReturnToStartAsync(GameState state, CancellationToken cancellationToken)
        {
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
                state.NavigationPath = null;
                state.SwapState(EventState.DecideOverworldAction);
            }
        }

        // Walks the NodePath segment by segment, running A* for each leg.
        // Returns true if a battle was encountered (NavigationPathIndex is left at the
        // in-progress segment so the A* can be recomputed on resume).
        private async Task<bool> ExecuteNavigationAsync(GameState state, CancellationToken cancellationToken)
        {
            var path = state.NavigationPath!;

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
                    bool battleOccurred = await ExecuteSegmentMacroAsync(state, macro, cancellationToken);
                    if (battleOccurred)
                        return true;
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

        // Runs macro and races it against battle detection.
        // Returns true if a battle started before the macro finished.
        private async Task<bool> ExecuteSegmentMacroAsync(GameState state, string macro, CancellationToken cancellationToken)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            state.Bot.RunMacroString(macro);

            Task macroTask = state.Bot.WaitForMacroAsync(cancellationToken);
            Task battleTask = WaitForBattleAsync(state.Bot, cts.Token);

            Task first = await Task.WhenAny(macroTask, battleTask);

            cancellationToken.ThrowIfCancellationRequested();

            cts.Cancel();

            if (ReferenceEquals(first, battleTask))
            {
                state.Bot.ClearMacro();
                await state.Bot.WaitForMacroAsync(cancellationToken);
                return true;
            }

            return false;
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
