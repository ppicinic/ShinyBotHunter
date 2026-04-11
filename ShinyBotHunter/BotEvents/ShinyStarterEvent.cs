using ShinyBotHunter.Bot;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;

namespace ShinyBotHunter.BotEvents
{
    public class HgssShinyStarterEvent : IBotEvent<PokemonData>
    {
        private IBot bot;
        private GameFlavor flavor;
        private HgssPokemonParser parser = new HgssPokemonParser();

        public HgssShinyStarterEvent(IBot bot, GameFlavor flavor)
        {
            this.bot = bot;
            this.flavor = flavor;
        }

        public async Task<PokemonData> ExecuteAsync(CancellationToken cancellationToken = default)
        {

            PokemonData? shinyPokemon = null;
            try
            {
                await bot.LaunchAsync();
                await Task.Delay(10000);
                bool shinyFound = false;
                int x = 0;
                int trialFinish = Random.Shared.Next(6, 13);
                while (!shinyFound && !cancellationToken.IsCancellationRequested)
                {
                    
                    bot.RunMacroString(MacroRegistry.Instance.GetMacro(flavor, "SelectStarterFull"));
                    await bot.WaitForMacroAsync(cancellationToken);
                    await Task.Delay(2000);

                    uint anchor = await bot.ReadUIntAsync(0x1D4158);
                    uint anchorOffset = anchor - 0x02000000;
                    uint starterData = anchorOffset + 0x1BC00;
                    for (uint i = 0; i < 3; i++)
                    {
                        var rawData = await this.bot.ReadBytesAsync(starterData + (0xEC * i), 0xEC);
                        PokemonData? starter = parser.Parse(rawData);
                        if (starter is not null)
                        {
                            if (x == trialFinish)
                            {
                                shinyPokemon = starter;
                                shinyFound = true;
                            }
                            x++;
                            // TODO Pass pokemon data to Producer-Consumer queue.
                            //TEMP
                            Console.WriteLine($"SPID: {starter.Species}, PID: {starter.PID}");
                        }
                        else
                        {
                            // probably don't need this. will be expected on a cancellation.
                            Console.WriteLine($"ERROR: Cannot read pokemon data on starter data.");
                        }
                    }
                    if (!shinyFound)
                    {
                        bot.RunMacroString(MacroRegistry.Instance.GetMacro(GameFlavor.HGSS, "SoftReset"));
                        await Task.Delay(15000);
                    }

                }
                if(shinyPokemon is null)
                {
                    return shinyPokemon!;
                }
                if(shinyPokemon.Species == 152)
                {
                    bot.Tap("Left");
                    await Task.Delay(2000);
                }
                else if(shinyPokemon.Species == 155)
                {
                    bot.Tap("Right");
                    await Task.Delay(2000);
                }
                // run save game macro
                bot.RunMacroString(MacroRegistry.Instance.GetMacro(GameFlavor.HGSS, "SaveStarter"));
                await bot.WaitForMacroAsync();
                await Task.Delay(10000);//10second safety margin to save game before closing emulator.

                // Pass successful shiny to producer-consumer queue

                return shinyPokemon!;
            }
            catch { }
            finally {
                await bot.DisposeAsync();
            }
            return shinyPokemon!;
            // therotically this could return null if this event is cancelled,
            // but if we did that we wouldn't await this response so /shrug ?
        }
    }
}
