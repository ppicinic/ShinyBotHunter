using ShinyBotHunter.Games;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Util;

namespace ShinyBotHunter.Tests
{
    // The committed Data/RS, with the map ids of mapreverselookup.json, and small inline maps for what the data has no
    // clean case of. RouteSearch asks the registry for RunHold, so the working directory is the app project (as in
    // RunEncounterEventTests).
    [Collection("macro-registry")]
    public class RouteSearchTests
    {
        private static readonly MapService Rs = new(Path.Combine(RepoPaths.AppProject, "Data", "RS"), withElevationAndEvents: true);

        private const int Route103 = 18, Route114 = 29, RusturfTunnel = 6148, PetalburgWoods = 6155;

        public RouteSearchTests()
        {
            Directory.SetCurrentDirectory(RepoPaths.AppProject);
        }

        private static Capability With(params CapabilityType[] types) => new() { CapabilityMask = types.Aggregate(0, (mask, t) => mask | (int)t) };

        private static byte[] NoFlags() => new byte[0x120];

        private static byte[] NoVars() => new byte[0x200];

        private static Route? Find(MapService maps, int map, int x, int y, int goalMap, int goalX, int goalY, Capability capability, byte[]? flags = null, byte[]? vars = null) =>
            new RouteSearch(maps, new RsPointerService(), GameFlavor.RS).Find(
                map, x, y, maps.GetMap(map, true).ElevationAt(y, x), (m, gx, gy) => m == goalMap && gx == goalX && gy == goalY, flags ?? NoFlags(), vars ?? NoVars(), capability);

        private static bool Crosses(Route route, int map, int x, int y) => route.Steps.Any(s => s.Map == map && s.X == x && s.Y == y);

        // FR-4a, with research "HMs in the data and the search": each obstacle is crossed with its capability and,
        // FR-4b, not at all with an empty mask.
        [Fact]
        public void CutTrees_NeedCut()
        {
            Route? route = Find(Rs, PetalburgWoods, 16, 38, PetalburgWoods, 22, 10, With(CapabilityType.Cut));

            Assert.NotNull(route);
            Assert.True(Crosses(route, PetalburgWoods, 19, 10) || Crosses(route, PetalburgWoods, 19, 11));
            Assert.Contains(route.Steps, s => s.Action == MoveAction.Cut);
            Assert.Null(Find(Rs, PetalburgWoods, 16, 38, PetalburgWoods, 22, 10, new Capability()));
        }

        [Fact]
        public void SmashRocks_NeedRockSmash()
        {
            Route? route = Find(Rs, RusturfTunnel, 20, 4, RusturfTunnel, 28, 4, With(CapabilityType.RockSmash));

            Assert.NotNull(route);
            Assert.True(Crosses(route, RusturfTunnel, 24, 4) && Crosses(route, RusturfTunnel, 24, 5));
            Assert.Contains(route.Steps, s => s.Action == MoveAction.RockSmash);
            Assert.Null(Find(Rs, RusturfTunnel, 20, 4, RusturfTunnel, 28, 4, new Capability()));
        }

        [Fact]
        public void Route103Channel_NeedsSurf()
        {
            Route? route = Find(Rs, Route103, 10, 10, Route103, 50, 12, With(CapabilityType.Surf));

            Assert.NotNull(route);
            Assert.Contains(route.Steps, s => s.Action == MoveAction.Surf && Rs.GetMap(Route103, true)[s.Y, s.X] == Constant.MAP_Surf);
            Assert.Null(Find(Rs, Route103, 10, 10, Route103, 50, 12, new Capability()));
        }

        [Fact]
        public void Route114Waterfall_NeedsWaterfall()
        {
            Route? route = Find(Rs, Route114, 10, 14, Route114, 10, 8, With(CapabilityType.Surf, CapabilityType.Waterfall));

            Assert.NotNull(route);
            Assert.Contains(route.Steps, s => s.Action == MoveAction.Waterfall);
            Assert.Null(Find(Rs, Route114, 10, 14, Route114, 10, 8, With(CapabilityType.Surf)));
            Assert.Null(Find(Rs, Route114, 10, 14, Route114, 10, 8, new Capability()));
        }

        // rs-nav-flags: Rusturf Tunnel's flagged NPC at (14, 4) (flag 880) closes the corridor while the flag is clear.
        [Fact]
        public void FlaggedNpc_ClosesTheCorridorUntilItsFlagIsSet()
        {
            byte[] flags = NoFlags();

            Assert.Null(Find(Rs, RusturfTunnel, 13, 4, RusturfTunnel, 15, 4, new Capability(), flags));

            flags[880 >> 3] |= 1 << (880 & 7);
            Route? route = Find(Rs, RusturfTunnel, 13, 4, RusturfTunnel, 15, 4, new Capability(), flags);

            Assert.NotNull(route);
            Assert.True(Crosses(route, RusturfTunnel, 14, 4));
        }

        // A 1 x 7 corridor: start at x 0, a boulder at x 2, the goal at x 4. The boulder goes to x 3, then x 4, then x 5,
        // pushed from x 1, 2 and 3 (HGSS's push: the player stays on its tile).
        [Fact]
        public void Boulder_IsPushedOnlyWithStrength()
        {
            Map corridor = new(1, 7) { Boulders = [new() { X = 2, Y = 0 }] };
            MapService maps = new(new Dictionary<int, Map> { [1] = corridor });

            Assert.Null(Find(maps, 1, 0, 0, 1, 4, 0, new Capability()));

            Route? route = Find(maps, 1, 0, 0, 1, 4, 0, With(CapabilityType.Strength));

            Assert.NotNull(route);
            Assert.Equal(3, route.Steps.Count(s => s.Action == MoveAction.Strength));
            Assert.Equal((1, 4, 0), (route.Steps[^1].Map, route.Steps[^1].X, route.Steps[^1].Y));
        }

        // FR-6, a 5 x 5 bridge: row 2 is a bridge at elevation 4, column 2 is ground at 3 running under it, (2, 2) is
        // the crossing tile at 15, and every other tile is blocked.
        [Fact]
        public void Bridge_ElevationDecidesOverOrUnder()
        {
            Map bridge = new(5, 5) { Elevation = new byte[5][] };
            for (int y = 0; y < 5; y++)
            {
                bridge.Elevation[y] = new byte[5];
                for (int x = 0; x < 5; x++)
                {
                    bool onBridge = y == 2;
                    bool ground = x == 2;
                    bridge[y, x] = onBridge || ground ? Constant.MAP_Free : Constant.MAP_Blocked;
                    bridge.Elevation[y][x] = (byte)(onBridge ? 4 : 3);
                }
            }
            bridge.Elevation[2][2] = 15;
            MapService maps = new(new Dictionary<int, Map> { [1] = bridge });

            Route? Search(int x, int y, int elevation, int goalX, int goalY) =>
                new RouteSearch(maps, new RsPointerService(), GameFlavor.RS).Find(1, x, y, elevation, (_, gx, gy) => gx == goalX && gy == goalY, NoFlags(), NoVars(), new Capability());

            // On the bridge at 4: along the row, never down to the ground at 3.
            Assert.NotNull(Search(0, 2, 4, 4, 2));
            Assert.Null(Search(0, 2, 4, 2, 4));
            // On the ground at 3: under the bridge through the 15 tile, never onto a 4.
            Route? under = Search(2, 0, 3, 2, 4);
            Assert.NotNull(under);
            Assert.DoesNotContain(under.Steps, s => bridge.ElevationAt(s.Y, s.X) == 4);
            Assert.Null(Search(2, 0, 3, 0, 2));
            // At 0 on the crossing tile, both.
            Assert.NotNull(Search(2, 2, 0, 0, 2));
            Assert.NotNull(Search(2, 2, 0, 2, 4));
        }
    }
}
