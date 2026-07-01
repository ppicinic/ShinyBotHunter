using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Util;
using System.Drawing;
using System.Text;
using System.Xml.Linq;

namespace ShinyBotHunter.Pathfinding
{
    public class MapPoint
    {
        public MapPoint()
        {

        }
        public MapPoint(Direction direction)
        {
            this.direction = direction;
        }
        public int X { get; set; }
        public int Y { get; set; }
        public MapPoint? CameFrom { get; set; } = null;
        public bool InitialStrength { get; set; }
        public BoulderState[] BoulderState { get; set; } = [];
        public MoveAction ActionNeeded { get; set; } = MoveAction.None;
        public int Cost { get; set; }
        private Direction? direction = null;
        public Direction Direction { get
            {
                if(direction is not null)
                {
                    return direction.Value;
                }
                direction = Direction.None;
                if(CameFrom is null)
                {
                    return direction.Value;
                }
                if(X - CameFrom.X == 0)
                {
                    if(Y - CameFrom.Y < 0)
                    {
                        direction = Direction.Up;
                    }
                    else if(Y - CameFrom.Y > 0)
                    {
                        direction = Direction.Down;
                    }
                }
                else if(X - CameFrom.X < 0)
                {
                    direction = Direction.Left;
                }
                else if(X - CameFrom.X > 0)
                {
                    direction = Direction.Right;
                }
                return direction.Value;
            } 
        }

        public PlayerState PlayerState
        {
            get
            {
                return new PlayerState(X, Y, ActionNeeded, BoulderState) { InitialStrength = InitialStrength };
            }
        }

        internal void PushBoulder(int boulderX, int boulderY, int pushX, int pushY)
        {
            BoulderState[] newBoulders = new BoulderState[BoulderState.Length];
            for(int i = 0; i < BoulderState.Length; i++)
            {
                if (BoulderState[i].X == boulderX && BoulderState[i].Y == boulderY)
                {
                    newBoulders[i] = new BoulderState(pushX, pushY);
                }
                else
                {
                    newBoulders[i] = new BoulderState(BoulderState[i].X, BoulderState[i].Y);
                }
            }
            BoulderState = newBoulders;
        }
    }

    public enum MoveAction
    {
        None,
        Cut,
        Surf,
        RockSmash,
        Bridge,
        Waterfall,
        Whirlpool,
        Strength,
        Portal
    }

    

    public enum Direction
    {
        None = 5,
        Up = 0,
        Down = 1,
        Left = 2,
        Right = 3
    }

    public class PlayerState : IEquatable<PlayerState>
    {
        public int X { get; private set; }
        public int Y { get; private set; }
        public bool InitialStrength { get; set; } = false;
        public BoulderState[] Boulders { get; private set; }
        public MoveAction MoveAction { get; private set; }
        
        public PlayerState(int x, int y, MoveAction moveAction, BoulderState[]? boulders = null)
        {
            X = x;
            Y = y;
            MoveAction = moveAction;
            if(boulders is null)
            {
                Boulders = [];
            }
            else
            {
                Boulders = boulders;
            }
        }

        public bool Equals(PlayerState? other)
        {
            if(other is null)
            {
                return false;
            }
            if(X != other.X || Y != other.Y)
            {
                return false;
            }
            if (Boulders.Length != other.Boulders.Length)
            {
                return false;
            }
            if(MoveAction != other.MoveAction)
            {
                return false;
            }
            for(int i = 0; i < Boulders.Length; i++)
            {
                if (Boulders[i].X != other.Boulders[i].X || Boulders[i].Y != other.Boulders[i].Y)
                {
                    return false;
                }
            }
            return true;
        }

        public override int GetHashCode()
        {
            HashCode hc = new HashCode();
            hc.Add(X);
            hc.Add(Y);
            hc.Add(MoveAction);
            foreach(var b in Boulders)
            {
                hc.Add(b.X);
                hc.Add(b.Y);
            }
            return hc.ToHashCode();
        }
    }

    public class BoulderState
    {
        public int X { get; private set; }
        public int Y { get; private set; }
        public BoulderState(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [Flags]
    public enum CapabilityType
    {
        None = 0x0,
        Cut = 0x1,
        Surf = 0x2,
        RockSmash = 0x4,
        Waterfall = 0x8,
        Whirlpool = 0x10,
        Strength = 0x20,
        RockClimb = 0x40,
    }

    public class NodePoint
    {
        public NodePoint? CameFrom { get; set; }
        public int Cost { get; set; }
        public Node Node { get; set; } = null!;
    }

    public class Capability
    {
        public bool CanCut
        {
            get => (CapabilityMask & 0x1) == 0x1;
            set => CapabilityMask = value ? (CapabilityMask | 0x1) : (CapabilityMask & ~0x1);
        }

        public bool CanSurf
        {
            get => (CapabilityMask & 0x2) == 0x2;
            set => CapabilityMask = value ? (CapabilityMask | 0x2) : (CapabilityMask & ~0x2);
        }

        public bool CanRockSmash
        {
            get => (CapabilityMask & 0x4) == 0x4;
            set => CapabilityMask = value ? (CapabilityMask | 0x4) : (CapabilityMask & ~0x4);
        }

        public bool CanWaterfall
        {
            get => (CapabilityMask & 0x8) == 0x8;
            set => CapabilityMask = value ? (CapabilityMask | 0x8) : (CapabilityMask & ~0x8);
        }

        public bool CanWhirlpool
        {
            get => (CapabilityMask & 0x10) == 0x10;
            set => CapabilityMask = value ? (CapabilityMask | 0x10) : (CapabilityMask & ~0x10);
        }

        public bool CanStrength
        {
            get => (CapabilityMask & 0x20) == 0x20;
            set => CapabilityMask = value ? (CapabilityMask | 0x20) : (CapabilityMask & ~0x20);
        }

        public bool CanRockClimb
        {
            get => (CapabilityMask & 0x40) == 0x40;
            set => CapabilityMask = value ? (CapabilityMask | 0x40) : (CapabilityMask & ~0x40);
        }

        public int CapabilityMask { get; set; }
    }
    public class PathfindingService
    {
        public const int XAddrVal = 0x21DA6F4;
        public const int YAddrVal = 0x21DA6FC;
        public static readonly string XAddr = XAddrVal.ToString();
        public static readonly string YAddr = YAddrVal.ToString();
        public const uint MapAnchorAddr = 0x21D4158;
        public const uint MapNegOffset = 0x22DA4;
        private MapService mapService;
        private GraphService graphService;
        public PathfindingService(MapService mapService, GraphService graphService)
        {
            this.mapService = mapService;
            this.graphService = graphService;
        }

        public async Task<IEnumerable<NodePoint>?> FindClosestPokemonCenter(int mapId, int startX, int startY, Capability capability, byte facing)
        {
            return await FindNodePath(mapId, startX, startY, capability, facing, (x) => x.Node.IsPokemonCenter);
        }

        public async Task<IEnumerable<NodePoint>?> FindNodePath(int startMapId, int startX, int startY, int destMapId, int destX, int destY, Capability capability, byte facing, bool startInternalId = false, bool endInternalId = false)
        {
            var firstNodeTask = BuildBeginningNode(startMapId, startX, startY, capability, facing, startInternalId);
            var endNodeTask = BuildEndingNode(destMapId, destX, destY, capability, 0, endInternalId);
            await Task.WhenAll(firstNodeTask, endNodeTask);
            var startNode = await firstNodeTask;
            var extraEdges = await endNodeTask;
            var botMapId = mapService.GetMapLookups(destMapId, endInternalId)[0].BotMapId;
            var nodePoint = Djitriska(startNode, capability, (x) => x.Node.MapId == botMapId && x.Node.X == destX && x.Node.Y == destY, extraEdges);
            if(nodePoint is null)
            {
                return null;
            }
            return ReconstructPath(nodePoint);
        }

        public async Task<IEnumerable<NodePoint>?> FindNodePath(int mapId, int startX, int startY, Capability capability, byte facing, Func<NodePoint, bool> predicate)
        {
            Node start = await BuildBeginningNode(mapId, startX, startY, capability, facing);
            NodePoint? nodePoint = Djitriska(start, capability, predicate);
            if (nodePoint is null)
            {
                return null;
            }
            return ReconstructPath(nodePoint);
        }

        public NodePoint? Djitriska(Node startNode, Capability capability, Func<NodePoint, bool> predicate, Dictionary<int, Edge>? extraEdges = null)
        {
            if(extraEdges is null)
            {
                extraEdges = new Dictionary<int, Edge>();
            }
            PriorityQueue<NodePoint, int> queue = new PriorityQueue<NodePoint, int>();
            Dictionary<int, int> visited = new Dictionary<int, int>();
            visited.Add(startNode.Id, 0);
            queue.Enqueue(new NodePoint() { Node = startNode, Cost = 0 }, 0);
            while(queue.Count > 0)
            {
                NodePoint top = queue.Dequeue();
                if (predicate(top))
                {
                    return top;
                }
                IEnumerable<NodePoint> nodePoints = GetNeighoringPoints(top, capability, extraEdges);
                foreach (var nodePoint in nodePoints)
                {
                    if (!visited.ContainsKey(nodePoint.Node.Id))
                    {
                        visited.Add(nodePoint.Node.Id, nodePoint.Cost);
                        queue.Enqueue(nodePoint, nodePoint.Cost);
                    }
                    else if (nodePoint.Cost < visited[nodePoint.Node.Id])
                    {
                        visited[nodePoint.Node.Id] = nodePoint.Cost;
                        queue.Enqueue(nodePoint, nodePoint.Cost);
                    }
                }
            }
            return null;
        }

        private IEnumerable<NodePoint> GetNeighoringPoints(NodePoint point, Capability capability, Dictionary<int, Edge> extraEdges)
        {
            Dictionary<int, NodePoint> edges = new Dictionary<int, NodePoint>();
            foreach(var edge in point.Node.Edges)
            {
                AddEdge(edge, capability, edges, point);
            }
            if (extraEdges.ContainsKey(point.Node.Id))
            {
                AddEdge(extraEdges[point.Node.Id], capability, edges, point);
            }
            return edges.Values;
        }

        private void AddEdge(Edge edge, Capability capability, Dictionary<int, NodePoint> edges, NodePoint point)
        {
            if ((edge.CapabilityMask ^ capability.CapabilityMask) == capability.CapabilityMask)
            {
                if (!edges.ContainsKey(edge.ToNodeId))
                {
                    edges.Add(edge.ToNodeId, new NodePoint()
                    {
                        CameFrom = point,
                        Cost = edge.Cost + point.Cost,
                        Node = edge.ToNode
                    });
                }
                else if (edge.Cost < edges[edge.ToNodeId].Cost)
                {
                    edges[edge.ToNodeId] = new NodePoint()
                    {
                        CameFrom = point,
                        Cost = edge.Cost + point.Cost,
                        Node = edge.ToNode
                    };
                }
            }
        }

        public async Task<Dictionary<int, Edge>> BuildEndingNode(int mapId, int x, int y, Capability capability, byte facing, bool internalId = false)
        {
            List<Node> nodes = graphService.GetNodesFromMap(mapId, internalId);
            var lookup = mapService.GetMapLookups(mapId, internalId)[0];
            Node node = new Node()
            {
                Id = -2,
                MapId = lookup.BotMapId,
                IsPokemonCenter = lookup.InternalName.EndsWith("PC0101"),
                X = x,
                Y = y,
            };
            List<Task<Edge?>> tasks = new List<Task<Edge?>>();
            Dictionary<int, Edge> edges = new Dictionary<int, Edge>();
            foreach (var toNode in nodes)
            {
                tasks.Add(Task.Run(() =>
                {
                    var mapPoint = ASearch(mapId, toNode.X, toNode.Y, node.X, node.Y, capability, facing, internalId);
                    if (mapPoint is not null)
                    {
                        Edge edge = new Edge()
                        {
                            FromNodeId = toNode.Id,
                            ToNode = node,
                            ToNodeId = node.Id,
                            CapabilityMask = capability.CapabilityMask,
                            Cost = mapPoint.Cost
                        };
                        return edge;
                    }
                    return null;
                }));

            }
            await Task.WhenAll(tasks);
            foreach (var task in tasks)
            {
                var edge = await task;
                if (edge is not null)
                {
                    edges.Add(edge.FromNodeId, edge);
                }
            }
            return edges;
        }

        public async Task<Node> BuildBeginningNode(int mapId, int startX, int startY, Capability capability, byte facing, bool internalId = false)
        {
            List<Node> nodes = graphService.GetNodesFromMap(mapId, internalId);
            var lookup = mapService.GetMapLookups(mapId, internalId)[0];
            Node node = new Node()
            {
                Id = -1,
                MapId = lookup.BotMapId,
                IsPokemonCenter = lookup.InternalName.EndsWith("PC0101"),
                X = startX,
                Y = startY,
            };
            List<Task<Edge?>> tasks = new List<Task<Edge?>>();
            foreach (var toNode in nodes)
            {
                tasks.Add(Task.Run(() => 
                { 
                    var mapPoint = ASearch(mapId, node.X, node.Y, toNode.X, toNode.Y, capability, facing, internalId);
                    if (mapPoint is not null)
                    {
                        Edge edge = new Edge()
                        {
                            FromNodeId = node.Id,
                            ToNode = toNode,
                            ToNodeId = toNode.Id,
                            CapabilityMask = capability.CapabilityMask,
                            Cost = mapPoint.Cost
                        };
                        return edge;
                    }
                    return null;
                }));
                
            }
            await Task.WhenAll(tasks);
            foreach(var task in tasks)
            {
                var edge = await task;
                if(edge is not null) 
                { 
                    node.Edges.Add(edge);
                }
            }
            return node;
        }

        public string? Pathfind(int mapId, int startX, int startY, int destX, int destY, Capability capability, byte facing, int? destBotMapId = null, bool overshootFinalStep = false)
        {
            MapPoint? point = ASearch(mapId, startX, startY, destX, destY, capability, facing);
            if(point is null)
            {
                return null;
            }
            var path = ReconstructPath(point);
            // When the final move steps through a warp, hand the emulator every raw game
            // map id the destination internal map could resolve to so it can stop the move
            // the moment any of them becomes live (the warp firing).
            IReadOnlyList<int>? destGameMapIds = destBotMapId.HasValue
                ? mapService.GetGameMapIds(destBotMapId.Value)
                : null;
            return CreateMacro(path, destGameMapIds, overshootFinalStep);
        }

        // Returns the first graph node on fromMapRawId that has a cross-map edge leading
        // to destMapRawId. Used as a fallback when the navigation path has a direct
        // cross-map edge with no intermediate portal node recorded.
        public Node? FindPortalToMap(int fromMapRawId, int destMapRawId)
        {
            var nodes = graphService.GetNodesFromMap(fromMapRawId);
            return nodes.FirstOrDefault(n => n.Edges.Any(e => e.ToNode.MapId == destMapRawId));
        }

        // Scans the 4 tiles adjacent to (nodeX, nodeY) for a portal tile, preferring the
        // one in the direction of travel from the player's current position (x, y).
        public (int X, int Y)? GetAdjacentPortalTarget(int mapId, int nodeX, int nodeY, int x, int y)
        {
            Map map = mapService.GetMap(mapId);
            (int X, int Y)[] candidates =
            [
                (nodeX,     nodeY - 1),
                (nodeX,     nodeY + 1),
                (nodeX - 1, nodeY),
                (nodeX + 1, nodeY),
            ];

            (int X, int Y)? best = null;
            int bestDist = int.MaxValue;

            foreach (var c in candidates)
            {
                if (c.Y < 0 || c.Y >= map.Height || c.X < 0 || c.X >= map.Width)
                    continue;
                byte tile = map[c.Y, c.X];
                if (tile != Constant.MAP_Portal1 && tile != Constant.MAP_Portal2)
                    continue;
                // Prefer the tile closest to the direction of travel (minimise Manhattan distance from player).
                int dist = Math.Abs(c.X - x) + Math.Abs(c.Y - y);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = c;
                }
            }

            return best;
        }

        //public List<MapPoint> GetAllWarpWeights(int mapId, int startX, int startY, Capability capability, byte facing)
        //{
        //    List<MapPoint> mapPoints = new List<MapPoint>();
        //    List<Task<MapPoint?>> tasks = new List<Task<MapPoint?>>();
        //    Map map = mapService.GetMap(mapId);
        //    map
        //    return mapPoints;
        //}
        
        public MapPoint? ASearch(int mapId, int startX, int startY, int destX, int destY, Capability capability, byte facing, bool internalId = false)
        {
            Map map = mapService.GetMap(mapId, internalId);
            Dictionary<PlayerState, int> visited = new Dictionary<PlayerState, int>();
            var initBoulderState = CreateBoulderState(map.Boulders);
            visited.Add(new PlayerState(startX, startY, map[startY, startX] == Constant.MAP_Surf ? MoveAction.Surf : MoveAction.None, initBoulderState), 0);
            PriorityQueue<MapPoint, int> queue = new PriorityQueue<MapPoint, int>();
            Direction startDirection = (Direction)facing;
            queue.Enqueue(new MapPoint(startDirection)
            {
                X = startX,
                Y = startY,
                Cost = 0,
                ActionNeeded = map[startY,startX] == Constant.MAP_Surf ? MoveAction.Surf : MoveAction.None,
                BoulderState = initBoulderState,
            }, Math.Abs(destY - startY) + Math.Abs(destX - startX));
            while(queue.Count > 0)
            {
                MapPoint top = queue.Dequeue();
                if(top.X == destX && top.Y == destY)
                {
                    return top;

                }
                var points = GetNeighboringMapPoints(map, top, capability);
                foreach(var point in points)
                {
                    var state = point.PlayerState;
                    if (!visited.ContainsKey(state)){
                        visited.Add(state, point.Cost);
                        queue.Enqueue(point, point.Cost + Math.Abs(destY - point.Y) + Math.Abs(destX - point.X));
                    }
                    else if (point.Cost < visited[state])
                    {
                        visited[state] = point.Cost;
                        queue.Enqueue(point, point.Cost + Math.Abs(destY - point.Y) + Math.Abs(destX - point.X));
                    }
                }
            }

            return null; // no solution
        }

        private BoulderState[] CreateBoulderState(BoulderData[] boulders)
        {
            BoulderState[] state = new BoulderState[boulders.Length];
            for(int i = 0; i < boulders.Length; i++)
            {
                state[i] = new BoulderState(boulders[i].X, boulders[i].Y);
            }
            return state;
        }

        private string FormatMove(string direction, int val, string addr, IReadOnlyList<int>? destGameMapIds)
        {
            // When destGameMapIds is set this is a warp-crossing move: the emulator stops it
            // the moment the live map id matches any of these destination game map ids.
            if (destGameMapIds is not null && destGameMapIds.Count > 0)
                return $"M.{direction}.{val}.{addr}.{MapAnchorAddr}.{MapNegOffset}." + string.Join(".", destGameMapIds);
            return $"M.{direction}.{val}.{addr}";
        }

        public string? CreateMacro(IEnumerable<MapPoint> mapPoints, IReadOnlyList<int>? destGameMapIds = null, bool overshootFinalStep = false)
        {
            if(mapPoints.Count() < 2)
            {
                return null;
            }
            bool initialStrength = false;
            StringBuilder sb = new StringBuilder();
            var enumerator = mapPoints.GetEnumerator();
            int curX = -1;
            int curY = -1;
            Direction direction = Direction.None;
            MoveAction moveAction = MoveAction.None;
            if (!enumerator.MoveNext())
            {
                return "";
            }
            curX = enumerator.Current.X;
            curY = enumerator.Current.Y;
            int startX = curX;
            int startY = curY;
            direction = enumerator.Current.Direction;
            moveAction = enumerator.Current.ActionNeeded;
            bool first = true;
            while (enumerator.MoveNext())
            {
                if(enumerator.Current.ActionNeeded == MoveAction.Strength)
                {
                    if (first)
                    {
                        first = false;
                    }
                    else
                    {
                        sb.Append(";");
                    }
                    if (!(startX == curX && startY == curY))
                    {
                        //add action to string
                        string directionAddr = direction == Direction.Up || direction == Direction.Down ? YAddr : XAddr;
                        int val = direction == Direction.Up || direction == Direction.Down ? curY : curX;
                        sb.Append(FormatMove(direction.ToString(), val, directionAddr, null) + ";");
                        startX = curX;
                        startY = curY;
                    }
                    if (enumerator.Current.Direction != direction)
                    {
                        sb.Append($"T.{enumerator.Current.Direction};"); // face the surf tile if not already
                    }
                    if (!initialStrength)
                    {
                        initialStrength = true;
                        sb.Append("T.A;W.70;T.A;W.60;T.A;W.70;T.A;W.195;T.A;W.5;");
                    }
                    sb.Append($"T.{enumerator.Current.Direction.ToString()}");
                }
                if (enumerator.Current.ActionNeeded == MoveAction.Whirlpool)
                {
                    if (first)
                    {
                        first = false;
                    }
                    else
                    {
                        sb.Append(";");
                    }
                    if (!(startX == curX && startY == curY))
                    {
                        //add action to string
                        string directionAddr = direction == Direction.Up || direction == Direction.Down ? YAddr : XAddr;
                        int val = direction == Direction.Up || direction == Direction.Down ? curY : curX;
                        sb.Append(FormatMove(direction.ToString(), val, directionAddr, null) + ";");
                        startX = curX;
                        startY = curY;
                    }
                    if (enumerator.Current.Direction != direction)
                    {
                        sb.Append($"T.{enumerator.Current.Direction};"); // face the surf tile if not already
                    }
                    sb.Append("W.5;T.A;W.100;T.A;W.70;T.A;W.5");
                    curX = enumerator.Current.X;
                    curY = enumerator.Current.Y;
                    direction = enumerator.Current.Direction;
                    enumerator.MoveNext();
                }
                if (enumerator.Current.ActionNeeded == MoveAction.Waterfall)
                {

                    if (!(startX == curX && startY == curY))
                    {
                        if (first)
                        {
                            first = false;
                        }
                        else
                        {
                            sb.Append(";");
                        }
                        //add action to string
                        string directionAddr = direction == Direction.Up || direction == Direction.Down ? YAddr : XAddr;
                        int val = direction == Direction.Up || direction == Direction.Down ? curY : curX;
                        sb.Append(FormatMove(direction.ToString(), val, directionAddr, null));
                        startX = curX;
                        startY = curY;
                    }
                    if (enumerator.Current.Direction == Direction.Up)
                    {
                        if (first)
                        {
                            first = false;
                        }
                        else
                        {
                            sb.Append(";");
                        }
                        if (enumerator.Current.Direction != direction)
                        {
                            sb.Append($"T.{enumerator.Current.Direction};"); // face the surf tile if not already
                        }
                        sb.Append("W.5;T.A;W.100;T.A;W.75;T.A;W.5");
                        curX = enumerator.Current.X;
                        curY = enumerator.Current.Y;
                        direction = enumerator.Current.Direction;
                        enumerator.MoveNext();
                    }
                }
                if (enumerator.Current.ActionNeeded == MoveAction.RockSmash)
                {
                    if (first)
                    {
                        first = false;
                    }
                    else
                    {
                        sb.Append(";");
                    }
                    if (!(startX == curX && startY == curY))
                    {
                        //add action to string
                        string directionAddr = direction == Direction.Up || direction == Direction.Down ? YAddr : XAddr;
                        int val = direction == Direction.Up || direction == Direction.Down ? curY : curX;
                        sb.Append(FormatMove(direction.ToString(), val, directionAddr, null) + ";");
                        startX = curX;
                        startY = curY;
                    }
                    if (enumerator.Current.Direction != direction)
                    {
                        sb.Append($"T.{enumerator.Current.Direction};"); // face the surf tile if not already
                    }
                    sb.Append("W.5;T.A;W.100;T.A;W.70;T.A;W.350;T.A;W.210;T.A;W.5");
                }
                if (enumerator.Current.ActionNeeded == MoveAction.Cut)
                {
                    if (first)
                    {
                        first = false;
                    }
                    else
                    {
                        sb.Append(";");
                    }
                    if (!(startX == curX && startY == curY))
                    {
                        //add action to string
                        string directionAddr = direction == Direction.Up || direction == Direction.Down ? YAddr : XAddr;
                        int val = direction == Direction.Up || direction == Direction.Down ? curY : curX;
                        sb.Append(FormatMove(direction.ToString(), val, directionAddr, null) + ";");
                    }
                    if (enumerator.Current.Direction != direction)
                    {
                        sb.Append($"T.{enumerator.Current.Direction};"); // face the surf tile if not already
                    }
                    sb.Append("W.5;T.A;W.100;T.A;W.65;T.A;W.5");
                }
                if(enumerator.Current.ActionNeeded == MoveAction.Surf && (moveAction != MoveAction.Surf && moveAction != MoveAction.Waterfall && moveAction != MoveAction.Whirlpool))
                {
                    if (first)
                    {
                        first = false;
                    }
                    else
                    {
                        sb.Append(";");
                    }
                    if (!(startX == curX && startY == curY))
                    {
                        //add action to string
                        string directionAddr = direction == Direction.Up || direction == Direction.Down ? YAddr : XAddr;
                        int val = direction == Direction.Up || direction == Direction.Down ? curY : curX;
                        sb.Append(FormatMove(direction.ToString(), val, directionAddr, null) + ";");
                        startX = curX;
                        startY = curY;
                    }
                    if (enumerator.Current.Direction != direction)
                    {
                        sb.Append($"T.{enumerator.Current.Direction};"); // face the surf tile if not already
                    }
                    direction = enumerator.Current.Direction;
                    moveAction = MoveAction.Surf;
                    sb.Append($"W.5;T.A;W.90;T.A;W.65;T.A");
                    //unsure this is needed
                    curX = enumerator.Current.X;
                    curY = enumerator.Current.Y;
                    enumerator.MoveNext();
                }
                if (enumerator.Current.Direction != direction)
                {
                    
                    string directionAddr = direction == Direction.Up || direction == Direction.Down ? YAddr : XAddr;
                    int val = direction == Direction.Up || direction == Direction.Down ? curY : curX;
                    if (!(startX == curX && startY == curY))
                    {
                        //add action to string
                        if (first)
                        {
                            first = false;
                        }
                        else
                        {
                            sb.Append(";");
                        }
                        sb.Append(FormatMove(direction.ToString(), val, directionAddr, null));
                        startX = curX;
                        startY = curY;
                    }

                    curX = enumerator.Current.X;
                    curY = enumerator.Current.Y;
                    direction = enumerator.Current.Direction;
                    moveAction = enumerator.Current.ActionNeeded;
                }
                else
                {
                    curX = enumerator.Current.X;
                    curY = enumerator.Current.Y;
                    moveAction = enumerator.Current.ActionNeeded;
                }
            }
            string directionAddr2 = direction == Direction.Up || direction == Direction.Down ? YAddr : XAddr;
            int val2 = direction == Direction.Up || direction == Direction.Down ? curY : curX;
            if (overshootFinalStep)
                val2 += (direction == Direction.Down || direction == Direction.Right) ? 1 : -1;
            if (!first)
            {
                sb.Append(";");
            }
            sb.Append(FormatMove(direction.ToString(), val2, directionAddr2, destGameMapIds));
            if (sb.Length == 0)
            {
                return null;
            }
            return sb.ToString();
        }

        private IEnumerable<NodePoint> ReconstructPath(NodePoint top)
        {
            LinkedList<NodePoint> path = new LinkedList<NodePoint>();
            NodePoint? current = top;
            while (current is not null)
            {
                path.AddFirst(current);
                current = current.CameFrom;
            }
            return path;
        }

        private IEnumerable<MapPoint> ReconstructPath(MapPoint top)
        {
            LinkedList<MapPoint> path = new LinkedList<MapPoint>();
            MapPoint? current = top;
            while(current is not null)
            {
                path.AddFirst(current);
                current = current.CameFrom;
            }
            return path;
        }

        private List<MapPoint> GetNeighboringMapPoints(Map map, MapPoint top, Capability capability)
        {
            List<MapPoint> neighbors = new List<MapPoint>();
            if(top.ActionNeeded == MoveAction.Portal) // if we hit a portal we can't move around we're on the portal.
            {
                return neighbors;
            }
            MapPoint? left = GetNeighboringMapPoint(top.X - 1, top.Y, top, map, capability);
            if(left is not null)
            {
                neighbors.Add(left);
            }
            MapPoint? right = GetNeighboringMapPoint(top.X + 1, top.Y, top, map, capability);
            if (right is not null)
            {
                neighbors.Add(right);
            }
            MapPoint? up = GetNeighboringMapPoint(top.X, top.Y - 1, top, map, capability);
            if (up is not null)
            {
                neighbors.Add(up);
            }
            MapPoint? down = GetNeighboringMapPoint(top.X, top.Y + 1, top, map, capability);
            if (down is not null)
            {
                neighbors.Add(down);
            }
            return neighbors;
        }

        private MapPoint? GetNeighboringMapPoint(int x, int y, MapPoint top, Map map, Capability capability)
        {
            

            if (x < 0 || x >= map.Width || y < 0 || y >= map.Height)
            {
                return null;
            }
            byte tile = map[y, x];
            if (tile == 0x80)
            {
                return null;
            }
            MapPoint point = new MapPoint()
            {
                X = x,
                Y = y,
                CameFrom = top,
                Cost = top.Cost,
                BoulderState = top.BoulderState,
                InitialStrength = top.InitialStrength,
            };
            int thisCostFactor = 1;
            if (top.Direction != Direction.None && point.Direction != top.Direction) // what if Top is None direction? 
            {
                thisCostFactor = 2;
            }
            BoulderState? boulder = null;
            foreach (var b in top.BoulderState)
            {
                if (point.X == b.X && point.Y == b.Y)
                {
                    boulder = b;
                }
            }
            if (boulder is not null)
            {
                if (capability.CanStrength)
                {
                    int pushY = point.Direction == Direction.Up ? point.Y - 1 : point.Direction == Direction.Down ? point.Y + 1 : point.Y;
                    int pushX = point.Direction == Direction.Left ? point.X - 1 : point.Direction == Direction.Right ? point.X + 1 : point.X;
                    if (top.Direction == Direction.None) // protection for pushing. TODO we need the search to init the direction, by pulling facing
                    {
                        return null;
                    }
                    if (map[pushY, pushX] == Constant.MAP_Free)
                    {
                        int boulderX = point.X;
                        int boulderY = point.Y;
                        point.X = top.X;
                        point.Y = top.Y;
                        point.Cost += 10 + (point.InitialStrength ? 30 : 0);
                        point.PushBoulder(boulderX, boulderY, pushX, pushY);
                        point.InitialStrength = true;
                        point.ActionNeeded = MoveAction.Strength;
                        return point;
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }
            else if (tile == Constant.MAP_Free)
            {
                if (top.ActionNeeded == MoveAction.Bridge && map[top.Y, top.X] != Constant.MAP_BridgeStart)
                {
                    return null;
                }
                point.Cost += 1 * thisCostFactor;
                return point;
            }
            else if (tile == Constant.MAP_Portal1 || tile == Constant.MAP_Portal2) // no special logic for stepping on a portal.
            {
                point.Cost += 1 * thisCostFactor;
                point.ActionNeeded = MoveAction.Portal;
                return point;
            }
            else if(tile == Constant.MAP_LeftLedge)
            {
                if(top.X > x)
                {
                    return GetNeighboringMapPoint(x - 1, y, top, map, capability);
                }
                return null;
            }
            else if(tile == Constant.MAP_RightLedge)
            {
                if(top.X < x)
                {
                    return GetNeighboringMapPoint(x + 1, y, top, map, capability);
                }
                return null;
            }
            else if(tile == Constant.MAP_DownLedge)
            {
                if(top.Y < y)
                {
                    return GetNeighboringMapPoint(x, y + 1, top, map, capability);
                }
            }
            else if(tile == Constant.MAP_Grass)
            {
                point.Cost += 3 * thisCostFactor;
                return point;
            }
            else if(tile == Constant.MAP_WalkingTrainer)
            {
                if(point.CameFrom.ActionNeeded == MoveAction.Bridge)
                {
                    point.ActionNeeded = MoveAction.Bridge;
                }
                else if(point.CameFrom.ActionNeeded == MoveAction.Surf)
                {
                    thisCostFactor *= 3;
                    point.ActionNeeded = MoveAction.Surf;
                }
                point.Cost += 10 * thisCostFactor;
                return point;
            }
            else if(tile == Constant.MAP_Surf)
            {
                if (capability.CanSurf && point.CameFrom.ActionNeeded != MoveAction.Bridge)
                {
                    if(point.CameFrom.ActionNeeded == MoveAction.Surf) 
                    {
                        point.Cost += 3 * thisCostFactor;
                    }
                    else
                    {
                        point.Cost += 10 * thisCostFactor;
                    }
                    point.ActionNeeded = MoveAction.Surf;
                    return point;
                }
                return null;
            }
            else if(tile == Constant.MAP_Cut)
            {
                if (capability.CanCut)
                {
                    point.Cost += 10 * thisCostFactor;
                    point.ActionNeeded = MoveAction.Cut;
                    return point;
                }
                return null;
            }
            else if(tile == Constant.MAP_RockSmash)
            {
                if (capability.CanRockSmash)
                {
                    point.Cost += 30 * thisCostFactor;
                    point.ActionNeeded = MoveAction.RockSmash;
                    return point;
                }
            }
            else if (tile == Constant.MAP_BridgeStart || tile == Constant.MAP_BridgeMiddle && point.CameFrom.ActionNeeded != MoveAction.Surf)
            {
                if (map[top.Y, top.X] == Constant.MAP_Bridge && top.ActionNeeded != MoveAction.Bridge)
                {
                    return null; // if we are on a bridge tile and not on a bridge can't go on this tile, we are underneath.
                }
                point.ActionNeeded = MoveAction.Bridge;
                return point;
            }
            else if(tile == Constant.MAP_Bridge)
            {
                if (point.CameFrom.ActionNeeded == MoveAction.Bridge)
                {
                    point.ActionNeeded = MoveAction.Bridge;
                    point.Cost += 1 * thisCostFactor;
                    return point;
                }
                else if (point.CameFrom.ActionNeeded == MoveAction.Surf)
                {
                    point.ActionNeeded = MoveAction.Surf;
                    point.Cost += 3 * thisCostFactor;
                    return point;
                }
                else if(top.ActionNeeded == MoveAction.None)
                {
                    point.Cost += 3 * thisCostFactor;
                    return point;
                }
                else
                {
                    throw new Exception("Somehow we are on a bridge without already being on the bridge or surfing/walking under it");
                }
            }
            else if(tile == Constant.MAP_Waterfall)
            {
                if (!capability.CanWaterfall) 
                {
                    return null;
                }
                //if (point.CameFrom.ActionNeeded != MoveAction.Surf && point.CameFrom.ActionNeeded != MoveAction.Waterfall)
                //{
                //    return null;
                //}
                if(point.Direction == Direction.Left || point.Direction == Direction.Right) // hack for walking underneath
                {
                    point.Cost += 1 * thisCostFactor;
                    return point;
                }
                int newY = point.Direction == Direction.Up ? point.Y - 1 : point.Direction == Direction.Down ? point.Y + 1 : point.Y;
                int newX = point.Direction == Direction.Left ? point.X - 1 : point.Direction == Direction.Right ? point.X + 1 : point.X;
                var newPoint = GetNeighboringMapPoint(newX, newY, top, map, capability); // this needs to search until water. or we need to confirm if 0x22 is a surf tile (Tohjo Falls)
                if(newPoint is null)
                {
                    return null;
                }
                newPoint.Cost += 10;
                newPoint.ActionNeeded = MoveAction.Waterfall;
                return newPoint;
            }
            else if (tile == Constant.MAP_Whirlpool)
            {
                if (!capability.CanWhirlpool)
                {
                    return null;
                }
                if(point.CameFrom.ActionNeeded != MoveAction.Surf && point.CameFrom.ActionNeeded != MoveAction.Whirlpool)
                {
                    return null;
                }
                int newY = point.Direction == Direction.Up ? point.Y - 1 : point.Direction == Direction.Down ? point.Y + 1 : point.Y;
                int newX = point.Direction == Direction.Left ? point.X - 1 : point.Direction == Direction.Right ? point.X + 1 : point.X;
                var newPoint = GetNeighboringMapPoint(newX, newY, top, map, capability);
                if (newPoint is null)
                {
                    return null;
                }
                newPoint.Cost += 10;
                newPoint.ActionNeeded = MoveAction.Whirlpool;
                return newPoint;
            }
            return null;
        }
    }
}
