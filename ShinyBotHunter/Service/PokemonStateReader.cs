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

        /// <summary>
        /// Whether the box arrays were actually filled. GetPartyState allocates them and leaves every
        /// entry null, so a box-inclusive query against a party-only state would scan 540 nulls and
        /// quietly answer "not present". For the catch check that reads as "the pokemon got away":
        /// nothing is persisted, the milestone never completes, and the only repair is by hand. The
        /// box-reading queries below therefore refuse to run rather than trusting the caller.
        /// </summary>
        public bool BoxesLoaded { get; set; } = false;

        private void RequireBoxes(string caller)
        {
            if (!BoxesLoaded)
            {
                throw new InvalidOperationException(
                    $"{caller} searches the PC boxes, but this state came from a party-only read. Use GetPokemonState, not GetPartyState.");
            }
        }

        public bool Search(PokemonData pokemon)
        {
            RequireBoxes(nameof(Search));
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
            RequireBoxes(nameof(ToList));
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
        // Size of a party-sized pokemon structure (stored data plus battle stats).
        private const int PokemonBlockSize = 0xEC;

        // The in-battle mirror of a pokemon, a different and shorter structure.
        private const int BattlePokemonBlockSize = 108;

        private const int MaxPartySize = 6;

        // How long the emulator keeps retrying a party read before giving up. At 60fps this is about
        // two seconds, comfortably inside the memory server's 5s handler timeout.
        private const int DefaultPartyRetryFrames = 120;

        private static int MillisecondsToFrames(int milliseconds)
        {
            int frames = milliseconds * 60 / 1000;
            return frames < 1 ? 1 : frames;
        }

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
            var blocks = await bot.ReadValidatedPartyAsync(
                anchorAddress: 0x21D4158,
                countOffset: -0x23F44,
                slotsOffset: -0x23F40,
                slotSize: PokemonBlockSize,
                slotStride: PokemonBlockSize,
                maxSlots: MaxPartySize,
                maxRetryFrames: DefaultPartyRetryFrames);
            state.PartySize = blocks.Length;
            for (int i = 0; i < blocks.Length; i++)
            {
                state.PartyPokemon[i] = ParseValidated(blocks[i], $"party slot {i + 1}");
            }
            return state;
        }

        /// <summary>
        /// Parses a block the emulator already validated. The emulator checked the same things the
        /// parser reports as IsCorrupt, so a failure here means the two disagree -- worth surfacing
        /// rather than silently retrying, since the likely cause is the validator's copy of the
        /// crypto having drifted from HgssPokemonParser's.
        /// </summary>
        private PokemonData ParseValidated(byte[] block, string what)
        {
            var pokemon = parser.Parse(block);
            if (pokemon is null || pokemon.IsCorrupt)
            {
                throw new InvalidOperationException(
                    $"{what} passed emulator-side validation but the parser rejected it. " +
                    "ApiHawk's PokemonValidator and HgssPokemonParser may have diverged.");
            }
            return pokemon;
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
            // Every box slot is now either parsed or confirmed empty, so box-inclusive queries are safe.
            state.BoxesLoaded = true;
            return state;
        }

        public async Task<PokemonBattleState?> GetPokemonBattleState(IMemoryClient bot, int timeoutMs = 1000)
        {
            List<PokemonData> partyPokemon = new List<PokemonData>();
            byte[][] battleBlocks;
            try
            {
                battleBlocks = await bot.ReadValidatedPartyAsync(
                    anchorAddress: pointerService.Anchor,
                    countOffset: pointerService.PartySize,
                    slotsOffset: (int)pointerService.BattlePartyPokemon,
                    slotSize: PokemonBlockSize,
                    slotStride: PokemonBlockSize,
                    maxSlots: MaxPartySize,
                    // Keeps this method's timeout contract: the emulator gives up after roughly the
                    // same wall clock the old polling loop allowed.
                    maxRetryFrames: MillisecondsToFrames(timeoutMs));
            }
            catch (PartyReadUnavailableException ex)
            {
                // The battle party is only populated during a battle, so an unreadable one is an
                // expected outcome here rather than a fault -- the caller polls.
                Console.WriteLine($"[PokemonStateReader] Battle party unavailable: {ex.Message}");
                return null;
            }
            foreach (var block in battleBlocks)
            {
                partyPokemon.Add(ParseValidated(block, $"battle party slot {partyPokemon.Count + 1}"));
            }

            // anchor -> +EnemyAnchor -> deref -> +Enemy, all inside one frame.
            var enemyData = await bot.ReadBytesViaPointerAsync(
                pointerService.Anchor,
                PokemonBlockSize,
                (int)pointerService.EnemyAnchor,
                (int)pointerService.Enemy);
            var enemyPokemon = parser.Parse(enemyData);

            var sw = new Stopwatch();
            sw.Start();
            while (enemyPokemon is null || enemyPokemon.IsCorrupt)
            {
                if(sw.ElapsedMilliseconds > 2000)
                {
                    sw.Stop();
                    return null;
                }
                await Task.Delay(100);
                enemyData = await bot.ReadBytesViaPointerAsync(
                    pointerService.Anchor,
                    PokemonBlockSize,
                    (int)pointerService.EnemyAnchor,
                    (int)pointerService.Enemy);
                enemyPokemon = parser.Parse(enemyData);
            }
            sw.Stop();
            var battleEnemyPokemonData = await bot.ReadBytesViaPointerAsync(pointerService.Anchor, BattlePokemonBlockSize, (int)pointerService.BattleEnemyPokemon);
            var battlePokemonData = await bot.ReadBytesViaPointerAsync(pointerService.Anchor, BattlePokemonBlockSize, (int)pointerService.BattlePokemon);
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
