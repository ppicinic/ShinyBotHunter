using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Util;

namespace ShinyBotHunter.Tests
{
    public class PathfindingTests
    {
        private const byte FacingRight = (byte)Direction.Right;

        private static PathfindingService ServiceFor(Map map)
        {
            // ASearch and Djitriska never touch GraphService.
            return new PathfindingService(new MapService(new() { [1] = map }), graphService: null!);
        }

        private static List<(int X, int Y)> Chain(MapPoint end)
        {
            var points = new List<(int X, int Y)>();
            for (MapPoint? p = end; p is not null; p = p.CameFrom)
            {
                points.Add((p.X, p.Y));
            }
            return points;
        }

        private static void Link(Node from, Node to, int cost, int capabilityMask = 0)
        {
            from.Edges.Add(new Edge { FromNodeId = from.Id, ToNodeId = to.Id, ToNode = to, Cost = cost, CapabilityMask = capabilityMask });
        }

        [Fact]
        public void ASearch_OpenGrid_ReturnsStraightLineCost()
        {
            var map = new Map(height: 3, width: 4);

            var result = ServiceFor(map).ASearch(1, 0, 0, 3, 0, new Capability(), FacingRight, internalId: true);

            Assert.NotNull(result);
            Assert.Equal((3, 0), (result.X, result.Y));
            Assert.Equal(3, result.Cost);
        }

        [Fact]
        public void ASearch_BlockedTile_DetoursAround()
        {
            var map = new Map(height: 2, width: 3);
            map[0, 1] = Constant.MAP_Blocked;

            var result = ServiceFor(map).ASearch(1, 0, 0, 2, 0, new Capability(), FacingRight, internalId: true);

            Assert.NotNull(result);
            var chain = Chain(result);
            Assert.DoesNotContain((1, 0), chain);
            Assert.Contains((1, 1), chain);
            Assert.Equal(7, result.Cost);
        }

        [Fact]
        public void ASearch_CutTile_RequiresCanCut()
        {
            var map = new Map(height: 1, width: 3);
            map[0, 1] = Constant.MAP_Cut;
            var service = ServiceFor(map);

            var withoutCut = service.ASearch(1, 0, 0, 2, 0, new Capability(), FacingRight, internalId: true);
            var withCut = service.ASearch(1, 0, 0, 2, 0, new Capability { CanCut = true }, FacingRight, internalId: true);

            Assert.Null(withoutCut);
            Assert.NotNull(withCut);
            Assert.Equal(MoveAction.Cut, withCut.CameFrom!.ActionNeeded);
            Assert.Equal((1, 0), (withCut.CameFrom.X, withCut.CameFrom.Y));
        }

        [Fact]
        public void Djitriska_ReturnsCheapestRoute()
        {
            var a = new Node { Id = 1 };
            var b = new Node { Id = 2 };
            var c = new Node { Id = 3 };
            var d = new Node { Id = 4 };
            Link(a, b, 1);
            Link(b, d, 1);
            Link(a, c, 1);
            Link(c, d, 5);

            var result = ServiceFor(new Map(1, 1)).Djitriska(a, new Capability(), p => p.Node.Id == d.Id);

            Assert.NotNull(result);
            Assert.Equal(2, result.Cost);
            Assert.Same(b, result.CameFrom!.Node);
        }

        // Capability 0 only: the AddEdge mask check is wrong for nonzero capabilities, see #91.
        [Fact]
        public void Djitriska_SkipsEdgeWithCapabilityMask()
        {
            var a = new Node { Id = 1 };
            var b = new Node { Id = 2 };
            var c = new Node { Id = 3 };
            Link(a, b, 1, (int)CapabilityType.Surf);
            Link(a, c, 5);
            Link(c, b, 5);

            var result = ServiceFor(new Map(1, 1)).Djitriska(a, new Capability(), p => p.Node.Id == b.Id);

            Assert.NotNull(result);
            Assert.Equal(10, result.Cost);
            Assert.Same(c, result.CameFrom!.Node);
        }
    }
}
