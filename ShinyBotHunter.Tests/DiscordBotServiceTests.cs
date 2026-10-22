using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
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
    }
}
