using Mono.TextTemplating;
using ShinyBotHunter.Bot;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Scripts;
using ShinyBotHunter.Service.BattlePokemonParser;
using ShinyBotHunter.Service.Pointers;
using System.Diagnostics;

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

    public class PokemonBattleState
    {
        public BattlePokemon ActivePokemon { get; set; } = null!;
        public BattlePokemon EnemyPokemon { get; set; } = null!;
        public List<PokemonData> PartyPokemon { get; set; } = new List<PokemonData>();

        public Move[] Moves
        {
            get
            {
                var moves = ActivePokemon.Pokemon.Moves;
                for(int i = 0; i < moves.Length; i++)
                {
                    moves[i].PP = ActivePokemon.MovePP[i];
                }
                return moves;
            }
        }
    }
    public class PokemonStateReader
    {
        private HgssPokemonParser parser;
        private IPointerService pointerService;
        private IBattlePokemonParser battleParser;
        public PokemonStateReader(HgssPokemonParser parser, IServiceProvider provider)
        {
            this.parser = parser;
            this.pointerService = provider.GetRequiredKeyedService<IPointerService>(GameFlavor.HGSS);
            this.battleParser = provider.GetRequiredKeyedService<IBattlePokemonParser>(GameFlavor.HGSS);
        }

        /// <summary>
        /// Reads only the field party, leaving the box data empty. Use this when the boxes are not
        /// needed -- the box read in GetPokemonState pulls 72KB and is far too slow for interactive use.
        /// </summary>
        public async Task<PokemonState> GetPartyState(IMemoryClient bot)
        {
            PokemonState state = new PokemonState();
            uint anchor = await bot.ReadUIntAsync(0x21D4158);
            byte partyCount = await bot.ReadByteAsync(anchor - 0x23F44);
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
            return state;
        }

        public async Task<PokemonState> GetPokemonState(IMemoryClient bot)
        {
            PokemonState state = await GetPartyState(bot);
            uint anchor;
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

        public async Task<PokemonBattleState?> GetPokemonBattleState(IMemoryClient bot, int timeoutMs = 1000)
        {
            uint anchor = await bot.ReadUIntAsync(pointerService.Anchor);
            byte partyCount = await bot.ReadByteAsync((uint)(anchor + pointerService.PartySize));
            List<PokemonData> partyPokemon = new List<PokemonData>();
            Stopwatch sw = new Stopwatch();
            sw.Start();
            for(uint i = 0; i < partyCount; i++)
            {
                var pokemon = parser.Parse(await bot.ReadBytesAsync(anchor + pointerService.BattlePartyPokemon + (i * 0xEC), 0xEC));
                while (pokemon is null || pokemon.IsCorrupt)
                {
                    if(sw.ElapsedMilliseconds > timeoutMs)
                    {
                        sw.Stop();
                        return null;
                    }
                    await Task.Delay(100);
                    anchor = await bot.ReadUIntAsync(pointerService.Anchor);
                    pokemon = parser.Parse(await bot.ReadBytesAsync(anchor + pointerService.BattlePartyPokemon + (i * 0xEC), 0xEC));
                }
                partyPokemon.Add(pokemon);
            }
            sw.Stop();
            var enemyAnchor = await bot.ReadUIntAsync(anchor + pointerService.EnemyAnchor);
            var enemyData = await bot.ReadBytesAsync(enemyAnchor + pointerService.Enemy, 0xEC);
            var enemyPokemon = parser.Parse(enemyData);

            sw = new Stopwatch();
            sw.Start();
            while (enemyPokemon is null || enemyPokemon.IsCorrupt)
            {
                if(sw.ElapsedMilliseconds > 2000)
                {
                    sw.Stop();
                    return null;
                }
                await Task.Delay(100);
                anchor = await bot.ReadUIntAsync(pointerService.Anchor); 
                enemyAnchor = await bot.ReadUIntAsync(anchor + pointerService.EnemyAnchor);
                enemyData = await bot.ReadBytesAsync(enemyAnchor + pointerService.Enemy, 0xEC);
                enemyPokemon = parser.Parse(enemyData);
            }
            sw.Stop();
            var battleEnemyPokemonData = await bot.ReadBytesAsync(anchor + pointerService.BattleEnemyPokemon, 108);
            var battlePokemonData = await bot.ReadBytesAsync(anchor + pointerService.BattlePokemon, 108);
            var activePokemon = battleParser.ParseBattlePokemon(battlePokemonData, partyPokemon);
            if(activePokemon is null)
            {
                return null;
            }
            return new PokemonBattleState()
            {
                PartyPokemon = partyPokemon,
                EnemyPokemon = battleParser.ParseBattlePokemon(battleEnemyPokemonData, enemyPokemon),
                ActivePokemon = activePokemon
            };
        }
    }
}
