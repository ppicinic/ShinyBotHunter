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
        /// <summary>
        /// The block this was decoded from, kept so anything needing the original encoding -- legality
        /// analysis reads bytes, not a parsed model -- does not have to re-encode and risk disagreeing
        /// with the game. Not persisted.
        /// </summary>
        [BsonIgnore]
        public byte[]? RawBytes { get; set; }

        public uint PID { get; set; }
        [BsonRepresentation(BsonType.Int32)]
        public ushort Checksum { get; set; }
        [BsonRepresentation(BsonType.Int32)]
        public ushort Species { get; set; }
        public byte Gender { get; set; }
        [BsonIgnore]
        public bool IsCorrupt { get; set; }
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

        /// <summary>
        /// Bit 30 of the IV word at 0x38. An egg stores the species, PID and IVs of the pokemon
        /// inside it, so this flag is the only thing separating an egg sitting in the party from the
        /// pokemon it hatches into. The game clears it the moment the hatch animation starts, which
        /// is how a hatch is detected.
        ///
        /// Not persisted: an egg is a transient state of a party slot, not a property of the
        /// encounter that gets written out once it hatches.
        /// </summary>
        [BsonIgnore]
        public bool IsEgg => ((IVs >> 30) & 1) == 1;

        [BsonIgnore]
        public byte Move1PP { get; internal set; }
        [BsonIgnore]
        public byte Move2PP { get; internal set; }
        [BsonIgnore]
        public byte Move3PP { get; internal set; }
        [BsonIgnore]
        public byte Move4PP { get; internal set; }
        [BsonIgnore]
        public Move[] Moves { get
            {
                return [new Move() { Id = Move1, PP = Move1PP }, new Move() { Id = Move2, PP = Move2PP }, new Move() { Id = Move3, PP = Move3PP }, new Move() { Id = Move4, PP = Move4PP }];
            } 
        }
        [BsonIgnore]
        public byte EVHP { get; set; }
        [BsonIgnore]
        public byte EVAtk { get; set; }
        [BsonIgnore]
        public byte EVDef { get; set; }
        [BsonIgnore]
        public byte EVSpAtk { get; set; }
        [BsonIgnore]
        public byte EVSpDef { get; set; }
        [BsonIgnore]
        public byte EVSpeed { get; set; }
        [BsonIgnore]
        public byte Happiness { get; set; }
        [BsonIgnore]
        public byte Beauty { get; set; }

        /// <summary>
        /// Status condition byte (0x88). Bits 0-2 are the remaining sleep rounds; bit 3 poisoned,
        /// bit 4 burned, bit 5 frozen, bit 6 paralyzed, bit 7 toxic. Live state, so not persisted.
        /// </summary>
        [BsonIgnore]
        public byte Status { get; set; }

        [BsonIgnore]
        public bool IsPoisoned => (Status & (StatusPoisoned | StatusToxic)) != 0;

        private const byte StatusPoisoned = 0x08;
        private const byte StatusToxic = 0x80;
    }

    public class Move
    {
        public int Id { get; set; }
        public int PP { get; set; }
    }
}
