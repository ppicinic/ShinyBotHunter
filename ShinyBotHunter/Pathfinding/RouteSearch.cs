using ShinyBotHunter.Games;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Util;
using System.Buffers.Binary;

namespace ShinyBotHunter.Pathfinding
{
    // A route RouteSearch found. Steps: each tile the player ends a step on, the direction pressed and the field action
    // the step needs. Segments: each M macro with the map and tile it ends on. LiveTriggers: the live triggers the route
    // crosses (FR-19).
    public sealed record Route(
        int Cost,
        IReadOnlyList<(int Map, int X, int Y, Direction Direction, MoveAction Action)> Steps,
        IReadOnlyList<(string Macro, int Map, int X, int Y)> Segments,
        IReadOnlyList<(int Map, int X, int Y)> LiveTriggers);

    // The Ruby and Sapphire search (FR-4a to FR-8): Dijkstra tile by tile across maps, with HGSS's kinds and costs
    // (PathfindingService.GetNeighboringMapPoint) on Data/RS. A separate class because the cross-map state (map and
    // elevation) and the edges between maps fit neither HGSS's per-map A* nor its graph (FR-7, plan of #426).
    // Rules and costs: docs/research/rs-navigation-and-training.md, "The generated data", "HMs in the data and the
    // search" and "Search and macros".
    public sealed class RouteSearch
    {
        private static readonly Direction[] Directions = [Direction.Up, Direction.Down, Direction.Left, Direction.Right];

        private readonly MapService maps;
        private readonly IPointerService addresses;
        private readonly GameFlavor flavor;

        // Dijkstra's visited key. Boulders: empty while the current map's boulders stand where the data puts them,
        // otherwise every boulder's position on it, two chars (x, y) per boulder in the data's order.
        private readonly record struct State(int Map, int X, int Y, int Elevation, bool Surfing, string Boulders);

        // Direction and Action are the step that reached State; Parent is the node before it.
        private sealed record Node(State State, int Cost, Direction Direction, MoveAction Action, bool LiveTrigger, Node? Parent);

        public RouteSearch(MapService maps, IPointerService addresses, GameFlavor flavor)
        {
            this.maps = maps;
            this.addresses = addresses;
            this.flavor = flavor;
        }

        // flags: the 0x120-byte flag block at gSaveBlock1 + 0x1220; vars: the 0x200-byte variable block at + 0x1340,
        // exactly as read. isGoal(map, x, y) is checked on each tile as it leaves the queue, so the first goal reached is
        // the cheapest. Null when no route exists.
        public Route? Find(int map, int x, int y, int elevation, Func<int, int, int, bool> isGoal, byte[] flags, byte[] vars, Capability capability)
        {
            // HGSS: the start counts as surfing when its tile is water. No facing input, so the first step never turns
            // (the spike's prototype, as walked on the emulator).
            Node start = new(new State(map, x, y, elevation, maps.GetMap(map, true)[y, x] == Constant.MAP_Surf, ""), 0, Direction.None, MoveAction.None, false, null);
            Dictionary<State, int> best = new() { [start.State] = 0 };
            // Ties break on (cost, map, x, y, elevation), the prototype's order, so the search finds the route it walked.
            PriorityQueue<Node, (int, int, int, int, int)> queue = new();
            queue.Enqueue(start, (0, map, x, y, elevation));
            while (queue.TryDequeue(out Node? node, out _))
            {
                State state = node.State;
                if (node.Cost > best[state])
                {
                    continue;
                }
                if (isGoal(state.Map, state.X, state.Y))
                {
                    return ToRoute(node);
                }
                foreach (Node next in Neighbours(node, flags, vars, capability))
                {
                    if (!best.TryGetValue(next.State, out int known) || next.Cost < known)
                    {
                        best[next.State] = next.Cost;
                        queue.Enqueue(next, (next.Cost, next.State.Map, next.State.X, next.State.Y, next.State.Elevation));
                    }
                }
            }
            return null;
        }

        private IEnumerable<Node> Neighbours(Node node, byte[] flags, byte[] vars, Capability capability)
        {
            State state = node.State;
            Map map = maps.GetMap(state.Map, true);
            foreach (Direction direction in Directions)
            {
                (int X, int Y) ahead = Ahead(state.X, state.Y, direction);
                if (ahead.X < 0 || ahead.X >= map.Width || ahead.Y < 0 || ahead.Y >= map.Height)
                {
                    continue;
                }
                // HGSS: a step that turns costs double.
                int factor = node.Direction != Direction.None && node.Direction != direction ? 2 : 1;
                Node? next = Enter(node, direction, factor, state.Map, ahead.X, ahead.Y, false, flags, vars, capability);
                if (next is not null)
                {
                    yield return next;
                }
            }
        }

        // The step from node onto (x, y) of mapId in direction. jumped: the player is carried there by a ledge or a
        // waterfall, which the game does without the elevation check (ShouldJumpLedge before the collision in
        // CheckForObjectEventCollision, pokeruby src/field_player_avatar.c).
        private Node? Enter(Node node, Direction direction, int factor, int mapId, int x, int y, bool jumped, byte[] flags, byte[] vars, Capability capability)
        {
            State state = node.State;
            Map map = maps.GetMap(mapId, true);
            string boulders = mapId == state.Map ? state.Boulders : "";
            int boulder = mapId == state.Map ? BoulderAt(map, boulders, x, y) : -1;
            if (boulder >= 0)
            {
                return Push(node, map, boulders, boulder, direction, x, y, capability);
            }

            byte kind = map[y, x];
            int cost;
            MoveAction action = MoveAction.None;
            bool surfing = false;
            bool live = false;
            switch (kind)
            {
                case Constant.MAP_Free:
                case Constant.MAP_Portal1: // an arrow warp's mat, walked over in another direction
                    cost = 1;
                    break;
                case Constant.MAP_Grass:
                    cost = 3;
                    break;
                case Constant.MAP_Surf:
                    if (!capability.CanSurf)
                    {
                        return null;
                    }
                    // HGSS: 10 to start surfing, 3 a tile after.
                    cost = state.Surfing ? 3 : 10;
                    action = MoveAction.Surf;
                    surfing = true;
                    break;
                case Constant.MAP_RightLedge:
                case Constant.MAP_LeftLedge:
                case Constant.MAP_DownLedge:
                    // HGSS: a ledge only in its own direction, landing on the tile beyond it.
                    if (direction != (kind == Constant.MAP_RightLedge ? Direction.Right : kind == Constant.MAP_LeftLedge ? Direction.Left : Direction.Down))
                    {
                        return null;
                    }
                    return Beyond(node, direction, factor, mapId, map, x, y, flags, vars, capability);
                case Constant.MAP_Waterfall:
                    if (!capability.CanWaterfall)
                    {
                        return null;
                    }
                    // HGSS: sideways a waterfall costs 1; Up or Down it carries the player on for 10 a tile.
                    if (direction == Direction.Left || direction == Direction.Right)
                    {
                        cost = 1;
                        action = MoveAction.Waterfall;
                        surfing = true;
                        break;
                    }
                    Node? climbed = Beyond(node, direction, factor, mapId, map, x, y, flags, vars, capability);
                    return climbed is null ? null : climbed with { Cost = climbed.Cost + 10, Action = MoveAction.Waterfall };
                case Constant.MAP_Cut:
                    if (!capability.CanCut)
                    {
                        return null;
                    }
                    cost = 10;
                    action = MoveAction.Cut;
                    break;
                case Constant.MAP_RockSmash:
                    // Always a rock: the side file has no flag for a rock the story removes (plan of #426, decision 1).
                    if (!capability.CanRockSmash)
                    {
                        return null;
                    }
                    cost = 30;
                    action = MoveAction.RockSmash;
                    break;
                case Constant.MAP_WalkingTrainer:
                    // HGSS: an NPC's range costs 10, three times that while surfing.
                    cost = state.Surfing ? 30 : 10;
                    surfing = state.Surfing;
                    action = state.Surfing ? MoveAction.Surf : MoveAction.None;
                    break;
                case Constant.MAP_FlaggedNpc:
                    // FR-5: blocked while any object on the tile stands, i.e. its flag is clear or did not resolve.
                    if (!Hidden(map, x, y, flags))
                    {
                        return null;
                    }
                    cost = 1;
                    break;
                case Constant.MAP_ScriptTrigger:
                    // FR-5: 200 while the trigger is live, ordinary ground otherwise.
                    live = map.Events.Triggers.Any(t => t.X == x && t.Y == y && Live(t, vars));
                    cost = live ? 200 : 1;
                    break;
                default: // 0x80 blocked, 0x0E ice and sliding floor
                    return null;
            }

            // FR-6: a tile at an elevation other than 0 or 15 takes a player at 0 or at the same elevation; water is
            // entered and left at any elevation (research, "HMs in the data and the search").
            byte elevation = map.ElevationAt(y, x);
            bool water = IsWater(kind) || IsWater(maps.GetMap(state.Map, true)[state.Y, state.X]);
            if (!jumped && !water && elevation != 0 && elevation != 15 && state.Elevation != 0 && state.Elevation != elevation)
            {
                return null;
            }
            State next = new(mapId, x, y, elevation == 15 ? state.Elevation : elevation, surfing, boulders);
            return new Node(next, node.Cost + cost * factor, direction, action, live, node);
        }

        // The tile beyond a ledge or a waterfall, entered by the same rules (HGSS's recursion in GetNeighboringMapPoint).
        private Node? Beyond(Node node, Direction direction, int factor, int mapId, Map map, int x, int y, byte[] flags, byte[] vars, Capability capability)
        {
            (int X, int Y) ahead = Ahead(x, y, direction);
            if (ahead.X < 0 || ahead.X >= map.Width || ahead.Y < 0 || ahead.Y >= map.Height)
            {
                return null;
            }
            return Enter(node, direction, factor, mapId, ahead.X, ahead.Y, true, flags, vars, capability);
        }

        // HGSS's Strength push: the player stays, the boulder moves one tile onto free ground; 10, and 40 for every push
        // after the first on the map. Never from the start, which has no facing.
        private static Node? Push(Node node, Map map, string boulders, int boulder, Direction direction, int x, int y, Capability capability)
        {
            (int X, int Y) to = Ahead(x, y, direction);
            if (!capability.CanStrength || node.Direction == Direction.None
                || to.X < 0 || to.X >= map.Width || to.Y < 0 || to.Y >= map.Height || map[to.Y, to.X] != Constant.MAP_Free)
            {
                return null;
            }
            char[] moved = (boulders == "" ? string.Concat(map.Boulders.Select(b => $"{(char)b.X}{(char)b.Y}")) : boulders).ToCharArray();
            moved[boulder * 2] = (char)to.X;
            moved[boulder * 2 + 1] = (char)to.Y;
            int cost = 10 + (boulders == "" ? 0 : 30);
            return new Node(node.State with { Boulders = new string(moved) }, node.Cost + cost, direction, MoveAction.Strength, false, node);
        }

        // The index of the boulder on (x, y), or -1.
        private static int BoulderAt(Map map, string boulders, int x, int y)
        {
            for (int i = 0; i < map.Boulders.Length; i++)
            {
                (int bx, int by) = boulders == "" ? (map.Boulders[i].X, map.Boulders[i].Y) : (boulders[i * 2], boulders[i * 2 + 1]);
                if (bx == x && by == y)
                {
                    return i;
                }
            }
            return -1;
        }

        // A flagged NPC's tile is open once every object on it is hidden: its flag resolved and set (bit id & 7 of byte
        // id >> 3 of the flag block).
        private static bool Hidden(Map map, int x, int y, byte[] flags) =>
            map.Events.Objects.Where(o => o.X == x && o.Y == y).All(o => o.Flag is int id && (flags[id >> 3] & (1 << (id & 7))) != 0);

        // A trigger is live while its variable (u16 at (var - 0x4000) * 2 of the variable block) holds its value cut to a
        // byte; a trigger whose variable did not resolve is always live.
        private static bool Live(MapTrigger trigger, byte[] vars) =>
            trigger.Var is not int id || BinaryPrimitives.ReadUInt16LittleEndian(vars.AsSpan((id - 0x4000) * 2)) == (trigger.Value & 0xFF);

        private static bool IsWater(byte kind) => kind == Constant.MAP_Surf || kind == Constant.MAP_Waterfall;

        private static (int X, int Y) Ahead(int x, int y, Direction direction) => direction switch
        {
            Direction.Up => (x, y - 1),
            Direction.Down => (x, y + 1),
            Direction.Left => (x - 1, y),
            _ => (x + 1, y),
        };

        private static Route ToRoute(Node last)
        {
            List<Node> nodes = [];
            for (Node? node = last; node is not null; node = node.Parent)
            {
                nodes.Add(node);
            }
            nodes.Reverse();
            List<Node> steps = nodes.Skip(1).ToList();
            return new Route(
                last.Cost,
                steps.Select(n => (n.State.Map, n.State.X, n.State.Y, n.Direction, n.Action)).ToList(),
                [],
                steps.Where(n => n.LiveTrigger).Select(n => (n.State.Map, n.State.X, n.State.Y)).ToList());
        }
    }
}
