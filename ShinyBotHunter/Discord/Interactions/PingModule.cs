using Discord.Interactions;
using Discord.WebSocket;

namespace ShinyBotHunter.Discord.Interactions;

public class PingModule : InteractionModuleBase<SocketInteractionContext<SocketInteraction>>
{
    [SlashCommand("ping", "Replies with pong")]
    public async Task Ping()
    {
        await RespondAsync("pong");
    }
}