using Newtonsoft.Json;
using System.IO.Compression;

namespace ShinyBotHunter.Scripts.Maps
{
    public class Node 
    {
        public short Id { get; set; }
        public int MapId { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public bool IsPokemonCenter { get; set; }
        public List<Edge> Edges { get; set; } = new List<Edge>();
    }
    public class Edge
    {
        public short FromNodeId { get; set; }
        public short ToNodeId { get; set; }
        public Node ToNode { get; set; } = null!;
        public int CapabilityMask { get; set; }
        public int Cost { get; set; }
    }
    public class GraphService
    {
        private Dictionary<short, Node> nodes = new Dictionary<short, Node>();
        private Dictionary<int, List<Node>> maps = new Dictionary<int, List<Node>>(); 
        private Dictionary<int, MapLookup> mapLookup = new Dictionary<int, MapLookup>();

        public static GraphService Instance { get; private set; } = new GraphService();
        private GraphService()
        {
            LoadGraph();
        }
        private void LoadGraph()
        {
            Console.WriteLine("Load graphs");
            using (FileStream compressedFileStream = new FileStream(".\\data\\HGSS\\graph.bin", FileMode.Open))
            using (GZipStream decompressor = new GZipStream(compressedFileStream, CompressionMode.Decompress))
            using (BinaryReader reader = new BinaryReader(decompressor))
            {
                int nodeCount = reader.ReadInt32();
                for(int i = 0; i < nodeCount; i++)
                {
                    
                    short id = reader.ReadInt16();
                    short mapId = reader.ReadInt16();
                    short x = reader.ReadInt16();
                    short y = reader.ReadInt16();
                    bool isCenter = reader.ReadBoolean();
                    var node = new Node()
                    {
                        Id = id,
                        MapId = mapId,
                        X = x,
                        Y = y,
                        IsPokemonCenter = isCenter
                    };
                    nodes.Add(node.Id, node);
                    if (!maps.ContainsKey(node.MapId))
                    {
                        maps.Add(node.MapId, new List<Node>());
                    }
                    maps[node.MapId].Add(node);
                }
                int edgeCount = reader.ReadInt32();
                for(int i = 0; i < edgeCount; i++)
                {
                    short fromId = reader.ReadInt16();
                    short toId = reader.ReadInt16();
                    int capability = reader.ReadInt32();
                    int cost = reader.ReadInt32();
                    Node fromNode = nodes[fromId];
                    Node toNode = nodes[toId];
                    Edge edge = new Edge()
                    {
                        FromNodeId = fromId,
                        ToNodeId = toId,
                        ToNode = toNode,
                        CapabilityMask = capability,
                        Cost = cost
                    };
                    fromNode.Edges.Add(edge);
                }
            }
            mapLookup = JsonConvert.DeserializeObject<Dictionary<int, MapLookup>>(File.ReadAllText(".\\data\\HGSS\\mapreverselookup.json"))!;
        }

        public List<Node> GetNodesFromMap(int mapId, bool isInternalId = false)
        {
            if (isInternalId)
            {
                return maps[mapId];
            }
            else
            {
                return maps[mapLookup[mapId].BotMapId];
            }
        }
    }
}
