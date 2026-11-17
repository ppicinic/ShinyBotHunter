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

        private const int OldaleTown = 10, Route101 = 16, Route103 = 18, Route114 = 29, Route124 = 39, Underwater1 = 50;
        private const int OldaleCenter = 514, RusturfTunnel = 6148, GraniteCaveB1F = 6152, GraniteCaveB2F = 6153, PetalburgWoods = 6155, AquaHideoutB1F = 6168;

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

        // rs-nav-flags: Oldale's trigger at (8, 19) (var 0x40C7) on the way from Route 101 to the Pokémon Center
        // counter. The other way in, by (11, 19), is closed by the flagged NPC of flag 979 until that flag is set.
        [Fact]
        public void LiveTrigger_IsDetouredAndOrdinaryWhenItsVariableDiffers()
        {
            byte[] flagSet = NoFlags();
            flagSet[979 >> 3] |= 1 << (979 & 7);
            byte[] varOne = NoVars();
            varOne[(0x40C7 - 0x4000) * 2] = 1;

            Route? detour = Find(Rs, Route101, 14, 4, OldaleCenter, 7, 4, new Capability(), flagSet, varOne);
            Assert.NotNull(detour);
            Assert.True(Crosses(detour, OldaleTown, 11, 19));
            Assert.Empty(detour.LiveTriggers);

            Route? straight = Find(Rs, Route101, 14, 4, OldaleCenter, 7, 4, new Capability(), flagSet, NoVars());
            Assert.NotNull(straight);
            Assert.True(Crosses(straight, OldaleTown, 8, 19));
            Assert.Empty(straight.LiveTriggers);

            Route? through = Find(Rs, Route101, 14, 4, OldaleCenter, 7, 4, new Capability(), NoFlags(), varOne);
            Assert.NotNull(through);
            Assert.Equal([(OldaleTown, 8, 19)], through.LiveTriggers);
            Assert.Equal(244, through.Cost);
        }

        // FR-11's search half: the nearest Pokémon Center by route cost is the same Find with a goal predicate (the
        // counter tile (7, 4) of the shared layout is N4's predicate).
        [Fact]
        public void NearestPokemonCenter_FromRoute101_IsOldale()
        {
            Route? route = new RouteSearch(Rs, new RsPointerService(), GameFlavor.RS).Find(
                Route101, 14, 4, Rs.GetMap(Route101, true).ElevationAt(4, 14),
                (m, x, y) => Rs.GetMapLookup(m).InternalName.EndsWith("_PokemonCenter_1F") && x == 7 && y == 4,
                NoFlags(), NoVars(), new Capability());

            Assert.NotNull(route);
            Assert.Equal((OldaleCenter, 7, 4), (route.Steps[^1].Map, route.Steps[^1].X, route.Steps[^1].Y));
        }

        // FR-4a: Dive is an edge between Route 124 and Underwater1 through the one access tile (10, 3) on each side.
        [Fact]
        public void Dive_JoinsTheSurfaceAndUnderwaterMapsAtTheAccessTile()
        {
            Assert.Null(Find(Rs, Route124, 12, 5, Underwater1, 11, 3, With(CapabilityType.Surf)));

            Route? route = Find(Rs, Route124, 12, 5, Underwater1, 11, 3, With(CapabilityType.Surf, CapabilityType.Dive));

            Assert.NotNull(route);
            int dive = route.Steps.ToList().FindIndex(s => s.Action == MoveAction.Dive);
            Assert.True(dive > 0);
            Assert.Equal((Route124, 10, 3), (route.Steps[dive - 1].Map, route.Steps[dive - 1].X, route.Steps[dive - 1].Y));
            Assert.Equal((Underwater1, 10, 3, Direction.None), (route.Steps[dive].Map, route.Steps[dive].X, route.Steps[dive].Y, route.Steps[dive].Direction));
        }

        // FR-7: map 1 (3 x 5) has a Right connection to map 2 at offset 2, so stepping Right off (2, 4) lands on (0, 2).
        [Fact]
        public void Connection_ShiftsTheLandingByItsOffset()
        {
            Map west = new(5, 3);
            west.Events.Connections.Add(new MapConnection(Direction.Right, 2, 2));
            MapService maps = new(new Dictionary<int, Map> { [1] = west, [2] = new Map(5, 3) });

            Route? route = new RouteSearch(maps, new RsPointerService(), GameFlavor.RS).Find(1, 2, 4, 0, (m, _, _) => m == 2, NoFlags(), NoVars(), new Capability());

            Assert.NotNull(route);
            Assert.Equal((2, 0, 2, Direction.Right), (route.Steps[^1].Map, route.Steps[^1].X, route.Steps[^1].Y, route.Steps[^1].Direction));
        }

        // FR-8, the research doc's five macros ("Search and macros"), derived with the spike's rsmaps.py on pokeruby
        // 5784633 and the plan of #426: Left to Route 101 (8, 4), Up to row 0, over the connection to Oldale row 17,
        // Left to (6, 17) under the Center's door (6, 16), and Up through the door, which lands on the mat (7, 8), to
        // the counter. x is RunWatchX 0x02025732 = 33707826, y RunWatchY 0x02025734 = 33707828.
        [Fact]
        public void Route101ToOldaleCenter_GivesTheFiveMacros()
        {
            Route? route = Find(Rs, Route101, 14, 4, OldaleCenter, 7, 4, new Capability());

            Assert.NotNull(route);
            Assert.Equal(45, route.Cost);
            Assert.Empty(route.LiveTriggers);
            Assert.All(route.Steps, s => Assert.True(s.Action == MoveAction.None || s.Action == MoveAction.Portal));
            Assert.Equal(
                [
                    ("M.Left.8.33707826:H.B.1", Route101, 8, 4),
                    ("M.Up.0.33707828", Route101, 8, 0),
                    ("M.Up.17.33707828", OldaleTown, 8, 17),
                    ("M.Left.6.33707826", OldaleTown, 6, 17),
                    ("M.Up.4.33707828", OldaleCenter, 7, 4),
                ],
                route.Segments);
        }

        // FR-8: Down onto the Center's south mat (7, 8), then Down on the mat, which warps to Oldale (6, 17), the tile
        // below the door (N1b's landing); that run waits for the y outside.
        [Fact]
        public void ArrowWarp_RunWaitsForTheCoordinateOutside()
        {
            Route? route = Find(Rs, OldaleCenter, 7, 4, OldaleTown, 6, 17, new Capability());

            Assert.NotNull(route);
            Assert.Equal([("M.Down.8.33707828:H.B.1", OldaleCenter, 7, 8), ("M.Down.17.33707828", OldaleTown, 6, 17)], route.Segments);
        }

        // R3 on two inline maps: the player at (1, 1) of map 1 steps Up onto the door (1, 0), so y 1 and y 0 are shown
        // before map 2. Landing on (3, 1) and walking on to (3, 0) would wait for a y already shown, so the run is cut
        // at the landing, which waits on x. Landing on (1, 1), both axes are shown, so the cut run waits for the door's
        // own y 0 and still ends on the landing.
        [Theory]
        [InlineData(3, "M.Up.3.33707826:H.B.1")]
        [InlineData(1, "M.Up.0.33707828:H.B.1")]
        public void MapChangingRun_EndingOnAShownCoordinate_IsCutAtTheLanding(int landingX, string cut)
        {
            Map outside = new(3, 3);
            outside[0, 1] = Constant.MAP_Portal1;
            outside.Events.Warps.Add(new MapWarp(1, 0, 2, landingX, 1, Direction.None));
            MapService maps = new(new Dictionary<int, Map> { [1] = outside, [2] = new Map(3, 5) });

            Route? route = Find(maps, 1, 1, 1, 2, landingX, 0, new Capability());

            Assert.NotNull(route);
            Assert.Equal([(cut, 2, landingX, 1), ("M.Up.0.33707828", 2, landingX, 0)], route.Segments);
        }

        // R3 on the committed data: Granite Cave B2F's ladder (29, 13), pressed Up from (29, 14), lands on B1F (29, 13),
        // whose coordinates were both shown, so the run waits for the ladder's y 13 and ends on the landing.
        [Fact]
        public void LadderLandingOnShownCoordinates_WaitsForTheLaddersOwnCoordinate()
        {
            Route? route = Find(Rs, GraniteCaveB2F, 29, 14, GraniteCaveB1F, 28, 13, new Capability());

            Assert.NotNull(route);
            Assert.Equal([("M.Up.13.33707828:H.B.1", GraniteCaveB1F, 29, 13), ("M.Left.28.33707826", GraniteCaveB1F, 28, 13)], route.Segments);
        }

        // The reviewer's fixture of B1: on map 1 the cheapest way to map 2 is the ladder (2, 2), which lands on (2, 2)
        // of map 2 (both coordinates shown); the dearer one is the door (4, 0) to map 2 (4, 4). The cheapest crossing
        // gets its macros, so it no longer hides the route (it used to make Find return null).
        [Fact]
        public void CrossingWithNoNewAxis_DoesNotHideTheRoute()
        {
            Map outside = new(5, 5);
            outside[2, 2] = Constant.MAP_Portal1;
            outside[0, 4] = Constant.MAP_Portal1;
            outside.Events.Warps.Add(new MapWarp(2, 2, 2, 2, 2, Direction.None));
            outside.Events.Warps.Add(new MapWarp(4, 0, 2, 4, 4, Direction.None));
            MapService maps = new(new Dictionary<int, Map> { [1] = outside, [2] = new Map(5, 5) });

            Route? route = Find(maps, 1, 2, 3, 2, 0, 2, new Capability());

            Assert.NotNull(route);
            Assert.Equal([("M.Up.2.33707828:H.B.1", 2, 2, 2), ("M.Left.0.33707826", 2, 0, 2)], route.Segments);
        }

        // R2 and R3 for a warp to the same map: Aqua Hideout B1F's pad (31, 4), stepped onto Right from (30, 4), lands
        // on (27, 12) of the same map; going on Right to (30, 12) would wait for an x already shown, so the run is cut.
        // The flagged object at (29, 12) (flag 1071) is hidden so the row is open.
        [Fact]
        public void WarpToTheSameMap_StartsARunAndGetsR3()
        {
            byte[] flags = NoFlags();
            flags[1071 >> 3] |= 1 << (1071 & 7);

            Route? route = Find(Rs, AquaHideoutB1F, 30, 4, AquaHideoutB1F, 30, 12, new Capability(), flags);

            Assert.NotNull(route);
            Assert.Equal([("M.Right.27.33707826:H.B.1", AquaHideoutB1F, 27, 12), ("M.Right.30.33707826", AquaHideoutB1F, 30, 12)], route.Segments);
        }

        // S1: a Pokémon Center's stairs (1, 6) are at elevation 4 between tiles at 3, so they are entered from (2, 6)
        // at 0, never pressed Up into from (1, 7).
        [Fact]
        public void CenterStairs_AreEnteredUnderTheElevationRule()
        {
            Route? route = Find(Rs, OldaleCenter, 1, 7, OldaleCenter + 1, 2, 5, new Capability());

            Assert.NotNull(route);
            int warp = route.Steps.ToList().FindIndex(s => s.Action == MoveAction.Portal);
            Assert.Equal((OldaleCenter, 2, 6, Direction.Left), (route.Steps[warp - 1].Map, route.Steps[warp - 1].X, route.Steps[warp - 1].Y, route.Steps[warp].Direction));
        }
    }
}
