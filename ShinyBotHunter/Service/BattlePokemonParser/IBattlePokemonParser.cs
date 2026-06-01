using ShinyBotHunter.Pokemon;

namespace ShinyBotHunter.Service.BattlePokemonParser
{
    public interface IBattlePokemonParser
    {
        BattlePokemon ParseBattlePokemon(byte[] data, PokemonData pokemon);
        BattlePokemon? ParseBattlePokemon(byte[] data, List<PokemonData> pokemonState);
    }
}
