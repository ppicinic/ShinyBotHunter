using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using ShinyBotHunter.Discord.Interactions;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using System.Threading.Channels;
using static System.Net.Mime.MediaTypeNames;
using IResult = Discord.Interactions.IResult;

namespace ShinyBotHunter.Service;

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
    private ulong guildId;
    private ulong newMoveChannelId;
    private PokemonMetadataService metadataService;
    private Channel<DiscordMessage> messageQueue = Channel.CreateUnbounded<DiscordMessage>();

    public DiscordBotService(
        DiscordSocketClient client,
        InteractionService interactions,
        IOptions<ApplicationSettings> options,
        IServiceProvider services,
        PokemonMetadataService metadataService)
    {
        _client = client;
        _interactions = interactions;
        _options = options;
        _services = services;
        this.metadataService = metadataService;
        this.guildId = options.Value.DiscordGuildId!.Value;
        this.newMoveChannelId = options.Value.DiscordNewMoveChannelId!.Value;
    }

    public async Task<int> SendNewMoveDecisionAndWaitAsync(PokemonData pokemon, PokemonLevelUpMoveMetadata newMove)
    {
        // 1. Create a TaskCompletionSource to return the CustomId string
        var tcs = new TaskCompletionSource<int>();
        EmbedBuilder eb = new EmbedBuilder();
        var channel = _client.GetGuild(guildId).GetTextChannel(newMoveChannelId);
        eb.WithTitle($"{pokemon.Name} - New Move");
        eb.WithDescription($"{pokemon.Name} is trying to learn a new move, {newMove.Name}. Should {pokemon.Name} forget a move to learn {newMove.Name}");
        // 2. One button per occupied move slot, plus Do Not Learn.
        // Empty slots are skipped: there is no move to forget, and no metadata entry to name the
        // button either -- looking one up threw KeyNotFoundException and killed the run. The custom
        // id stays bound to the slot index rather than the button position, so the answer still
        // identifies the right slot when a slot is missing.
        var moveStyles = new[] { ButtonStyle.Primary, ButtonStyle.Secondary, ButtonStyle.Success, ButtonStyle.Danger };
        var componentBuilder = new ComponentBuilder();
        var knownMoves = pokemon.Moves;
        for (int slot = 0; slot < knownMoves.Length; slot++)
        {
            if (knownMoves[slot].Id <= 0)
            {
                continue;
            }
            componentBuilder.WithButton(metadataService.GetPokemonMoveMetadata(knownMoves[slot].Id).Name, $"move_{slot}", moveStyles[slot]);
        }
        var component = componentBuilder
            .WithButton("Do Not Learn", "do_not_learn", ButtonStyle.Primary)
            .Build();

        // 3. Send the custom text message, embed, and the 5 buttons
        var message = await channel.SendMessageAsync(embed: eb.Build(), components: component);
        string? pendingChoice = null;

        async Task ButtonHandler(SocketMessageComponent interaction)
        {
            if (interaction.Message.Id != message.Id) return;

            string customId = interaction.Data.CustomId;
            var clickedButton = interaction.Message.Components
                .OfType<ActionRowComponent>()
                .SelectMany(row => row.Components)
                .OfType<ButtonComponent>()
                .FirstOrDefault(btn => btn.CustomId == customId);
            string buttonLabel = clickedButton?.Label ?? customId;

            // Step A: User clicked one of the main 5 buttons
            if (customId.StartsWith("move_") || customId == "do_not_learn")
            {
                pendingChoice = customId;

                // Build "Are you sure?" confirmation buttons
                var confirmComponent = new ComponentBuilder()
                    .WithButton("Confirm Choice", "confirm_yes", ButtonStyle.Success)
                    .WithButton("Cancel", "confirm_no", ButtonStyle.Secondary)
                    .Build();

                // Update message inline to ask for confirmation
                if (customId == "do_not_learn")
                {
                    EmbedBuilder eb2 = new EmbedBuilder();
                    eb2.WithTitle($"{pokemon.Name} - New Move")
                        .WithDescription($"⚠️ **Are you sure** you want {pokemon.Name} to stop trying to learn {newMove.Name}?");
                    await interaction.UpdateAsync(msg =>
                    { 
                        msg.Embed = eb2.Build();
                        msg.Components = confirmComponent;
                    });
                }
                else
                {
                    EmbedBuilder eb2 = new EmbedBuilder();
                    eb2.WithTitle($"{pokemon.Name} - New Move")
                        .WithDescription($"⚠️ **Are you sure** you want {pokemon.Name} to forget {buttonLabel}, and learn {newMove.Name}?");
                    await interaction.UpdateAsync(msg =>
                    {
                        msg.Embed = eb2.Build();
                        msg.Components = confirmComponent;
                    });
                }
                return;
            }

            // Step B: User clicked "Confirm Choice"
            if (customId == "confirm_yes")
            {
                try
                {
                    int result = -1;
                    switch (pendingChoice)
                    {
                        case "move_0":
                            result = 0;
                            break;
                        case "move_1":
                            result = 1;
                            break;
                        case "move_2":
                            result = 2;
                            break;
                        case "move_3":
                            result = 3;
                            break;
                    }
                    tcs.TrySetResult(result);
                    if (result == -1)
                    {
                        EmbedBuilder eb3 = new EmbedBuilder();
                        eb3.WithTitle($"{pokemon.Name} - New Move")
                            .WithDescription($"{pokemon.Name} did not learn {newMove.Name}!");
                        await interaction.UpdateAsync(msg =>
                        {
                            msg.Embed = eb3.Build();
                            msg.Components = null;
                        });
                    }
                    else
                    {
                        EmbedBuilder eb3 = new EmbedBuilder();
                        eb3.WithTitle($"{pokemon.Name} - New Move")
                            .WithDescription($"{pokemon.Name} forget {buttonLabel}, and learned {newMove.Name}!");
                        await interaction.UpdateAsync(msg =>
                        {
                            msg.Embed = eb3.Build();
                            msg.Components = null;
                        });
                    } // Wake up the background task with the original choice
                }
                catch(Exception e)
                {
                    Console.WriteLine(e.ToString());
                    throw;
                }
                return;
            }

            // Step C: User clicked "Cancel" (Reset back to the 5 buttons)
            if (customId == "confirm_no")
            {
                pendingChoice = null;
                await interaction.UpdateAsync(msg => {
                    msg.Embed = eb.Build();
                    msg.Components = component;
                });
            }
        }

        // 5. Hook up the event
        _client.ButtonExecuted += ButtonHandler;

        try
        {
            // 6. Await indefinitely until one of the 5 buttons is clicked
            return await tcs.Task;
        }
        finally
        {
            // 7. Unhook the event to prevent memory leaks
            _client.ButtonExecuted -= ButtonHandler;
        }
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
        // The queue's only reader starts after login, so a message queued without one is never read
        // and StopAsync would wait on the drain forever.
        if (_client.LoginState != LoginState.LoggedIn)
        {
            Console.WriteLine($"Discord is not logged in; dropping message: {message.StringMessage ?? message.EmbedMessage?.Title}");
            return;
        }
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