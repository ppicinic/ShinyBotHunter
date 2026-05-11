using Discord;
using Microsoft.Extensions.Options;
using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.BotEvents.Target;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Scripts;
using ShinyBotHunter.Service;
using ShinyBotHunter.Services;
using YamlDotNet.Core.Tokens;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace ShinyBotHunter.BotEvents
{
    
public class WildPokemonEncounterEvent : IBotEvent
    {
        private static Dictionary<byte, string> NATURE_NAMES = new Dictionary<byte, string>() {
            { 0, "Hardy" }, {  1, "Lonely" }, {  2, "Brave" }, {  3, "Adamant" }, {  4, "Naughty" }, 
            { 5, "Bold" }, {  6, "Docile" }, {  7, "Relaxed" }, {  8, "Impish" }, {  9, "Lax" }, 
            { 10, "Timid" }, {  11, "Hasty" }, {  12, "Serious" }, {  13, "Jolly" }, {  14, "Naive" }, 
            { 15, "Modest" }, { 16, "Mild" }, {  17, "Quiet" }, {  18, "Bashful" }, {  19, "Rash" }, 
            { 20, "Calm" }, {  21, "Gentle" }, {  22, "Sassy" }, {  23, "Careful" }, {  24, "Quirky" } };
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
        private DiscordBotService discordBotService;
        private ApplicationSettings appSettings;
        private BotService botService;
        private PokemonStateReader reader;
        private PhaseInfoService phaseInfoService;
        private EncounterParams encounterParams;
        public WildPokemonEncounterEvent(IBot bot, GameFlavor gameFlavor, string phase, EncounterParams encounterParams, IServiceProvider provider, ITarget target, BotService botService)
        {
            var encounterFactory = provider.GetRequiredService<EncounterEventTypeFactory>();
            encounteringEvent = encounterFactory.GetEncounterEvent(bot, encounterParams);
            State = WildPokemonState.NotStarted;
            this.bot = bot; 
            parser = provider.GetRequiredService<HgssPokemonParser>();
            persistenceService = provider.GetRequiredService<PokemonPersistenceService>();
            discordBotService = provider.GetRequiredService<DiscordBotService>();
            appSettings = provider.GetRequiredService<IOptions<ApplicationSettings>>().Value;
            reader = provider.GetRequiredService<PokemonStateReader>();
            phaseInfoService = provider.GetRequiredService<PhaseInfoService>();
            this.gameFlavor = gameFlavor;
            this.phase = phase;
            this.target = target;
            this.botService = botService;
            this.encounterParams = encounterParams;
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
                try
                {
                    await CheckForCatchAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    throw;
                }
                await this.botService.RefreshDashboardState();
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

        private async Task NotifyDiscordOfTarget(PersistPokemonData persistPokemonData)
        {
            EmbedBuilder eb = new EmbedBuilder();
            eb.WithColor(Color.Gold);
            bool isFemale = persistPokemonData.PokemonData.FormCode.EndsWith(";f");
            string formCode = persistPokemonData.PokemonData.FormCode.Split(";f")[0];
            string spriteFolder = persistPokemonData.PokemonData.IsShiny ? (isFemale ? "Female_Shiny" : "Shiny") : (isFemale ? "Female_Default" : "Default");
            string spritePath = $"{appSettings.SpriteBaseDirectory}{spriteFolder}/{formCode}.png";
            string attachment = $"attachment://{Path.GetFileName(spritePath)}";
            Console.WriteLine(attachment);
            eb.WithThumbnailUrl(attachment);
            eb.WithTitle($":sparkles: Shiny {persistPokemonData.PokemonData.Name} encountered! :sparkles:");
            eb.AddField("Nature", NATURE_NAMES[persistPokemonData.PokemonData.Nature]);
            eb.AddField("Shiny Value", persistPokemonData.PokemonData.ShinyValue);
            var pokemon = persistPokemonData.PokemonData;
            int ivSum = pokemon.IVHp + pokemon.IVAtk + pokemon.IVDef + pokemon.IVSpAtk + pokemon.IVSpDef + pokemon.IVSpe;
            eb.AddField($"IVs ({ivSum})", $"```╔═══╤═══╤═══╤═══╤═══╤═══╗\r\n║HP │ATK│DEF│SPA│SPD│SPE║\r\n╠═══╪═══╪═══╪═══╪═══╪═══╣\r\n║{PadIv(pokemon.IVHp)} │{PadIv(pokemon.IVAtk)} │{PadIv(pokemon.IVDef)} │{PadIv(pokemon.IVSpAtk)} │{PadIv(pokemon.IVSpDef)} │{PadIv(pokemon.IVSpe)} ║\r\n╚═══╧═══╧═══╧═══╧═══╧═══╝```");
            await discordBotService.SendMessageAsync(new DiscordMessage()
            {
                StringMessage = $"<@&{appSettings.ShinyLogMention}>",
                GuildId = appSettings.DiscordGuildId,
                ChannelId = appSettings.ShinyLogChannel,
                EmbedMessage = eb.Build(),
                Files = [spritePath]
            });
        }

        private string PadIv(byte iv)
        {
            if (iv < 10)
            {
                return $"0{iv}";
            }
            return $"{iv}";
        }

        private async Task CheckForCatchAsync()
        {
            Console.WriteLine("Checking for pokemon");

            bool found = (await reader.GetPokemonState(bot)).Search(foundShinyPokemon!.PokemonData);
            
            Console.WriteLine($"Found pokemon: {found}");
            if (found)
            {
                // TODO put this in macro yaml
                bot.RunMacroString("S.Save.3;W.5;T.A;W.60;T.A;W.90;T.A;W.1260;T.B;W.60;T.B;W.5");
                await bot.WaitForMacroAsync();
                await persistenceService.AddCaughtPokemonAsync(foundShinyPokemon!);
                await Task.Delay(1000);
                await bot.FlushSave();
            }
            foundShinyPokemon = null;
            var phaseInfo = phaseInfoService.GetPhaseInfo(this.gameFlavor, this.phase);
            var caughtPokemon = (await persistenceService.GetCaughtPokemonAsync(GameFlavor.HGSS)).Where(x => x.GameFileName == bot.GameFileName).ToList();
            bool complete = true;
            if(phaseInfo is not null)
            {
                foreach(var targetPokemon in phaseInfo.Pokemon)
                {
                    if (targetPokemon.RequireSpecificForm)
                    {
                        if (caughtPokemon.Where(x => x.Pokemon.IsShiny && x.Pokemon.FormCode == targetPokemon.FormCode).Count() < targetPokemon.Amount)
                        {
                            complete = false;
                        }
                    }
                    else
                    {
                        if (caughtPokemon.Where(x => x.Pokemon.IsShiny && x.Pokemon.Name == targetPokemon.Name).Count() < targetPokemon.Amount)
                        {
                            complete = false;
                        }
                    }
                }
            }
            if (complete)
            {
                State = WildPokemonState.Complete;
            }
            else
            {
                State = WildPokemonState.Encountering;
            }
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
            if (encounterParams.type == EncounterType.Spin) // TODO move this to start of spin encounter.
            {
                bot.RunMacroString("W.120");
                await bot.WaitForMacroAsync();
                Console.WriteLine("Run Complete");
            }
            State = WildPokemonState.Encountering;
        }

        //private async Task<byte> GetBattleMenuState()
        //{
        //    uint anchor = await bot.ReadUIntAsync(0x21D4158);
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
            uint anchor = await bot.ReadUIntAsync(0x21D4158);
            uint foeAnchorPtr = anchor + 0x6930;
            uint foeAnchor = await bot.ReadUIntAsync(foeAnchorPtr);
            uint foeCountPtr = foeAnchor + 0xC14;
            var foeCount = await bot.ReadByteAsync(foeCountPtr);
            while (foeCount == 0)
            {
                await Task.Delay(166);
                anchor = await bot.ReadUIntAsync(0x21D4158);
                foeAnchorPtr = anchor + 0x6930;
                foeAnchor = await bot.ReadUIntAsync(foeAnchorPtr);
                foeCountPtr = foeAnchor + 0xC14;
                foeCount = await bot.ReadByteAsync(foeCountPtr);
            }

            PokemonData? pokemon = pokemon = parser.Parse(await bot.ReadBytesAsync(foeAnchor + 0xC18, 0xEC));
            while(pokemon is null || pokemon.IsCorrupt)
            {
                await Task.Delay(166); 
                anchor = await bot.ReadUIntAsync(0x21D4158);
                foeAnchor = await bot.ReadUIntAsync(anchor + 0x6930);
                pokemon = pokemon = parser.Parse(await bot.ReadBytesAsync(foeAnchor + 0xC18, 0xEC));
            }
                
            
            // tap B until battle menu
            bot.RunRepeatMacro("T.B;W.5");
            anchor = await bot.ReadUIntAsync(0x21D4158);
            var battleMenuStatePtr = anchor + 0x230EC;
            byte battleMenuState = await bot.ReadByteAsync(battleMenuStatePtr);
            while (battleMenuState != 1)
            {
                await Task.Delay(166);
                anchor = await bot.ReadUIntAsync(0x21D4158);
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
                {
                    foundShinyPokemon = persistPokemon;
                    await NotifyDiscordOfTarget(persistPokemon);
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
