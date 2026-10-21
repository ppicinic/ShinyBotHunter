using ShinyBotHunter.Bot;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.BotEvents
{
    public class HgssShinyStarterEvent : AbstractBotEvent<PokemonData?>
    {
        private IBot bot;
        private GameFlavor flavor;
        private IPokemonParser parser;
        private PokemonPersistenceService encounterService;

        public HgssShinyStarterEvent(IPokemonParser parser, PokemonPersistenceService encounterService, IBot bot, GameFlavor flavor)
        {
            this.bot = bot;
            this.flavor = flavor;
            this.parser = parser;
            this.encounterService = encounterService;
        }

        public int[] BotPositions { get { return []; } }

        public async override Task<PokemonData?> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken = default)
        {

            PersistPokemonData? shinyPokemon = null;
            try
            {
                await bot.LaunchAsync();
                await Task.Delay(10000);
                bool shinyFound = false;
                while (!shinyFound && !cancellationToken.IsCancellationRequested)
                {
                    
                    bot.RunMacroString(MacroRegistry.Instance.GetMacro(flavor, "SelectStarterFull"));
                    await bot.WaitForMacroAsync(cancellationToken);
                    await Task.Delay(2000);

                    uint anchor = await bot.ReadUIntAsync(0x21D4158);
                    uint starterData = anchor + 0x1BC00;
                    List<PersistPokemonData> pokemonData = new List<PersistPokemonData>();
                    DateTime now = DateTime.UtcNow;
                    for (uint i = 0; i < 3; i++)
                    {
                        var rawData = await this.bot.ReadBytesAsync(starterData + (0xEC * i), 0xEC);
                        PokemonData? starter = parser.Parse(rawData);
                        if (starter is not null && !starter.IsCorrupt)
                        {
                            PersistPokemonData persistPokemonData = new PersistPokemonData()
                            {
                                PokemonData = starter,
                                DateTime = now,
                                Game = flavor,
                                Phase = "1-1 Starters",
                                GameFileName = bot.GameFileName,
                            };
                            pokemonData.Add(persistPokemonData);
                            if (starter.IsShiny)
                            {
                                shinyPokemon = persistPokemonData;
                                shinyFound = true;
                            }
                            Console.WriteLine($"SPID: {starter.Species}, PID: {starter.PID}");
                        }
                        else
                        {
                            // probably don't need this. will be expected on a cancellation.
                            Console.WriteLine($"ERROR: Cannot read pokemon data on starter data.");
                        }
                    }
                    await this.encounterService.AddEncounterAsync(pokemonData);
                    if (!shinyFound)
                    {
                        bot.RunMacroString(MacroRegistry.Instance.GetMacro(flavor, "SoftReset"));
                        await Task.Delay(15000);
                    }

                }
                if(shinyPokemon is null)
                {
                    return null; 
                }
                if(shinyPokemon.PokemonData.Species == 152)
                {
                    bot.Tap("Left");
                    await Task.Delay(2000);
                }
                else if(shinyPokemon.PokemonData.Species == 155)
                {
                    bot.Tap("Right");
                    await Task.Delay(2000);
                }
                // run save game macro
                bot.RunMacroString(MacroRegistry.Instance.GetMacro(flavor, "SaveStarter"));
                await bot.WaitForMacroAsync();
                await bot.FlushSave();
                // Pass successful shiny to producer-consumer queue
                await this.encounterService.AddCaughtPokemonAsync(shinyPokemon);
                await Task.Delay(10000);//10second safety margin to save game before closing emulator.

                
                return shinyPokemon.PokemonData;
            }
            catch { }
            finally {
                await bot.DisposeAsync();
            }
            return shinyPokemon is not null ? shinyPokemon.PokemonData : null;
            // therotically this could return null if this event is cancelled,
            // but if we did that we wouldn't await this response so /shrug ?
        }
    }
}
