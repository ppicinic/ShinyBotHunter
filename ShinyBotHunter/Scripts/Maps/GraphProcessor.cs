using Newtonsoft.Json;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Util;
using System.Diagnostics;
using System.IO.Compression;

namespace ShinyBotHunter.Scripts.Maps
{
    public class BaseWarp
    {
        public short header { get; set; }
        public short x { get; set; }
        public short z { get; set; }
        public short anchor { get; set; }
        public short pos { get; set; }
    }

    // Serialized version of Node
    public class NodeData 
    {
        public short Id { get; set; }
        public short MapId { get; set; }
        public short X { get; set; }
        public short Y { get; set; }
        public bool IsPokemonCenter { get; set; }
    }

    public class EdgeData
    {
        public short FromNodeId { get; set; }
        public short ToNodeId { get; set; }
        public int CapabilityMask { get; set; }
        public int Cost { get; set; }
    }
    public class GraphProcessor
    {
        private MapService mapService;
        private Dictionary<int, List<NodeData>> maps = new Dictionary<int, List<NodeData>>();
        private PathfindingService pathfindingService;
        private readonly string outputDir;

        public GraphProcessor(MapService mapService, PathfindingService pathfindingService, string outputDir)
        {
            this.mapService = mapService;
            this.pathfindingService = pathfindingService;
            this.outputDir = outputDir;
        }
        public async Task CreateBaseGraph()
        {
            var warpSource = JsonConvert.DeserializeObject<Dictionary<short, BaseWarp[]>>(File.ReadAllText(Path.Combine(outputDir, "warp.json")));
            HashSet<(int, int)> visited = new HashSet<(int, int)>();
            List<NodeData> nodes = new List<NodeData>();
            List<EdgeData> edges = new List<EdgeData>();
            short id = 0;
            foreach(var (key, baseWarps) in warpSource!)
            {
                foreach(var w in baseWarps)
                {
                    if(!visited.Contains((key, w.pos)))
                    {
                        visited.Add((key, w.pos));
                        BaseWarp? w2 = FindMatchingWarp(key, w, warpSource);
                        if (w2 is not null && w.anchor == w2.pos && w2.anchor == w.pos) // we filter out warps that don't have a pair. This handles things like double tile warps or elevators that we can't figure out yet.
                        {
                            visited.Add((w.header, w2.pos));
                            var lookups = mapService.GetMapLookups(key);
                            NodeData wd1 = new NodeData()
                            {
                                Id = id,
                                X = w.x,
                                Y = w.z,
                                MapId = key,
                                IsPokemonCenter = lookups.Count() == 1 && lookups[0].InternalName.EndsWith("PC0101")
                            };
                            id++;
                            var lookups2 = mapService.GetMapLookups(w.header);
                            NodeData wd2 = new NodeData()
                            {
                                Id = id,
                                X = w2.x,
                                Y = w2.z,
                                MapId = w.header,
                                IsPokemonCenter = lookups2.Count() == 1 && lookups2[0].InternalName.EndsWith("PC0101")
                            };
                            id++;
                            nodes.Add(wd1);
                            nodes.Add(wd2);
                            edges.Add(new EdgeData()
                            {
                                FromNodeId = wd1.Id,
                                ToNodeId = wd2.Id,
                                CapabilityMask = 0,
                                Cost = 60,
                            });
                            edges.Add(new EdgeData()
                            {
                                FromNodeId = wd2.Id,
                                ToNodeId = wd1.Id,
                                CapabilityMask = 0,
                                Cost = 60,
                            });
                            if (!maps.ContainsKey(key))
                            {
                                maps.Add(key, new List<NodeData>());
                            }
                            if (!maps.ContainsKey(w.header))
                            {
                                maps.Add(w.header, new List<NodeData>());
                            }
                            maps[key].Add(wd1);
                            maps[w.header].Add(wd2);
                        }
                    }
                }
            }
            foreach(var mapId in maps.Keys)
            {
                var mapEdges = await GenerateEdgesInMap(mapId);
                edges.AddRange(mapEdges);
            }

            string nodeJson = JsonConvert.SerializeObject(nodes);
            File.WriteAllText(Path.Combine(outputDir, "nodes.json"), nodeJson);
            string edgeJson = JsonConvert.SerializeObject(edges);
            File.WriteAllText(Path.Combine(outputDir, "edges.json"), edgeJson);

            using (FileStream fileStream = new FileStream(Path.Combine(outputDir, "graph.bin"),FileMode.Create, FileAccess.Write))
            // 2. Wrap it in a GZipStream for compression
            using (GZipStream compressionStream = new GZipStream(fileStream, CompressionLevel.Optimal))
            // 3. Create a StreamWriter to write text into the compression stream
            using (BinaryWriter streamWriter = new BinaryWriter(compressionStream))
            // 4. Use JsonSerializer to stream the object directly
            {
                streamWriter.Write(nodes.Count());
                for (int i = 0; i < nodes.Count(); i++)
                {
                    streamWriter.Write(nodes[i].Id);
                    streamWriter.Write(nodes[i].MapId);
                    streamWriter.Write(nodes[i].X);
                    streamWriter.Write(nodes[i].Y);
                    streamWriter.Write(nodes[i].IsPokemonCenter);
                }
                streamWriter.Write(edges.Count);
                for (int i = 0; i < edges.Count(); i++)
                {
                    streamWriter.Write(edges[i].FromNodeId);
                    streamWriter.Write(edges[i].ToNodeId);
                    streamWriter.Write(edges[i].CapabilityMask);
                    streamWriter.Write(edges[i].Cost);
                }
            }
        }

        private async Task<List<EdgeData>> GenerateEdgesInMap(int mapId)
        {
            List<EdgeData> edges = new List<EdgeData>();
            if(mapId == 190)
            {
                return edges;
            }
            Stopwatch sw = new Stopwatch();
            sw.Start();
            Console.WriteLine($"Starting A* search for map {mapId}");
            List<int> capabilityCombinations = GetAllCapabilityMasks(mapId);
            List<Task<List<EdgeData>>> tasks = new List<Task<List<EdgeData>>>();
            List<NodeData> nodes = maps[mapId];
            
            for(int i = 0; i < nodes.Count(); i++)
            {
                for(int j = 0; j < nodes.Count(); j++)
                {
                    if(i != j)
                    {
                        var newEdges = GenerateEdgesForTwoWarps(nodes[i], nodes[j], mapId, capabilityCombinations);
                        edges.AddRange(newEdges);
                    }
                }
            }
            //foreach(var task in tasks)
            //{
            //    edges.AddRange(await task);
            //}
            sw.Stop();
            Console.WriteLine($"Completed all A* searches for map {mapId} in {(double)sw.ElapsedMilliseconds / 1000.0}s");
            return edges;
        }

        public List<EdgeData> GenerateEdgesForTwoWarps(NodeData start, NodeData end, int mapId, List<int> capabilityCombinations)
        {
            List<(MapPoint, Capability)> potentialEdges = new List<(MapPoint, Capability)>();
            foreach(var capabilityMask in capabilityCombinations)
            {
                var capability = new Capability() { CapabilityMask = capabilityMask };
                var point = pathfindingService.ASearch(mapId, start.X, start.Y, end.X, end.Y, capability, 0, true);
                if(point is not null)
                {
                    potentialEdges.Add((point, capability));
                }
            }
            List<(MapPoint, Capability)> prunedPoints = PruneEdges(potentialEdges);
            List<EdgeData> edges = new List<EdgeData>();
            foreach(var prune in prunedPoints)
            {
                edges.Add(new EdgeData()
                {
                    FromNodeId = start.Id,
                    ToNodeId = end.Id,
                    CapabilityMask = prune.Item2.CapabilityMask,
                    Cost = prune.Item1.Cost
                });
            }
            return edges;
        }

        private List<(MapPoint, Capability)> PruneEdges(List<(MapPoint, Capability)> potentialEdges)
        {
            List<(MapPoint, Capability)> keepEdges = new List<(MapPoint, Capability)>();
            for (int i = 0; i < potentialEdges.Count; i++)
            {
                bool betterFound = false;
                for(int j = 0; j < potentialEdges.Count && !betterFound; j++)
                {
                    if(i != j)
                    {
                        // first comparison checks that j point has less capabilities 
                        // second comparison checks that j point cost is not better
                        // collective we check that the point doesn't give a better edge cost when using more capabilities
                        if ((potentialEdges[j].Item2.CapabilityMask & potentialEdges[i].Item2.CapabilityMask) == potentialEdges[j].Item2.CapabilityMask 
                            && potentialEdges[j].Item1.Cost <= potentialEdges[i].Item1.Cost)
                        {
                            betterFound = true;
                        }
                    }
                }
                if (!betterFound)
                {
                    keepEdges.Add(potentialEdges[i]);
                }
            }
            return keepEdges;
        }

        private List<int> GetAllCapabilityMasks(int mapId)
        {
            CapabilityType mapMask = GetMapCapabilities(mapId);
            return GetMapCapabilityCombinations(mapMask);
        }

        private List<int> GetMapCapabilityCombinations(CapabilityType accumulatedCapabilities)
        {
            // 1. Extract just the active flags into a fast, flat array of bytes
            // (Max 7 HMs means this array allocation is practically free)
            List<int> activeFlags = new List<int>();
            foreach (CapabilityType flag in Enum.GetValues<CapabilityType>())
            {
                if (flag != CapabilityType.None && (accumulatedCapabilities & flag) == flag)
                {
                    activeFlags.Add((int)flag);
                }
            }

            int n = activeFlags.Count;
            int totalCombinations = 1 << n; // 2^n combinations

            // Capacity pre-allocation keeps this blazing fast
            var generatedMasks = new List<int>(totalCombinations);

            // 2. Run your original, excellent combinations loop
            for (int i = 0; i < totalCombinations; i++)
            {
                int currentCombinationMask = 0;

                for (int j = 0; j < n; j++)
                {
                    if ((i & (1 << j)) != 0)
                    {
                        currentCombinationMask |= activeFlags[j];
                    }
                }

                generatedMasks.Add(currentCombinationMask);
            }

            return generatedMasks;
        }

        public CapabilityType GetMapCapabilities(int mapId)
        {
            Map map = mapService.GetMap(mapId, true);
            CapabilityType capabilityMask = CapabilityType.None;
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    byte tile = map[y, x];
                    if (tile == Constant.MAP_Cut)
                    {
                        capabilityMask |= CapabilityType.Cut;
                    }
                    else if (tile == Constant.MAP_Surf)
                    {
                        capabilityMask |= CapabilityType.Surf;
                    }
                    else if (tile == Constant.MAP_RockClimb)
                    {
                        capabilityMask |= CapabilityType.RockClimb;
                    }
                    else if (tile == Constant.MAP_RockSmash)
                    {
                        capabilityMask |= CapabilityType.RockSmash;
                    }
                    else if (tile == Constant.MAP_Waterfall)
                    {
                        capabilityMask |= CapabilityType.Waterfall;
                    }
                    else if(tile == Constant.MAP_Whirlpool)
                    {
                        capabilityMask |= CapabilityType.Whirlpool;
                    }
                }
            }
            if(map.Boulders.Length > 0)
            {
                capabilityMask |= CapabilityType.Strength;
            }
            return capabilityMask;
        }

        private BaseWarp? FindMatchingWarp(int mapId, BaseWarp w, Dictionary<short, BaseWarp[]> warpSource)
        {

            var warps = warpSource[w.header];
            foreach (var warp in warps)
            {
                if (warp.pos == w.anchor)
                {
                    return warp;
                }
            }
            return null;
        }
    }
}
