using System.Text;

namespace ShinyBotHunter.Scripts.Maps
{    
    public record MapHeaderRecord(
    int ID,
    string InternalName,
    byte Unknown1, byte Unknown2,
    ushort MapFile,
    ushort MatrixID,
    ushort ScriptID,
    ushort TextID,
    ushort MusicDay,
    ushort MusicNight,
    ushort Weather,
    ushort CameraID,
    string DisplayName, // From nameText[]
    byte Extra1, byte Extra2, byte Extra3, byte Extra4);
    public class MapHeaderProcessor
    {
        public List<MapHeaderRecord> LoadAllHeaders(
            string mapTablePath,
            string arm9Path,
            int headerCount)
        {
            var headers = new List<MapHeaderRecord>();

            // Open both streams simultaneously
            using var tableStream = File.OpenRead(mapTablePath);
            using var arm9Stream = File.OpenRead(arm9Path);
            using var tableReader = new BinaryReader(tableStream);
            using var arm9Reader = new BinaryReader(arm9Stream);
            arm9Reader.BaseStream.Position = 0xEEDBC;

            for (int i = 0; i < headerCount; i++)
            {
                // 1. Read Internal Name (16-byte fixed length)
                byte[] nameBytes = tableReader.ReadBytes(16);
                string internalName = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');

                // 2. Read Map Attributes from ARM9/Binary
                // We use ReadUInt16() to replace the (byte + (byte << 8)) logic
                headers.Add(new MapHeaderRecord(
                    ID: i,
                    InternalName: internalName,
                    Unknown1: arm9Reader.ReadByte(),
                    Unknown2: arm9Reader.ReadByte(),
                    MapFile: (ushort)((ushort)arm9Reader.ReadByte() + ((ushort)arm9Reader.ReadByte() << 8)),
                    MatrixID: arm9Reader.ReadUInt16(),
                    ScriptID: arm9Reader.ReadUInt16(),
                    TextID: arm9Reader.ReadUInt16(),
                    MusicDay: arm9Reader.ReadUInt16(),
                    MusicNight: arm9Reader.ReadUInt16(),
                    Weather: arm9Reader.ReadUInt16(),
                    CameraID: arm9Reader.ReadUInt16(),
                    DisplayName: arm9Reader.ReadUInt16().ToString(), // Area Name Lookup
                    Extra1: arm9Reader.ReadByte(),
                    Extra2: arm9Reader.ReadByte(),
                    Extra3: arm9Reader.ReadByte(),
                    Extra4: arm9Reader.ReadByte()
                ));
            }

            return headers;
        }

        public void ExportToCsv(List<MapHeaderRecord> headers, string outputPath)
        {
            var csv = new StringBuilder();
            csv.AppendLine("ID,InternalName,Texture1,Texture2,MapFile,MatrixId,ScriptID,TextID,MusicDay");

            foreach (var h in headers)
            {
                csv.AppendLine($"{h.ID},\"{h.InternalName}\",{h.Unknown1},{h.Unknown2},{h.MapFile},{h.MatrixID},{h.ScriptID},{h.TextID},{h.MusicDay}");
            }

            File.WriteAllText(outputPath, csv.ToString());
        }

        public void FindMissingMatrixId()
        {
            var lines = File.ReadAllLines("D:\\output.csv");
            HashSet<int> ids = new HashSet<int>();
            foreach(var line in lines)
            {
                var parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                ids.Add(Int32.Parse(parts[3]));
            }
            var idList = ids.ToList();
            idList.Sort();
            for(int i = 1; i < idList.Count(); i++) 
            {
                if (idList[i] != idList[i - 1] + 1)
                {
                    Console.WriteLine(idList[i]);
                }
            }
        }
    }
}
