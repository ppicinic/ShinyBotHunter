using Discord;
using Discord.Interactions;
using IResult = Discord.Interactions.IResult;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using ShinyBotHunter.Discord.Interactions;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ShinyBotHunter.Services;

public class DiscordMessage()
{
    public string? StringMessage { get; set; }
    public Embed? EmbedMessage { get; set; }
    public ulong? GuildId { get; set; }
    public ulong? ChannelId { get; set; }
    public List<string> Files { get; set; } = [];
}
public sealed class DiscordBotService
{
    private readonly DiscordSocketClient _client;
    private readonly IOptions<ApplicationSettings> _options;
    private readonly InteractionService _interactions;
    private readonly IServiceProvider _services;
    private bool isReady = false;
    private Channel<DiscordMessage> messageQueue = Channel.CreateUnbounded<DiscordMessage>();

    public DiscordBotService(
        DiscordSocketClient client,
        InteractionService interactions,
        IOptions<ApplicationSettings> options,
        IServiceProvider services)
    {
        _client = client;
        _interactions = interactions;
        _options = options;
        _services = services;
    }



    public async Task StartAsync()
    {
        _client.Log += OnLogAsync;
        _interactions.Log += OnLogAsync;
        _client.Ready += OnReadyAsync;
        _interactions.SlashCommandExecuted += SlashCommandExecuted;
        _client.InteractionCreated += OnInteractionCreatedAsync;

        var token = _options.Value.DiscordToken;
        if (string.IsNullOrWhiteSpace(token) || token == "YOUR_BOT_TOKEN")
        {
            Console.WriteLine("Discord bot token is not configured. Skipping Discord bot startup.");
            return;
        }

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();
    }

    public async Task SendMessageAsync(DiscordMessage message)
    {
        await messageQueue.Writer.WriteAsync(message);
    }

    private async Task ProcessMessagesAsync()
    {
        await foreach(var message in messageQueue.Reader.ReadAllAsync())
        {
            if(message.GuildId is null || message.ChannelId is null)
            {
                continue; // skip this message
            }
            while(_client.ConnectionState != ConnectionState.Connected)
            {
                await Task.Delay(1000);
            }
            var channel = _client.GetGuild(message.GuildId.Value).GetTextChannel(message.ChannelId.Value);
            if (message.EmbedMessage is not null)
            {
                if (message.Files.Count() > 1)
                {
                    await channel.SendFilesAsync(message.Files.Select(x => new FileAttachment(x)), message.StringMessage, embed: message.EmbedMessage);
                }
                else if(message.Files.Count() == 1)
                {
                    Console.WriteLine("Sending files");
                    await channel.SendFileAsync(message.Files[0], message.StringMessage, embed: message.EmbedMessage);
                }
                else
                {
                    await channel.SendMessageAsync(message.StringMessage, embed: message.EmbedMessage);
                }
            }
            else if(message.StringMessage is not null)
            {
                await channel.SendMessageAsync(message.StringMessage);
            }
        }
    }

    private async Task SlashCommandExecuted(SlashCommandInfo info, IInteractionContext context, IResult result)
    {
        if (!result.IsSuccess)
        {
            switch (result.Error)
            {
                case InteractionCommandError.UnmetPrecondition:
                    await RespondOrFollowupAsync($"Unmet Precondition: {result.ErrorReason}", context);
                    break;
                case InteractionCommandError.UnknownCommand:
                    await RespondOrFollowupAsync("Unknown command", context);
                    break;
                case InteractionCommandError.BadArgs:
                    await RespondOrFollowupAsync("Invalid number or arguments", context);
                    break;
                case InteractionCommandError.Exception:
                    await RespondOrFollowupAsync($"Command exception: {result.ErrorReason}", context);
                    break;
                case InteractionCommandError.Unsuccessful:
                    await RespondOrFollowupAsync("Command could not be executed", context);
                    break;
                default:
                    break;
            }
        }
    }

    private async Task OnInteractionCreatedAsync(SocketInteraction interaction)
    {
        try
        {
            var context = new SocketInteractionContext<SocketInteraction>(_client, interaction);
            var result = await _interactions.ExecuteCommandAsync(context, _services);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString(), "Exception while executing interaction.");
            throw;
        }
    }

    public async Task StopAsync()
    {
        messageQueue.Writer.TryComplete();
        await messageQueue.Reader.Completion;
        try
        {
            if (_client.LoginState == LoginState.LoggedIn)
                await _client.LogoutAsync();
            await _client.StopAsync();
        }
        catch { }
    }

    private Task OnLogAsync(LogMessage msg)
    {
        Console.WriteLine(msg.Message);
        return Task.CompletedTask;
    }

    private async Task OnReadyAsync()
    {
        Console.WriteLine($"Discord client ready. Logged in as {_client.CurrentUser} ({_client.CurrentUser.Id})");

        try
        {
            await _interactions.AddModulesAsync(typeof(PingModule).Assembly, _services);

            var guildId = _options.Value.DiscordGuildId;
            if (guildId is null || guildId == 0)
            {
                Console.WriteLine("Discord GuildId is not configured. Skipping interaction command registration.");
                return;
            }

            foreach (var g in _client.Guilds)
            {
                Console.WriteLine($"Connected guild: {g.Name} ({g.Id})");
            }

            var guild = _client.GetGuild(guildId.Value);
            if (guild is null)
            {
                Console.WriteLine($"Missing access to guild {guildId}. Ensure the bot is invited to this server with scopes 'bot' and 'applications.commands'.");
                return;
            }

            await _interactions.RegisterCommandsToGuildAsync(guildId.Value);
            Console.WriteLine($"Registered interaction commands to guild {guildId}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to register interaction commands. {e.ToString()}");
        }
        if (!isReady)
        {
            isReady = true;
            var _ = Task.Run(async () => await ProcessMessagesAsync());
        }
        
    }

    private async Task RespondOrFollowupAsync(string message, IInteractionContext context)
    {
        if (context.Interaction.HasResponded)
        {
            await context.Interaction.FollowupAsync(message);
        }
        else
        {
            await context.Interaction.RespondAsync(message);
        }
    }
}