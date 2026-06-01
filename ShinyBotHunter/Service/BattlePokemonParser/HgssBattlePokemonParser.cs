using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Service.BattlePokemonParser
{
    public class HgssBattlePokemonParser : IBattlePokemonParser
    {
        public HgssBattlePokemonParser() 
        {
        }

        public BattlePokemon ParseBattlePokemon(byte[] data, PokemonData pokemon)
        {
            BattlePokemon result = ParseBattlePokemon(data);
            result.Pokemon = pokemon;
            return result;
        }

        private BattlePokemon ParseBattlePokemon(byte[] data)
        {
            //TODO 0x18 is where the modifier data is it's 9 bytes
            // byte 2 - stat affected by growl
            // byte 3 - def
            return new BattlePokemon()
            {
                Species = BitConverter.ToUInt16(data, 0),
                HP = BitConverter.ToUInt16(data, 0x4C),
                MaxHP = BitConverter.ToUInt16(data, 0x50),
                Level = data[0x34],
                Move1PP = data[0x2C],
                Move2PP = data[0x2D],
                Move3PP = data[0x2E],
                Move4PP = data[0x2F],
                PID = BitConverter.ToUInt32(data, 0x68)
            }; 
        }

        public BattlePokemon? ParseBattlePokemon(byte[] data, List<PokemonData> pokemonState)
        {
            var result = ParseBattlePokemon(data);
            for(int i = 0; i < pokemonState.Count(); i++)
            {
                if(result.Species == pokemonState[i].Species && result.PID == pokemonState[i].PID)
                {
                    result.Pokemon = pokemonState[i];
                    return result;
                }
            }
            return null;
        }
    }
}
