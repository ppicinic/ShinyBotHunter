using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    public class DiscordBotServiceTests
    {
        [Fact]
        public async Task StopAsync_completes_after_a_message_when_Discord_never_logged_in()
        {
            var client = new DiscordSocketClient();
            var service = new DiscordBotService(
                client,
                new InteractionService(client),
                Options.Create(new ApplicationSettings { DiscordGuildId = 1, DiscordNewMoveChannelId = 1 }),
                null!,
                null!);

            await service.SendMessageAsync(new DiscordMessage { StringMessage = "x", GuildId = 1, ChannelId = 1 });
            await service.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task SendNewMoveDecisionAndWaitAsync_declines_the_move_when_Discord_never_logged_in()
        {
            var client = new DiscordSocketClient();
            var service = new DiscordBotService(
                client,
                new InteractionService(client),
                Options.Create(new ApplicationSettings { DiscordGuildId = 1, DiscordNewMoveChannelId = 1 }),
                null!,
                null!);

            var choice = await service.SendNewMoveDecisionAndWaitAsync(
                new PokemonData { Name = "Bulbasaur" },
                new PokemonLevelUpMoveMetadata { Id = 36, Name = "Take Down", Level = 15 })
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(-1, choice);
        }
    }
}
