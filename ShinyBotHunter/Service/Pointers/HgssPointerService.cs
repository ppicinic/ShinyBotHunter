namespace ShinyBotHunter.Service.Pointers
{
    public class HgssPointerService : IPointerService
    {
        public uint Anchor => 0x21D4158;

        public uint BattlePokemon => 0x25E2C;

        public uint BattleEnemyPokemon => 0x25EEC;
        public uint BattlePartyPokemon => 0x2AAB0;
        public int PartySize => -0x23F44;

        public uint EnemyAnchor => 0x6930;

        public uint EnemyCount => 0xC14;

        public uint Enemy => 0xC18;

        public uint BattleMenuState => 0x230EC;
    }
}
