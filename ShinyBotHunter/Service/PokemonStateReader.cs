using ShinyBotHunter.Bot;
using ShinyBotHunter.Pokemon;

namespace ShinyBotHunter.Service
{
    public class PokemonState
    {
        public PokemonState()
        {
            BoxPokemon = new PokemonData?[18][];
            for (int i = 0; i < 18; i++)
            {
                BoxPokemon[i] = new PokemonData?[30];
            }
        }
        public PokemonData?[] PartyPokemon { get; set; } = new PokemonData[6];
        public PokemonData?[][] BoxPokemon { get; set; }
        public int PartySize { get; set; }
        public bool Search(PokemonData pokemon)
        {
            for(int i = 0; i < PartySize; i++) 
            {
                var compare = PartyPokemon[i]!;
                if(pokemon.PID == compare.PID && pokemon.Species == compare.Species)
                {
                    return true;
                }
            }
            for(int box = 0; box < 18; box++)
            {
                for(int slot = 0; slot < 30; slot++)
                {
                    var compare = BoxPokemon[box][slot];
                    if(compare is not null && compare.PID == pokemon.PID && compare.Species == pokemon.Species)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public List<PokemonData> ToList()
        {
            var list = new List<PokemonData>();
            for(int i = 0; i < PartySize; i++)
            {
                list.Add(PartyPokemon[i]!);
            }
            for(int box = 0; box < 18; box++)
            {
                for(int slot = 0; slot < 30; slot++)
                {
                    if (BoxPokemon[box][slot] is not null)
                    {
                        list.Add(BoxPokemon[box][slot]!);
                    }
                }
            }
            return list;
        }
    }
    public class PokemonStateReader
    {
        private HgssPokemonParser parser;
        public PokemonStateReader(HgssPokemonParser parser)
        {
            this.parser = parser;
        }

        public async Task<PokemonState> GetPokemonState(IBot bot)
        {
            PokemonState state = new PokemonState();
            uint anchor = await bot.ReadUIntAsync(0x21D4158);
            byte partyCount = await bot.ReadByteAsync(anchor - 0x23F44);
            byte[] partyData = await bot.ReadBytesAsync(await bot.ReadUIntAsync(0x21D4158) - 0x23F40, 0xEC * partyCount);
            state.PartySize = partyCount;
            for (uint i = 0; i < partyCount; i++)
            {
                var pokemon = parser.Parse(await bot.ReadBytesAsync(anchor - 0x23F40 + (i * 0xEC), 0xEC));
                while (pokemon is null || pokemon.IsCorrupt)
                {
                    await Task.Delay(100);
                    anchor = await bot.ReadUIntAsync(0x21D4158);
                    pokemon = parser.Parse(await bot.ReadBytesAsync(anchor - 0x23F40 + (i * 0xEC), 0xEC));
                }
                state.PartyPokemon[i] = pokemon;
            }
            //constantly polling is too slow. load the whole party data into memory

            bool[][] checkMap = new bool[30][];
            for (int i = 0; i < 18; i++)
            {
                checkMap[i] = new bool[30];
                for (int j = 0; j < 30; j++)
                {
                    checkMap[i][j] = false;
                }
            }
            anchor = await bot.ReadUIntAsync(0x21D4158);
            byte[] fullBoxData = await bot.ReadBytesAsync(anchor - 0x148D8, (18 * 4096));
            for (int box = 0; box < 18; box++)
            {
                for (int slot = 0; slot < 30; slot++)
                {

                    byte[] data = fullBoxData[((box * 4096) + (slot * 136))..((box * 4096) + (slot * 136) + 136)];
                    bool isEmpty = BitConverter.ToUInt32(data, 0) == 0 && BitConverter.ToUInt16(data, 4) == 0;
                    if (isEmpty)
                    {
                        checkMap[box][slot] = true;
                    }
                    else
                    {
                        var pokemon = parser.Parse(data);
                        if (pokemon is not null && !pokemon.IsCorrupt)
                        {
                            checkMap[box][slot] = true;
                            state.BoxPokemon[box][slot] = pokemon;
                        }
                    }
                }
            }
            for (uint box = 0; box < 18; box++)
            {
                for (uint slot = 0; slot < 30; slot++)
                {
                    if (!checkMap[box][slot])
                    {
                        PokemonData? boxPokemon = null;
                        do
                        {
                            await Task.Delay(100);
                            anchor = await bot.ReadUIntAsync(0x21D4158);
                            byte[] boxData = await bot.ReadBytesAsync((anchor - 0x148D8) + (box * 4096) + (slot * 136), 136);
                            boxPokemon = parser.Parse(boxData);
                        } while (boxPokemon is null || boxPokemon.IsCorrupt);
                        state.BoxPokemon[box][slot] = boxPokemon;
                    }
                }
            }
            return state;
        }
    }
}
