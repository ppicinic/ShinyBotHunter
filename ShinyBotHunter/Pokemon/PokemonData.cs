using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ShinyBotHunter.Pokemon
{
    public class PokemonData
    {
        public PokemonData()
        {
            Name = "Missingno";
            FormCode = "missingono";
        }
        [BsonRepresentation(BsonType.Int64)]
        public uint PID { get; set; }
        [BsonRepresentation(BsonType.Int32)]
        public ushort Checksum { get; set; }
        [BsonRepresentation(BsonType.Int32)]
        public ushort Species { get; set; }
        public byte Gender { get; set; }
        public string FormCode { get; set; }
        public string Name { get; set; }
        public int FormIndex { get; set; }
        [BsonRepresentation(BsonType.Int32)]
        public ushort HeldItem { get; set; }
        [BsonRepresentation(BsonType.Int64)]
        public uint OTId { get; set; }
        [BsonRepresentation(BsonType.Int32)]
        public ushort TID { get; set; }
        [BsonRepresentation(BsonType.Int32)]
        public ushort SID { get; set; }
        public byte Ability { get; set; }
        [BsonRepresentation(BsonType.Int32)]
        public ushort Move1 { get; set; }
        [BsonRepresentation(BsonType.Int32)]
        public ushort Move2 { get; set; }
        [BsonRepresentation(BsonType.Int32)]
        public ushort Move3 { get; set; }
        [BsonRepresentation(BsonType.Int32)]
        public ushort Move4 { get; set; }
        [BsonRepresentation(BsonType.Int64)]
        public uint IVs { get; set; }
        public string Nickname { get; set; } = "";
        public string OTName { get; set; } = "";
        public byte Nature { get; set; }
        public byte Level { get; set; }
        [BsonRepresentation(BsonType.Int32)]
        public ushort HP { get; set; }
        [BsonRepresentation(BsonType.Int32)]
        public ushort MaxHP { get; set; }
        [BsonRepresentation(BsonType.Int32)]
        public ushort Attack { get; set; }
        [BsonRepresentation(BsonType.Int32)]
        public ushort Defense { get; set; }
        [BsonRepresentation(BsonType.Int32)]
        public ushort Speed { get; set; }
        [BsonRepresentation(BsonType.Int32)]
        public ushort SpAttack { get; set; }
        [BsonRepresentation(BsonType.Int32)]
        public ushort SpDefense { get; set; }

        public byte IVHp => (byte)(IVs & 0x1F);
        public byte IVAtk => (byte)((IVs >> 5) & 0x1F);
        public byte IVDef => (byte)((IVs >> 10) & 0x1F);
        public byte IVSpe => (byte)((IVs >> 15) & 0x1F);
        public byte IVSpAtk => (byte)((IVs >> 20) & 0x1F);
        public byte IVSpDef => (byte)((IVs >> 25) & 0x1F);

        [BsonRepresentation(BsonType.Int32)]

        public ushort ShinyValue => (ushort)(TID ^ SID ^ (PID & 0xFFFF) ^ (PID >> 16));

        [BsonRepresentation(BsonType.Boolean)]
        public bool IsShiny => ShinyValue < 8;
    }
}
