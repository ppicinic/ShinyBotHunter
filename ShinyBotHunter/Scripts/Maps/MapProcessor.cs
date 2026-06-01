using Newtonsoft.Json;
using System.IO.Compression;
using System.Reflection.Metadata;
namespace ShinyBotHunter.Scripts.Maps
{
    public class EventData
    {
        public ObjectData[] objects { get; set; } = [];
        public WarpData[] warps { get; set; } = [];
    }

    public class JsonEventData
    {
        public ObjectData[] objects { get; set; } = [];
        public JsonWarpData[] warps { get; set; } = [];
    }

    public class WarpData
    {
        public int header { get; set; }
        public int x { get; set; }
        public int z { get; set; }
        public int anchor { get; set; }
        public int pos { get; set; }
    }
    public class JsonWarpData
    {
        public string header { get; set; } = null!;
        public int x { get; set; }
        public int z { get; set; }
        public int anchor { get; set; }
    }
    public class ObjectData
    {
        public string id { get; set; } = null!;
        public string eventFlag { get; set; } = null!;
        public int xRange { get; set; }
        public int yRange { get; set; }
        public int x { get; set; }
        public int z { get; set; }
        public string scriptid { get; set; } = null!;
    }

    public class BoulderData
    {
        public int X { get; set; }
        public int Y { get; set; }
    }
    public class MapProcessor
    {
        public void ExtractMovePermissionsFile()
        {
            var mapFiles = Directory.GetFiles("D:\\ExtractedROMs\\Pokemon HeartGold_SDSME\\data\\a\\0\\6\\map");
            foreach(var mapFile in mapFiles)
            {
                string fileName = Path.GetFileName(mapFile);
                var reader = new System.IO.BinaryReader(File.OpenRead(mapFile));
                var writer = new System.IO.BinaryWriter(File.Create($"D:\\Temp\\MovePermissions\\{fileName}"));
                reader.ReadUInt32();
                reader.ReadUInt32();
                reader.BaseStream.Position += 0xA;
                reader.BaseStream.Position = 0x14 + reader.ReadUInt16();
                for(int i = 0; i < 1024; i ++)
                {
                    writer.Write(reader.ReadByte());
                    writer.Write(reader.ReadByte());
                }
                writer.Flush();
            }
        }
        public (Dictionary<int, WarpData[]>, Dictionary<int, ObjectData[]>) GatherEventFiles(Dictionary<int, MapLookup> reverseLookup, Dictionary<int, List<int>> forwardLookup)
        {
            Dictionary<string, int> warpMapping = GetWarpNames();
            Dictionary<int, WarpData[]> result = new Dictionary<int, WarpData[]>();
            Dictionary<int, ObjectData[]> result2 = new Dictionary<int, ObjectData[]>();
            string[] files = Directory.GetFiles("D:\\Temp\\pokeheartgold\\files\\fielddata\\eventdata\\zone_event");
            //Console.WriteLine(String.Join("\n", files));
            Dictionary<(int, int), WarpData> warpMap = new Dictionary<(int, int), WarpData>();
            foreach(var botMapId in forwardLookup.Keys)
            {
                EventData data = new EventData();
                List<ObjectData> objects = new List<ObjectData>();
                List<WarpData> warps = new List<WarpData>();
                foreach (var headerId in forwardLookup[botMapId])
                {
                    string internalName = reverseLookup[headerId].InternalName;
                    //Console.WriteLine(internalName);
                    var file = files.FirstOrDefault(x => x.Contains(internalName));
                    //Console.WriteLine(file);
                    if(file is not null && file.Length > 0)
                    {
                        var fileData = JsonConvert.DeserializeObject<JsonEventData>(File.ReadAllText(file));
                        objects.AddRange(fileData!.objects);
                        for(int i = 0; i < fileData!.warps.Length; i++)
                        {
                            var warp = fileData!.warps[i];
                            // we skip the 4095 map and the safari zone
                            int id = -1;
                            int originalId = -1;
                            if (warpMapping.ContainsKey(warp.header))
                            {
                                if (warpMapping[warp.header] != 357)
                                {
                                    originalId = warpMapping[warp.header];
                                    id = reverseLookup[originalId].BotMapId;
                                }
                            }
                            if (id != -1)
                            {
                                var newWarp = new WarpData()
                                {
                                    anchor = warp.anchor,
                                    header = id,
                                    x = warp.x,
                                    z = warp.z,
                                    pos = warps.Count()
                                };
                                if (warpMap.ContainsKey((originalId, warp.anchor)))
                                {
                                    var connectingWarp = warpMap[(originalId, warp.anchor)];
                                    newWarp.anchor = connectingWarp.pos;
                                    connectingWarp.anchor = newWarp.pos;
                                }
                                warpMap.Add((headerId, i), newWarp);
                                warps.Add(newWarp);

                            }
                        }
                    }
                }
                result.Add(botMapId, warps.ToArray());
                result2.Add(botMapId, objects.ToArray());
            }
            Dictionary<int, WarpData[]> result3 = new Dictionary<int, WarpData[]>();
            foreach(int id in result.Keys)
            {
                List<WarpData> warpsToKeep = new List<WarpData>();
                foreach(var warp in result[id])
                {
                    if (true)//result.ContainsKey(warp.header) && result[warp.header].Length > warp.anchor && result[warp.header][warp.anchor].anchor == warp.pos)
                    {
                        warpsToKeep.Add(warp);
                    }
                }
                result3.Add(id, warpsToKeep.ToArray());
            }
            string json = JsonConvert.SerializeObject(result3);
            File.WriteAllText("D:\\Temp\\warp.json", json); 
            string json2 = JsonConvert.SerializeObject(result2);
            File.WriteAllText("D:\\Temp\\objects.json", json2);
            return (result, result2);
        }

        private Dictionary<string, int> GetWarpNames()
        {
            Dictionary<string, int> mapping = new Dictionary<string, int>();
            var lines = File.ReadAllLines("D:\\Temp\\pokeheartgold\\include\\constants\\maps.h");
            for(int i = 3; i < 544; i++)
            {
                var line = lines[i];
                var parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                mapping.Add(parts[1], Int32.Parse(parts[2]));
            }
            return mapping;
        }

        public void GenerateMapFiles()
        {
            List<(byte[,], int)> maps = new List<(byte[,], int)>();
            var sourceFiles = Directory.GetFiles("D:\\matrixMaps").ToList();
            Dictionary<int, List<BoulderData>> boulderData = new Dictionary<int, List<BoulderData>>();
            sourceFiles.Sort();
            //Console.WriteLine(String.Join("\n", sourceFiles));
            var reverseMap = JsonConvert.DeserializeObject<Dictionary<int, MapLookup>>(File.ReadAllText("D:\\mapreverselookup.json"));
            Dictionary<int, List<int>> forwardLookup = new Dictionary<int, List<int>>();
            foreach(var (key, value) in reverseMap!)
            {
                if (!forwardLookup.ContainsKey(value.BotMapId))
                {
                    forwardLookup.Add(value.BotMapId, new List<int>());
                }
                forwardLookup[value.BotMapId].Add(key);
            }

            var eventData = GatherEventFiles(reverseMap!, forwardLookup);
            foreach (var sourceFile in sourceFiles)
            {
                int id = Int32.Parse(Path.GetFileName(sourceFile.Split(".json")[0]));
                maps.Add(GenerateMapFile(sourceFile, id, eventData.Item1, eventData.Item2, boulderData));
            }
            WriteMapToFile(maps);
            WriteBoulderFile(boulderData);
        }

        private void WriteBoulderFile(Dictionary<int, List<BoulderData>> boulderData)
        {
            string json = JsonConvert.SerializeObject(boulderData);
            File.WriteAllText($"D:\\Temp\\boulders.json", json);
        }

        private (byte[,], int) GenerateMapFile(string sourceFile, int id, Dictionary<int, WarpData[]> warps, Dictionary<int, ObjectData[]> objectData, Dictionary<int, List<BoulderData>> boulders)
        {
            var sourceMap = (JsonConvert.DeserializeObject<int[,]>(File.ReadAllText(sourceFile)))!;
            int height = sourceMap.GetLength(0);
            int width = sourceMap.GetLength(1);
            byte[,] map = new byte[height * 32, width * 32];
            for (int outY = 0; outY < height; outY++)
            {
                for (int outX = 0; outX < width; outX++)
                {
                    if (sourceMap[outY,outX] == 65535)
                    {
                        for (int y = 0; y < 32; y++)
                        {
                            for (int x = 0; x < 32; x++)
                            {
                                map[outY * 32 + y, outX * 32 + x] = 0x80;
                            }
                        }
                    }
                    else
                    {
                        int mapFileId = sourceMap[outY,outX];
                        var reader = new System.IO.BinaryReader(File.OpenRead($"D:\\Temp\\MovePermissions\\{mapFileId:D4}"));
                        for (int y = 0; y < 32; y++)
                        {
                            for (int x = 0; x < 32; x++)
                            {
                                byte mapValue = reader.ReadByte();
                                byte permValue = reader.ReadByte();
                                byte value = 0x00;
                                if (mapValue == 0x69 || mapValue == 0x6E)
                                {
                                    // We can remove warps on the map but this could be wrong, unsure.
                                    //WarpData? warp = null;
                                    //if(id == 0)
                                    //{
                                    //    Console.WriteLine($"{outX * 32 + x},{outY * 32 + y}");
                                    //}
                                    //if (warps.ContainsKey(id))
                                    //{
                                    //    warp = warps[id].Where(t => t.z == outY * 32 + y && t.x == outX * 32 + x).FirstOrDefault();
                                    //}
                                    //if (warp is not null)
                                    //{
                                        value = 0x01;
                                    //}
                                    //else
                                    //{
                                    //    value = 0x80;
                                    //}
                                }
                                else if (mapValue == 0x11)
                                {
                                    value = 0x09;
                                }
                                else if (mapValue == 0x13)
                                {
                                    value = 0x0D;
                                }
                                else if (mapValue == 0x06 || mapValue == 0x86 || mapValue == 0xE1 || mapValue == 0xE5 || mapValue == 0x80 || mapValue == 0x3E 
                                            || permValue == 0x80 && mapValue != 0x38 && mapValue != 0x39 && mapValue != 0x3B)
                                {
                                    value = 0x80;
                                }
                                else if (mapValue == 0x6D || mapValue == 0x6C || mapValue == 0x6F || mapValue == 0x65 || mapValue == 0x63 || mapValue == 0x5F || mapValue == 0x6A)
                                {
                                    value = 0x02;
                                }
                                else if (mapValue == 0x15 || mapValue == 0x10)
                                {
                                    value = 0x04;
                                }
                                else if (mapValue == 0x38)
                                {
                                    value = 0x05;
                                }
                                else if (mapValue == 0x02 || mapValue == 0x03)
                                {
                                    value = 0x03;
                                }
                                else if(mapValue == 0x3B)
                                {
                                    value = 0x07;
                                }
                                else if(mapValue == 0x39)
                                {
                                    value = 0x06;
                                }
                                else if(mapValue == 0x4B || mapValue == 0x4C)
                                {
                                    value = 0x08;
                                }
                                else if(mapValue == 0x70)
                                {
                                    value = 0x0A;
                                }
                                else if (mapValue == 0x71 || mapValue == 0x72 || mapValue == 0x73)
                                {
                                    value = 0x0C;
                                }
                                else if (mapValue == 0x20)
                                {
                                    value = 0x0E;
                                }
                                map[outY * 32 + y, outX * 32 + x] = value;
                            }
                        }
                    }
                }
            }
            // total:      183
            // cut:        48
            // rock_smash: 106
            // strength:   29
            if (objectData.ContainsKey(id))
            {
                var objects = objectData[id];
                foreach (var obj in objects)
                {
                    if (obj.scriptid == "std_field_cut")
                    {
                        map[obj.z, obj.x] = 0x0F;
                    }
                    else if (obj.scriptid == "std_field_rock_smash")
                    {
                        map[obj.z, obj.x] = 0x10;
                    }
                    else if (obj.scriptid == "std_field_strength")
                    {
                        if (!boulders.ContainsKey(id))
                        {
                            boulders.Add(id, new List<BoulderData>());
                        }
                        boulders[id].Add(new BoulderData() { X = obj.x, Y = obj.z });
                        map[obj.z, obj.x] = 0x00;
                    }
                    else if(!obj.eventFlag.StartsWith("FLAG_HIDE") && !obj.eventFlag.StartsWith("FLAG_MAPTEMP"))
                    {
                        try
                        {
                            map[obj.z, obj.x] = 0x80;
                            if (obj.xRange > 0 || obj.yRange > 0)
                            {
                                for (int y = 0; y <= obj.yRange; y++)
                                {
                                    for (int x = 0; x <= obj.xRange; x++)
                                    {
                                        if (!(x == 0 && y == 0))
                                        {
                                            if (obj.z + y < height * 32 && obj.x + x < width * 32)
                                            {
                                                UpdateMapWithMovingNpc(map, obj.z + y, obj.x + x); 
                                            }
                                            if (obj.z + y < height * 32 && obj.x - x >= 0)
                                            {
                                                UpdateMapWithMovingNpc(map, obj.z + y, obj.x - x);
                                            }
                                            if (obj.z - y >= 0 && obj.x + x < width * 32)
                                            {
                                                UpdateMapWithMovingNpc(map, obj.z - y, obj.x + x);
                                            }
                                            if (obj.z - y >= 0 && obj.x - x >= 0)
                                            {
                                                UpdateMapWithMovingNpc(map, obj.z - y, obj.x - x);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch
                        {
                            Console.WriteLine($"Map id: {id}, x: {obj.x}, y: {obj.z}");
                            throw;
                        }
                    }
                }
            }
            WriteMapToFile(map, id);
            return (map, id);
        }

        private void UpdateMapWithMovingNpc(byte[,] map, int y, int x)
        {
            if (!(map[y,x] == 0x80 || map[y,x] == 0x01 || map[y,x] == 0x02 || map[y,x] == 0x05 || map[y, x] == 0x06 || map[y, x] == 0x07 || map[y, x] == 0x08 || map[y, x] == 0x09 || map[y, x] == 0x0D 
                || map[y, x] == 0x0E || map[y, x] == 0x0F || map[y, x] == 0x10))
            {
                map[y, x] = 0x11;
            }
        }

        private void WriteMapToFile(byte[,] map, int id)
        {
            //string json = JsonConvert.SerializeObject(map);
            //File.WriteAllText($"D:\\Temp\\MapFiles\\{id:D4}.json", json);
            int rowCount = map.GetLength(0);
            int colCount = map.GetLength(1);

            // 1. Process each row
            var rows = Enumerable.Range(0, rowCount).Select(i =>
            {
                // 2. Format each byte as 0x followed by 2-digit hex (e.g., 0x0A)
                var formattedCells = Enumerable.Range(0, colCount)
                    .Select(j => $"0x{map[i, j]:X2}");

                // 3. Join cells with commas and wrap in brackets
                return "  [" + string.Join(", ", formattedCells) + "]";
            });

            // 4. Join rows with a comma and newline to form the JSON array
            string json = "[\n" + string.Join(",\n", rows) + "\n]";
            File.WriteAllText($"D:\\Temp\\MapFiles\\{id:D4}.json", json);

        }

        private void WriteMapToFile(List<(byte[,], int)> maps)
        {
            using (FileStream fileStream = new FileStream("D:\\Temp\\maps.bin", FileMode.Create))
            {
                // Use GZipStream for compression. 
                // CompressionLevel.Optimal gives the best balance of speed vs size.
                using (GZipStream compressionStream = new GZipStream(fileStream, CompressionLevel.SmallestSize))
                {
                    using (BinaryWriter writer = new BinaryWriter(compressionStream))
                    {
                        writer.Write(maps.Count());
                        foreach(var value in maps)
                        {
                            var map = value.Item1;
                            int id = value.Item2;
                            int height = map.GetLength(0);
                            int width = map.GetLength(1);
                            writer.Write(id);
                            writer.Write(height);
                            writer.Write(width);

                            for (int y = 0; y < height; y++)
                            {
                                for (int x = 0; x < width; x++)
                                {
                                    writer.Write(map[y, x]);
                                }
                            }
                        }
                        writer.Flush();
                    }
                }
            }
        }
    }

    public enum MapValues
    {
        Free = 0,
        Portal1 = 1,
        Portal2 = 2,
        Grass = 3,
        Surf = 4,
        RightLedge = 5,
        LeftLedge = 6,
        DownLedge = 7,
        RockClimb = 0x08,
        Whirlpool = 0x09,
        BridgeStart = 0x0A,
        BridgeMiddle = 0x0B,
        Bridge = 0x0C,
        Waterfall = 0x0D,
        Ice = 0x0E,
        Cut = 0x0F,
        RockSmash = 0x10,
        WalkingTrainer = 0x11,
        Blocked = 0x80,

    }

    /**
     * 0x00 - free
     * 0x01 - portal1 0x69 and 0x6E this is like doors and some gatehouses
     * 0x02 - portal2
     * 0x03 - grass
     * 0x80 - blocked
     * 
     **/
}
