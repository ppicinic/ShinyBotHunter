using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using SharpCompress.Compressors.Xz;
using ShinyBotHunter.Pathfinding;
using System.Text.Json;
using System.Xml.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace ShinyBotHunter.Scripts.Maps
{
    public class MapLookup
    {
        public int BotMapId { get; set; }
        public string InternalName { get; set; } = null!;
    }
    public class MatrixProcessor
    {
        public void GenerateAllMaps()
        {
            Dictionary<ushort, MapLookup> reverseLookUp = new Dictionary<ushort, MapLookup>();
            var headers = ReadHeaderFile();
            ValidateOverworld(reverseLookUp, headers);
            GenerateReverseLookups(headers, reverseLookUp);
            var options = new JsonSerializerOptions { WriteIndented = true };
            string jsonString = System.Text.Json.JsonSerializer.Serialize(reverseLookUp, options);
            File.WriteAllText("D:/mapreverselookup.json", jsonString);
        }

        private void GenerateReverseLookups(List<MapHeader> headers, Dictionary<ushort, MapLookup> reverseLookUp)
        {
            int id = 30;
            foreach(var header in headers)
            {
                if (header.matrixId != 0 && header.matrixId != 212) // skip overworld and safari zone
                {
                    reverseLookUp.Add(header.id, new MapLookup() { BotMapId = id, InternalName = header.internalName });
                    
                    System.IO.BinaryReader readMatrix = new System.IO.BinaryReader(File.OpenRead($"D:\\ExtractedROMs\\Pokemon HeartGold_SDSME\\data\\a\\0\\4\\matrix\\{header.matrixId:D4}"));
                    byte width = readMatrix.ReadByte();
                    byte height = readMatrix.ReadByte();
                    bool hiddenLayer = readMatrix.ReadByte() == 1;
                    bool mapLayer = readMatrix.ReadByte() == 1;
                    byte nameLength = readMatrix.ReadByte();
                    ushort[,] map = new ushort[height, width];
                    if(id == 47)
                    {
                        Console.WriteLine($"{width}x{height}:{header.matrixId}:{header.internalName}:{header.id}");
                    }
                    for (int z = 0; z < nameLength; z++)
                    {
                        readMatrix.ReadByte();
                    }
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            map[y, x] = readMatrix.ReadUInt16();
                            if (map[y,x] == 208 || map[y,x] == 209 || map[y,x] == 210 || map[y,x] == 65535)
                            {
                                Console.WriteLine($"Map {header.id} has matrix file: {map[y, x]}");
                            }
                        }
                    }
                    string json = CreateJson(map);
                    File.WriteAllText($"D:/matrixMaps/{id:D4}.json", json);
                    id++;
                }
            }
        }

        public record MapHeader(string internalName, ushort id, int matrixId);
        public List<MapHeader> ReadHeaderFile()
        {
            List<MapHeader> result = new List<MapHeader>();
            var lines = File.ReadAllLines("D:/output.csv");
            for(ushort i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var parts = line.Split(new char[] {' ', '\t'}, StringSplitOptions.RemoveEmptyEntries);
                result.Add(new MapHeader(parts[0], i, Int32.Parse(parts[3])));
            }
            return result;
        }
        public void ValidateOverworld(Dictionary<ushort, MapLookup> reverseLookUp, List<MapHeader> mapHeader)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            System.IO.BinaryReader readMatrix = new System.IO.BinaryReader(File.OpenRead($"D:\\ExtractedROMs\\Pokemon HeartGold_SDSME\\data\\a\\0\\4\\matrix\\0000"));
            byte width = readMatrix.ReadByte();
            byte height = readMatrix.ReadByte();
            bool hiddenLayer = readMatrix.ReadByte() == 1;
            bool mapLayer = readMatrix.ReadByte() == 1;
            byte nameLength = readMatrix.ReadByte();
            ushort[,] map = new ushort[height, width];
            ushort[,] headers = new ushort[height, width]; 
            for (int z = 0; z < nameLength; z++)
            {
                readMatrix.ReadByte();
            }
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    headers[y, x] = readMatrix.ReadUInt16();
                    //Console.Write($"{matrixId:D4} ");
                }
                //Console.WriteLine();
            }

            if (hiddenLayer)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        readMatrix.ReadByte();
                    }
                }
            }
            HashSet<ushort> values = new HashSet<ushort>();
            if (mapLayer)
            {
                //Console.WriteLine(); Console.WriteLine();
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        ushort matrixId = readMatrix.ReadUInt16();
                        if (matrixId == 65535 || matrixId == 209 || matrixId == 210 || matrixId == 208 || matrixId == 70 || matrixId == 71)
                        {
                            matrixId = 65535;
                        }
                        else
                        {
                            values.Add(matrixId);
                        }
                        map[y, x] = matrixId;
                        //Console.Write($"{matrixId:D4} ");
                    }
                    //Console.WriteLine();
                }
            }
            //Console.WriteLine();
            //Console.WriteLine();
            HashSet<ushort> loadZones = new HashSet<ushort>();
            foreach (var v in CustomLoadZoneMappings.OverworldLoadZones.Values)
            {
                var list = v;
                foreach (var lz in list)
                {
                    if (loadZones.Contains(lz))
                    {
                        throw new Exception($"Duplication LZ in defintion: {lz}");
                    }
                    else
                    {
                        loadZones.Add(lz);
                    }
                }
            }
            foreach (var lz in loadZones)
            {
                if (!values.Contains(lz))
                {
                    throw new Exception($"LZ {lz} in definition does not exist in matrix file");
                }
            }
            var myList = values.ToList();
            myList.Sort();
            foreach (var lz in myList)
            {
                if (!loadZones.Contains(lz))
                {
                    throw new Exception($"LZ in matrix file is missing in definition: {lz}");
                }
            }
            foreach (int key in CustomLoadZoneMappings.OverworldLoadZones.Keys)
            {
                var loadZone = CustomLoadZoneMappings.OverworldLoadZones[key];
                ushort[,] newMap = new ushort[height, width];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        if (loadZone.Contains(map[y, x]))
                        {
                            newMap[y, x] = map[y, x];
                            if (!reverseLookUp.ContainsKey(headers[y, x])) 
                            {
                                try
                                {
                                    var headerId = headers[y, x];
                                    reverseLookUp.Add(headers[y, x], new MapLookup() { BotMapId = key, InternalName = mapHeader.First(x => x.id == headerId).internalName });
                                }
                                catch
                                {
                                    Console.WriteLine($"Failed on headerid: {headers[y, x]}");
                                    throw;
                                }
                            }
                            else if (reverseLookUp[headers[y,x]].BotMapId != key)
                            {
                                throw new Exception($"Reverse lookup conflict: {headers[y, x]}, {map[y, x]}, {key}");
                            }
                        }
                        else
                        {
                            newMap[y, x] = 65535;
                        }
                    }
                }
                string json = CreateJson(newMap);
                File.WriteAllText($"D:/matrixMaps/{key:D4}.json", json);
            }

        }

        private string CreateJson(ushort[,] newMap)
        {
            int rowCount = newMap.GetLength(0);
            int colCount = newMap.GetLength(1);

            // 1. Process each row
            var rows = Enumerable.Range(0, rowCount).Select(i =>
            {
                // 2. Format each number to be exactly 5 characters wide
                var formattedCells = Enumerable.Range(0, colCount)
                    .Select(j => $"{newMap[i, j],5}");

                // 3. Join cells with commas and wrap in brackets
                return "  [" + string.Join(",", formattedCells) + "]";
            });

            // 4. Join rows with a comma and newline
            return "[\n" + string.Join(",\n", rows) + "\n]";
        }
    }
}
