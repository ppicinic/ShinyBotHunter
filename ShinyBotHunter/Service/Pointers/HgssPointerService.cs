using ShinyBotHunter.Emulators;

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

        public int PartySlots => -0x23F40;

        public int PartyPokemonSize => 0xEC;

        public int BoxPokemonSize => 136;

        public int BoxStorage => -0x148D8;

        public int BoxCount => 18;

        public int BoxCapacity => 30;

        public int BoxStride => 4096;

        public PartyAddressing PartyAddressing => PartyAddressing.Anchor;

        public PokemonScheme PokemonScheme => PokemonScheme.Gen4;
    }
}
