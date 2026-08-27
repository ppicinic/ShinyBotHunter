using ShinyBotHunter.Emulators;

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

        /// <summary>Offset from the dereferenced anchor to the first party slot.</summary>
        int PartySlots { get; }

        /// <summary>Bytes in a party-sized stored pokemon, including its battle stats.</summary>
        int PartyPokemonSize { get; }

        /// <summary>Bytes in a box-sized stored pokemon, which omits the battle stats.</summary>
        int BoxPokemonSize { get; }

        /// <summary>Offset from the dereferenced anchor to the start of box storage.</summary>
        int BoxStorage { get; }

        int BoxCount { get; }

        int BoxCapacity { get; }

        /// <summary>Bytes one box occupies, including any padding after its slots.</summary>
        int BoxStride { get; }

        /// <summary>Whether <see cref="Anchor"/> is a pointer to dereference or the party itself.</summary>
        PartyAddressing PartyAddressing { get; }

        /// <summary>Which generation's structure the emulator validates party slots against.</summary>
        PokemonScheme PokemonScheme { get; }
    }
}
