namespace ShinyBotHunter.Service.Pointers
{
    public interface IPointerService
    {
        uint Anchor { get; }
        uint BattlePokemon { get; }
        uint BattleEnemyPokemon { get; }
        uint BattlePartyPokemon { get; }
        int PartySize { get; }
        uint EnemyAnchor { get; }
        uint EnemyCount { get; }
        uint Enemy { get; } // this is encrypted Pokemon Data of the enemy
        uint BattleMenuState { get; }

    }
}
