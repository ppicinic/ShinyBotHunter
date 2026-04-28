using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Pokemon
{
    public class PersistPokemonData
    {
        public required PokemonData PokemonData { get; set; }
        public DateTime DateTime { get; set; }
        public GameFlavor Game { get; set; }
        public required string Phase { get; set; }
    }
}
