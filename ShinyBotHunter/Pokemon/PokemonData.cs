namespace ShinyBotHunter.Pokemon
{
    public class PokemonData
    {
        public uint PID { get; set; }
        public ushort Checksum { get; set; }
        public ushort Species { get; set; }
        public ushort HeldItem { get; set; }
        public uint OTId { get; set; }
        public ushort TID { get; set; }
        public ushort SID { get; set; }
        public byte Ability { get; set; }
        public ushort Move1 { get; set; }
        public ushort Move2 { get; set; }
        public ushort Move3 { get; set; }
        public ushort Move4 { get; set; }
        public uint IVs { get; set; }
        public string Nickname { get; set; } = "";
        public string OTName { get; set; } = "";
        public byte Nature { get; set; }
        public byte AbilitySlot { get; set; }
        public byte Level { get; set; }
        public ushort HP { get; set; }
        public ushort MaxHP { get; set; }
        public ushort Attack { get; set; }
        public ushort Defense { get; set; }
        public ushort Speed { get; set; }
        public ushort SpAttack { get; set; }
        public ushort SpDefense { get; set; }

        public byte IVHp => (byte)(IVs & 0x1F);
        public byte IVAtk => (byte)((IVs >> 5) & 0x1F);
        public byte IVDef => (byte)((IVs >> 10) & 0x1F);
        public byte IVSpe => (byte)((IVs >> 15) & 0x1F);
        public byte IVSpAtk => (byte)((IVs >> 20) & 0x1F);
        public byte IVSpDef => (byte)((IVs >> 25) & 0x1F);

        public ushort ShinyValue => (ushort)(TID ^ SID ^ (PID & 0xFFFF) ^ (PID >> 16));
        public bool IsShiny => ShinyValue < 8;
    }
}
