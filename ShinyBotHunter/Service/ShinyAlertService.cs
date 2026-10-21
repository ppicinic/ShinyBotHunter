using Discord;
using Microsoft.Extensions.Options;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Util;

namespace ShinyBotHunter.Service
{
    /// <summary>
    /// Builds and sends the "a target turned up" Discord alert.
    ///
    /// This embed was copy-pasted between the battle orchestrator and the wild encounter event, so a
    /// change to the sprite path or the IV table had to be made twice or the two silently drifted.
    /// The egg hunt would have made it three copies.
    /// </summary>
    public class ShinyAlertService
    {
        private readonly DiscordBotService discordBotService;
        private readonly ApplicationSettings appSettings;

        public ShinyAlertService(DiscordBotService discordBotService, IOptions<ApplicationSettings> appSettings)
        {
            this.discordBotService = discordBotService;
            this.appSettings = appSettings.Value;
        }

        /// <param name="verb">How the target turned up. Only the wording changes -- a hatched egg
        /// gets the same embed as a wild encounter, which is the point.</param>
        public async Task NotifyTargetFoundAsync(PersistPokemonData persistPokemonData, string verb = "encountered")
        {
            var pokemon = persistPokemonData.PokemonData;

            bool isFemale = pokemon.FormCode.EndsWith(";f");
            string formCode = pokemon.FormCode.Split(";f")[0];
            string spriteFolder = pokemon.IsShiny ? (isFemale ? "Female_Shiny" : "Shiny") : (isFemale ? "Female_Default" : "Default");
            string spritePath = $"{appSettings.SpriteBaseDirectory}{spriteFolder}/{formCode}.png";
            string attachment = $"attachment://{Path.GetFileName(spritePath)}";
            Console.WriteLine(attachment);

            int ivSum = pokemon.IVHp + pokemon.IVAtk + pokemon.IVDef + pokemon.IVSpAtk + pokemon.IVSpDef + pokemon.IVSpe;

            EmbedBuilder eb = new EmbedBuilder();
            eb.WithColor(Color.Gold);
            eb.WithThumbnailUrl(attachment);
            eb.WithTitle($":sparkles: Shiny {pokemon.Name} {verb}! :sparkles:");
            eb.AddField("Nature", Constant.NATURE_NAMES[pokemon.Nature]);
            eb.AddField("Shiny Value", pokemon.ShinyValue);
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

        private static string PadIv(byte iv) => iv < 10 ? $"0{iv}" : $"{iv}";
    }
}
