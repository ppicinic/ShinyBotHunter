using ShinyBotHunter.Bot;
using ShinyBotHunter.Pathfinding;

namespace ShinyBotHunter.BotEvents
{
    public class NavigatorEvent : AbstractBotEvent<GameState>
    {
        private const string WarpWaitMacro = "W.360";

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
                state.SwapState(EventState.HealAtPokemonCenter);
            }
        }

        private async Task HealAtPokemonCenterAsync(GameState state, CancellationToken cancellationToken)
        {
            // TODO: implement heal macro
            // Dismiss the nurse's automatic greeting so movement is unblocked before navigating out.
            state.Bot.RunMacroString("T.A;W.90;T.A;W.90");
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
                var fromNode = path[state.NavigationPathIndex].Node;
                var toNode = path[state.NavigationPathIndex + 1].Node;

                // A warp (door, cave entrance) occurs when consecutive nodes have different map IDs.
                bool isWarp = fromNode.MapId != toNode.MapId;

                var mapId = (int)await state.Bot.GetMapId();
                var x = await state.Bot.GetTrainerXPos();
                var y = await state.Bot.GetTrainerYPos();
                var facing = await state.Bot.GetFacing();

                // For a warp, navigate to the warp-entrance node on the current map;
                // for a regular segment, navigate to the destination node.
                int destX = isWarp ? fromNode.X : toNode.X;
                int destY = isWarp ? fromNode.Y : toNode.Y;

                // For warp segments, embed the destination map ID in the final M. command so
                // it stops the moment the bot arrives on the new map (door entered), preventing
                // an infinite loop caused by coordinates resetting on the new map.
                ushort? destMapIdCheck = isWarp ? (ushort)toNode.MapId : null;
                string? macro = pathfindingService.Pathfind(mapId, x, y, destX, destY, new Capability(), facing, destMapIdCheck);

                if (macro is not null)
                {
                    bool battleOccurred = await ExecuteSegmentMacroAsync(state, macro, cancellationToken);
                    if (battleOccurred)
                        return true;
                }

                // Apply the warp wait if the segment was expected to warp OR if the map
                // actually changed (e.g. the door triggered before coordinates were reached).
                var mapIdAfter = (int)await state.Bot.GetMapId();
                if (isWarp || mapIdAfter != mapId)
                {
                    state.Bot.RunMacroString(WarpWaitMacro);
                    await state.Bot.WaitForMacroAsync(cancellationToken);
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

            cts.Cancel(); // stop the losing task

            if (ReferenceEquals(first, battleTask))
            {
                state.Bot.ClearMacro();
                await state.Bot.WaitForMacroAsync(cancellationToken);
                return true;
            }

            return false;
        }

        private async Task WaitForBattleAsync(IBot bot, CancellationToken cancellationToken)
        {
            byte battleValue = await bot.GetBattleValue();
            while (battleValue != 0x41 && battleValue != 0x97 && battleValue != 0xC0
                   && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(166, cancellationToken);
                battleValue = await bot.GetBattleValue();
            }
        }
    }
}
