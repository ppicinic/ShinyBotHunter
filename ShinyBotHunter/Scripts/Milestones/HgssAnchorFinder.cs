using Newtonsoft.Json.Linq;
using ShinyBotHunter.Games;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Util;
using System.Text.RegularExpressions;

namespace ShinyBotHunter.Scripts.Milestones
{
    // A land milestone's anchor (FR-19, FR-20). The tiles are its kind's (grass, or floor in a cave) on its maps, out of
    // every trainer's sight and off the tile below every sign, which the map data does not keep, so they are read from
    // the decomp's zone events. The longest straight run whose middle tile reaches a Pokémon Center and back wins, left
    // and right before up and down at equal length, and among equal runs the cheapest to the Pokémon Center. Under three
    // tiles it is a spin on the cheapest tile. With no HM first, and only when nothing is reached, with every HM.
    // A headbutt tree's stand (FR-21) is a tile beside the tree, on a free or grass tile and reached with no HM; it has
    // no fallback to every HM.
    public class HgssAnchorFinder
    {
        private static readonly Direction[] AllDirections = [Direction.Up, Direction.Down, Direction.Left, Direction.Right];

        private readonly Dictionary<string, string> zoneEvents;
        private readonly MapService maps;
        private readonly PathfindingService pathfinding;
        private readonly ushort[,] owners;

        public HgssAnchorFinder(string decompDir, MapService maps, PathfindingService pathfinding, ushort[,] owners)
        {
            // NNN_<internal name>.json; NNN is the file's position, not the map id.
            zoneEvents = Directory.GetFiles(Path.Combine(decompDir, "files", "fielddata", "eventdata", "zone_event"), "*.json")
                .ToDictionary(path => Regex.Match(Path.GetFileName(path), @"^\d+_(.+)\.json$").Groups[1].Value);
            this.maps = maps;
            this.pathfinding = pathfinding;
            this.owners = owners;
        }

        // The milestone's maps as game map id, internal name and whether the map is on the overworld (matrix 0).
        public async Task<Anchor?> FindAsync(IReadOnlyList<(int Id, string Internal, bool Overworld)> milestoneMaps, bool grass)
        {
            byte kind = grass ? Constant.MAP_Grass : Constant.MAP_Free;
            var tiles = milestoneMaps.Select(m => (m.Id, Tiles(m.Id, m.Internal, m.Overworld, kind))).ToList();
            return await FindAsync(tiles, new Capability()) ?? await FindAsync(tiles, new Capability { CapabilityMask = 0xFF });
        }

        // Each tree of one headbutt map with the stand beside it that is cheapest to a Pokémon Center and reached back from
        // it with no HM, on a free or grass tile; a tree with no such stand is returned unchanged. As for RS land (B1r) there
        // is no HM fallback, and a stand on water, a Cut tile or a Rock Smash tile is not one (headbutt while surfing is not run).
        public async Task<HeadbuttTree[]> StandAsync(HeadbuttTree[] trees, int id, string internalName, bool overworld)
        {
            var map = maps.GetMap(id);
            var excluded = Unsafe(JObject.Parse(File.ReadAllText(zoneEvents[internalName])));
            var treeTiles = trees.SelectMany(t => t.Tiles).Select(t => (t[0], t[1])).ToHashSet();
            bool InMap(int x, int y) => x >= 0 && y >= 0 && x < map.Width && y < map.Height;
            bool Blocked(int x, int y) => InMap(x, y) && map[y, x] == Constant.MAP_Blocked;
            bool Allowed(int x, int y) => InMap(x, y) && map[y, x] is Constant.MAP_Free or Constant.MAP_Grass
                && (!overworld || owners[y / HgssMilestoneProcessor.CellSize, x / HgssMilestoneProcessor.CellSize] == id)
                && !excluded.Contains((x, y));
            var costs = new Dictionary<(int X, int Y), int?>();
            var result = new HeadbuttTree[trees.Length];
            for (int i = 0; i < trees.Length; i++)
            {
                (int X, int Y, Direction Facing)? best = null;
                int bestCost = int.MaxValue;
                foreach (var stand in Stands(trees[i], treeTiles, Blocked, Allowed))
                {
                    if (!costs.TryGetValue((stand.X, stand.Y), out int? cost))
                    {
                        cost = costs[(stand.X, stand.Y)] = await CenterCost(id, stand.X, stand.Y, new Capability());
                    }
                    if (cost < bestCost)
                    {
                        best = stand;
                        bestCost = cost.Value;
                    }
                }
                result[i] = best is { } found
                    ? trees[i] with { Anchor = new Anchor(id, found.X, found.Y, 0, 0), Facing = found.Facing, Cost = bestCost }
                    : trees[i];
            }
            return result;
        }

        // Each tile beside a tree tile, facing it, in tile then Up, Down, Left, Right order. The tree tile is blocked, so the
        // hunt's turn press toward it only turns; the stand is no tree's tile and is allowed (on the map, out of sight).
        internal static List<(int X, int Y, Direction Facing)> Stands(HeadbuttTree tree, IReadOnlySet<(int X, int Y)> treeTiles,
            Func<int, int, bool> blocked, Func<int, int, bool> allowed)
        {
            var stands = new List<(int X, int Y, Direction Facing)>();
            foreach (var tile in tree.Tiles.Where(t => blocked(t[0], t[1])))
            {
                foreach (var facing in AllDirections)
                {
                    var (stepX, stepY) = facing switch { Direction.Up => (0, -1), Direction.Down => (0, 1), Direction.Left => (-1, 0), _ => (1, 0) };
                    int x = tile[0] - stepX, y = tile[1] - stepY;
                    if (!treeTiles.Contains((x, y)) && allowed(x, y))
                    {
                        stands.Add((x, y, facing));
                    }
                }
            }
            return stands;
        }

        // A trainer (an object of type other than 0) sees param0 tiles: along its facing for movement 0 and 14 to 17,
        // otherwise in all four directions from every tile of its xRange/yRange box, since it turns. Beaten and hidden
        // trainers count and walls do not block sight. A sign (bg type 1) opens on an Up press from the tile below it.
        internal static HashSet<(int X, int Y)> Unsafe(JObject zoneEvent)
        {
            var tiles = new HashSet<(int X, int Y)>();
            foreach (var trainer in zoneEvent["objects"]?.Where(o => (int)o["type"]! != 0) ?? [])
            {
                int x = (int)trainer["x"]!, y = (int)trainer["z"]!, sight = (int)trainer["param0"]!, movement = (int)trainer["movement"]!;
                bool fixedFacing = movement is 0 or (>= 14 and <= 17);
                Direction[] directions = fixedFacing ? [(Direction)(int)trainer["facingDirection"]!] : AllDirections;
                int xRange = fixedFacing ? 0 : (int)trainer["xRange"]!, yRange = fixedFacing ? 0 : (int)trainer["yRange"]!;
                for (int dy = -yRange; dy <= yRange; dy++)
                {
                    for (int dx = -xRange; dx <= xRange; dx++)
                    {
                        foreach (var direction in directions)
                        {
                            var (stepX, stepY) = direction switch { Direction.Up => (0, -1), Direction.Down => (0, 1), Direction.Left => (-1, 0), _ => (1, 0) };
                            for (int step = 1; step <= sight; step++)
                            {
                                tiles.Add((x + dx + stepX * step, y + dy + stepY * step));
                            }
                        }
                    }
                }
            }
            foreach (var sign in zoneEvent["bgs"]?.Where(b => (int)b["type"]! == 1) ?? [])
            {
                tiles.Add(((int)sign["x"]!, (int)sign["z"]! + 1));
            }
            return tiles;
        }

        // Every maximal row and column run of the tiles, by start tile in row-major order.
        internal static List<(int X, int Y, int Length, bool Horizontal)> Runs(HashSet<(int X, int Y)> tiles)
        {
            var runs = new List<(int X, int Y, int Length, bool Horizontal)>();
            foreach (var (x, y) in tiles.OrderBy(t => t.Y).ThenBy(t => t.X))
            {
                if (!tiles.Contains((x - 1, y)))
                {
                    int length = 1;
                    while (tiles.Contains((x + length, y)))
                    {
                        length++;
                    }
                    runs.Add((x, y, length, true));
                }
                if (!tiles.Contains((x, y - 1)))
                {
                    int length = 1;
                    while (tiles.Contains((x, y + length)))
                    {
                        length++;
                    }
                    runs.Add((x, y, length, false));
                }
            }
            return runs;
        }

        // The map's tiles of the kind: on the overworld only in the 32 x 32 cells the map owns.
        private HashSet<(int X, int Y)> Tiles(int id, string internalName, bool overworld, byte kind)
        {
            var map = maps.GetMap(id);
            var excluded = Unsafe(JObject.Parse(File.ReadAllText(zoneEvents[internalName])));
            var tiles = new HashSet<(int X, int Y)>();
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    if (map[y, x] == kind && (!overworld || owners[y / HgssMilestoneProcessor.CellSize, x / HgssMilestoneProcessor.CellSize] == id)
                        && !excluded.Contains((x, y)))
                    {
                        tiles.Add((x, y));
                    }
                }
            }
            return tiles;
        }

        private async Task<Anchor?> FindAsync(List<(int Map, HashSet<(int X, int Y)> Tiles)> tiles, Capability capability)
        {
            var runs = tiles.SelectMany(t => Runs(t.Tiles).Where(r => r.Length >= 3).Select(r =>
            {
                int range = (r.Length - 1) / 2;
                var anchor = r.Horizontal ? new Anchor(t.Map, r.X + range, r.Y, range, 0) : new Anchor(t.Map, r.X, r.Y + range, 0, range);
                return (r.Length, r.Horizontal, Anchor: anchor);
            }));
            foreach (var group in runs.GroupBy(r => (r.Length, r.Horizontal)).OrderByDescending(g => g.Key.Length).ThenByDescending(g => g.Key.Horizontal))
            {
                var best = await Cheapest(group.Select(r => r.Anchor), capability);
                if (best is not null)
                {
                    return best;
                }
            }
            return await Cheapest(tiles.SelectMany(t => t.Tiles.OrderBy(p => p.Y).ThenBy(p => p.X).Select(p => new Anchor(t.Map, p.X, p.Y, 0, 0))), capability);
        }

        // The first of the cheapest reached anchors, or null when none is reached.
        private async Task<Anchor?> Cheapest(IEnumerable<Anchor> anchors, Capability capability)
        {
            Anchor? best = null;
            int bestCost = int.MaxValue;
            foreach (var anchor in anchors)
            {
                int? cost = await CenterCost(anchor.Map, anchor.X, anchor.Y, capability);
                if (cost < bestCost)
                {
                    best = anchor;
                    bestCost = cost.Value;
                }
            }
            return best;
        }

        // The cost to the closest Pokémon Center when the tile reaches it and is reached back from it, otherwise null.
        private async Task<int?> CenterCost(int map, int x, int y, Capability capability)
        {
            var toCenter = await pathfinding.FindClosestPokemonCenter(map, x, y, capability, (byte)Direction.Down);
            if (toCenter is null)
            {
                return null;
            }
            var center = toCenter.Last();
            var back = await pathfinding.FindNodePath(center.Node.MapId, center.Node.X, center.Node.Y, map, x, y, capability, (byte)Direction.Down, startInternalId: true);
            return back is null ? null : center.Cost;
        }
    }
}
