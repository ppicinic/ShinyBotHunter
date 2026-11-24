using ShinyBotHunter.Games;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Util;

namespace ShinyBotHunter.Tests
{
    // In the collection because MapService opens maps.bin without read sharing, as MapServiceTests says.
    [Collection("macro-registry")]
    public class PathfindingTests
    {
        private const byte FacingRight = (byte)Direction.Right;

        private static PathfindingService ServiceFor(Map map)
        {
            // ASearch and Djitriska never touch GraphService.
            var table = new HgssPointerService();
            return new PathfindingService(new MapService(new Dictionary<int, Map> { [1] = map }), graphService: null!, table.TrainerX, table.TrainerY, table.Anchor, table.MapIdOffset);
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

        [Fact]
        public void Djitriska_TakesSurfEdgeWithCanSurf()
        {
            var a = new Node { Id = 1 };
            var b = new Node { Id = 2 };
            var c = new Node { Id = 3 };
            Link(a, b, 1, (int)CapabilityType.Surf);
            Link(a, c, 5);
            Link(c, b, 5);

            var result = ServiceFor(new Map(1, 1)).Djitriska(a, new Capability { CanSurf = true }, p => p.Node.Id == b.Id);

            Assert.NotNull(result);
            Assert.Equal(1, result.Cost);
            Assert.Same(a, result.CameFrom!.Node);
        }

        [Fact]
        public void Djitriska_SkipsEdgeNeedingCapabilityNotHeld()
        {
            var a = new Node { Id = 1 };
            var b = new Node { Id = 2 };
            var c = new Node { Id = 3 };
            Link(a, b, 1, (int)(CapabilityType.Surf | CapabilityType.Strength));
            Link(a, c, 5);
            Link(c, b, 5);

            var result = ServiceFor(new Map(1, 1)).Djitriska(a, new Capability { CanSurf = true }, p => p.Node.Id == b.Id);

            Assert.NotNull(result);
            Assert.Equal(10, result.Cost);
            Assert.Same(c, result.CameFrom!.Node);
        }

        [Fact]
        public void CreateMacro_MoveStringsAreUnchanged()
        {
            PathfindingService service = ServiceFor(new Map(1, 1));
            MapPoint[] right = [new(Direction.Right) { X = 0, Y = 0 }, new(Direction.Right) { X = 3, Y = 0 }];
            MapPoint[] up = [new(Direction.Up) { X = 0, Y = 5 }, new(Direction.Up) { X = 0, Y = 2 }];

            string? warpMove = service.CreateMacro(right, destGameMapIds: [7, 9]);
            string? plainMove = service.CreateMacro(up);

            Assert.Equal("M.Right.3.35497716.35471704.142756.7.9", warpMove);
            Assert.Equal("M.Up.2.35497724", plainMove);
        }

        [Fact]
        public void ASidewaysApproachToABuildingExitOvershootsThroughTheDoor()
        {
            // The Pokémon Center's exit as in the HGSS data: the mat at (8, 19), the wall below it, floor on the other sides.
            var map = new Map(height: 21, width: 16);
            for (int x = 0; x < 16; x++) map[20, x] = Constant.MAP_Blocked;
            map[19, 8] = Constant.MAP_Portal2;
            // From Primo's tile: down to the exit's row, then left onto the mat.
            MapPoint[] route = [new(Direction.Up) { X = 12, Y = 12 }, new(Direction.Down) { X = 12, Y = 19 }, new(Direction.Left) { X = 8, Y = 19 }];

            Direction? exit = PathfindingService.ExitDirection(map, 8, 19);
            string? macro = ServiceFor(map).CreateMacro(route, destGameMapIds: [73], overshoot: exit);

            Assert.Equal(Direction.Down, exit);
            Assert.Equal("M.Down.19.35497724;M.Left.8.35497716;M.Down.20.35497724.35471704.142756.73", macro);
        }

        // Walks onto each door tile from each of its open sides on the committed HGSS data, as a door segment
        // into the gatehouse (bot map 55), and checks the walk ends with the step into the door's wall.
        private static void AssertEachOpenSideEndsWith(int liveMapId, (int X, int Y)[] door, string finalMove)
        {
            var maps = new MapService(Path.Combine(RepoPaths.AppProject, "Data", "HGSS"));
            var table = new HgssPointerService();
            var service = new PathfindingService(maps, graphService: null!, table.TrainerX, table.TrainerY, table.Anchor, table.MapIdOffset);
            Map map = maps.GetMap(liveMapId);
            foreach (var (x, y) in door)
            {
                (int X, int Y)[] open = new[] { (x, y - 1), (x, y + 1), (x - 1, y), (x + 1, y) }
                    .Where(n => map[n.Item2, n.Item1] is not (Constant.MAP_Blocked or Constant.MAP_Portal1 or Constant.MAP_Portal2))
                    .ToArray();
                Assert.NotEmpty(open);
                foreach (var (nx, ny) in open)
                {
                    string? macro = service.Pathfind(liveMapId, nx, ny, x, y, new Capability(), FacingRight, destBotMapId: 55);
                    Assert.NotNull(macro);
                    Assert.EndsWith(finalMove, macro);
                }
            }
        }

        [Fact]
        public void VioletGatehouseDoorStepsRightFromEachOpenSide()
        {
            AssertEachOpenSideEndsWith(73, [(511, 269), (511, 270)], "M.Right.512.35497716.35471704.142756.97");
        }

        [Fact]
        public void Route31GatehouseDoorStepsLeft()
        {
            AssertEachOpenSideEndsWith(35, [(518, 269), (518, 270)], "M.Left.517.35497716.35471704.142756.97");
        }
    }
}
