using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.BotEvents.Target;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using YamlDotNet.Core.Tokens;

namespace ShinyBotHunter.BotEvents
{
    public class WildPokemonEncounterEvent : IBotEvent
    {
        public enum WildPokemonState
        {
            NotStarted,
            Encountering,
            PokemonDataPending,
            Run,
            Waiting,
            Complete
        }
        IBotEvent<bool> encounteringEvent;
        private WildPokemonState State { get; set; }
        private IBot bot;
        private HgssPokemonParser parser;
        private PokemonPersistenceService persistenceService;
        private PersistPokemonData? foundShinyPokemon = null;
        private GameFlavor gameFlavor;
        private string phase;
        private ITarget target;
        public WildPokemonEncounterEvent(IBot bot, GameFlavor gameFlavor, string phase, EncounterType encounterType, IServiceProvider provider, ITarget target)
        {
            var encounterFactory = provider.GetRequiredService<EncounterEventTypeFactory>();
            encounteringEvent = encounterFactory.GetEncounterEvent(bot, encounterType);
            State = WildPokemonState.NotStarted;
            this.bot = bot; 
            parser = provider.GetRequiredService<HgssPokemonParser>();
            persistenceService = provider.GetRequiredService<PokemonPersistenceService>();
            this.gameFlavor = gameFlavor;
            this.phase = phase;
            this.target = target;
        }

        public EventState EventState
        {
            get
            {
                if(State == WildPokemonState.Waiting)
                {
                    return EventState.Waiting;
                }
                else if(State == WildPokemonState.Complete)
                {
                    return EventState.Complete;
                }
                else if(State == WildPokemonState.NotStarted)
                {
                    return EventState.NotStarted;
                }
                else
                {
                    return EventState.Running;
                }
            }
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            if(State == WildPokemonState.Waiting)
            {
                await CheckForCatchAsync();
                
            }
            while(State != WildPokemonState.Complete && State != WildPokemonState.Waiting && !cancellationToken.IsCancellationRequested)
            {
                if (State == WildPokemonState.NotStarted)
                {
                    State = WildPokemonState.Encountering;
                }
                else if( State == WildPokemonState.Encountering)
                {
                    bool foundPokemon = await encounteringEvent.ExecuteWithResultAsync(cancellationToken);
                    if (foundPokemon)
                    {
                        State = WildPokemonState.PokemonDataPending;
                    }
                }
                else if(State == WildPokemonState.PokemonDataPending)
                {
                    await PokemonDataPending();
                }
                else if(State == WildPokemonState.Run)
                {
                    await RunFromBattleAsync();
                }
            }
        }

        private async Task CheckForCatchAsync()
        {
            uint anchor = await bot.ReadUIntAsync(0x1D4158);
            byte partyCount = await bot.ReadByteAsync(anchor - 0x23F44);
            uint partyPtr = anchor - 0x23F40; 
            uint boxPtr = anchor - 0x148D8;
            bool found = false;
            for (uint i = 0; i < partyCount && !found; i++)
            {
                var pokemon = parser.Parse(await bot.ReadBytesAsync(partyPtr + (0xEC * i), 0xEC));
                if(foundShinyPokemon!.PokemonData.PID == pokemon!.PID && foundShinyPokemon.PokemonData.Species == pokemon.Species)
                {
                    found = true;
                }
            }
            for (uint box = 0; box < 30 && !found; box++) {
                for (uint slot = 0; slot < 30 && !found; slot++)
                {
                    var pokemon = parser.Parse(await bot.ReadBytesAsync(boxPtr + (slot * 136) + (box * 4096), 136));
                    if(pokemon is not null && foundShinyPokemon!.PokemonData.PID == pokemon!.PID && foundShinyPokemon.PokemonData.Species == pokemon.Species)
                    {
                        found = true;
                    }
                }
            }

            if (found)
            {
                // TODO put this in macro yaml
                bot.RunMacroString("S.Save.3;W.5;T.A;W.60;T.A;W.90;T.A;W.1260;T.B;W.60");
                await bot.WaitForMacroAsync();
                await persistenceService.AddCaughtPokemonAsync(foundShinyPokemon!);
                await Task.Delay(1000);
            }
            foundShinyPokemon = null;
            State = WildPokemonState.Encountering;
        }

        private async Task RunFromBattleAsync()
        {

            bot.RunMacroString("W.10");
            await bot.WaitForMacroAsync();
            Console.WriteLine("Running");
            bot.RunRepeatMacro("S.Run.3;W.3;");
            // TODO it's possible we won't run here. we should actually loop for the battle menu again or exact battle.
            // TODO even worse we could die. that would stuck the bot, but we'll assume that won't happen for now.
            byte battleValue = await GetBattleValue();
            //byte battleMenuState = await GetBattleMenuState();
            while (IsBattle(battleValue))
            {
                await Task.Delay(166); // sleep 10 frames before re-polling memory -
                                       // this is semi dangerous we don't know how slow the memory read is and we want to read on ever 10 frames
                                       // regardless of the delay, so real implementition needs a better solution
                battleValue = await GetBattleValue();
            }
            bot.ClearMacro();
            await bot.WaitForMacroAsync();
            //while (await bot.ReadUIntAsync(0x111868) != 0)
            //{
            //    await Task.Delay(100);
            //}
            ////Console.WriteLine("W 4 seconds");
            bot.RunMacroString("W.120");
            await bot.WaitForMacroAsync();
            Console.WriteLine("Run Complete");
            State = WildPokemonState.Encountering;
        }

        //private async Task<byte> GetBattleMenuState()
        //{
        //    uint anchor = await bot.ReadUIntAsync(0x1D4158);
        //    var battleMenuStatePtr = anchor + 0x230EC;
        //    return await bot.ReadByteAsync(battleMenuStatePtr);
        //}

        private bool IsBattle(byte battleValue)
        {
            return battleValue == 0x41 || battleValue == 0x97 || battleValue == 0xC0;
        }

        private async Task<byte> GetBattleValue()
        { 
            return await bot.ReadByteAsync(0x21E76D2);
        }

        private async Task PokemonDataPending()
        {
            uint anchor = await bot.ReadUIntAsync(0x1D4158);
            uint foeAnchorPtr = anchor + 0x6930;
            uint foeAnchor = await bot.ReadUIntAsync(foeAnchorPtr);
            uint foeCountPtr = foeAnchor + 0xC14;
            var foeCount = await bot.ReadByteAsync(foeCountPtr);
            while (foeCount == 0)
            {
                await Task.Delay(166);
                anchor = await bot.ReadUIntAsync(0x1D4158);
                foeAnchorPtr = anchor + 0x6930;
                foeAnchor = await bot.ReadUIntAsync(foeAnchorPtr);
                foeCountPtr = foeAnchor + 0xC14;
                foeCount = await bot.ReadByteAsync(foeCountPtr);
            }
            
            PokemonData? pokemon = parser.Parse(await bot.ReadBytesAsync(foeAnchor + 0xC18, 0xEC));
            
            // tap B until battle menu
            bot.RunRepeatMacro("T.B;W.5");
            anchor = await bot.ReadUIntAsync(0x1D4158);
            var battleMenuStatePtr = anchor + 0x230EC;
            byte battleMenuState = await bot.ReadByteAsync(battleMenuStatePtr);
            while (battleMenuState != 1)
            {
                await Task.Delay(166);
                anchor = await bot.ReadUIntAsync(0x1D4158);
                battleMenuStatePtr = anchor + 0x230EC;
                battleMenuState = await bot.ReadByteAsync(battleMenuStatePtr);
            }
            bot.ClearMacro();
            await bot.WaitForMacroAsync();
            if (pokemon is null)
            {
                Console.WriteLine("read pokemon too fast");
                State = WildPokemonState.Run;
            }
            else
            {
                Console.WriteLine($"{pokemon.Name}, isShiny: {pokemon.IsShiny}");
                var persistPokemon = new PersistPokemonData()
                {
                    Game = gameFlavor,
                    Phase = phase,
                    PokemonData = pokemon,
                    DateTime = DateTime.UtcNow,
                    GameFileName = bot.GameFileName,
                };
                await persistenceService.AddEncounterAsync(persistPokemon);
                if (await target.IsTargetAsync(pokemon))
                    // if(random.Shared < .3) // testing line.
                {
                    foundShinyPokemon = persistPokemon;
                    State = WildPokemonState.Waiting;
                }
                else
                {
                    State = WildPokemonState.Run;
                }
            }
        }
    }
}
