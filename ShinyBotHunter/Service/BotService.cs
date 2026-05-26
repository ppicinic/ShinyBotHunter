using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.BotEvents.Target;
using ShinyBotHunter.Macro;
using System.Runtime.CompilerServices;
using System.Text.Json;
using static ShinyBotHunter.Service.PhaseInfoService;

namespace ShinyBotHunter.Service
{
    public class BotService
    {
        private int maxEmulators;
        private BotFactory botFactory;
        private IServiceProvider provider;
        private EventArgumentOptions[] specialEvents;
        private IHubContext<ElectronHub, IElectronClient> hubContext;
        private string bizHawkMode;
        public BotService(IOptions<ApplicationSettings> appSettings, BotFactory botFactory, IServiceProvider provider, IHubContext<ElectronHub, IElectronClient> hubContext)
        {
            this.maxEmulators = appSettings.Value.MaxEmulators["DS"];
            this.bizHawkMode = appSettings.Value.BizHawkMode;
            this.botFactory = botFactory;
            this.provider = provider;
            specialEvents = Array.Empty<EventArgumentOptions>();
            this.hubContext = hubContext;
        }

        public void Init()
        {
            string json = File.ReadAllText("./Configs/SpecialEvents/SpecialEvents.json");
            specialEvents = JsonSerializer.Deserialize<EventArgumentOptions[]>(json)!;
        }

        private Dictionary<int, BotAndOrEvent> botEvents = new Dictionary<int, BotAndOrEvent>();
        public async Task<Dictionary<int, EmulatorState>> ExecuteNewBotOrSpecialEvent(NewEventArguments args)
        {
            var positions = botEvents.Values.Aggregate(new List<int>(), (acc, x) => { acc.AddRange(x.GetBotPositions()); return acc; });
            positions.Sort();
            if (positions.Count() >= maxEmulators)
            {
                throw new Exception("No open space to start any new emulators");
            }
            int newPos = positions.Count() == 0 ? 0 : -1;
            for (int i = 0; i < positions.Count() && newPos == -1; i++)
            {
                if (positions[i] != i)
                {
                    newPos = i;
                }
            }
            if(newPos == -1)
            {
                newPos = positions.Count();
            }
            if (args.EventName == "Launch Bot")
            {
                if(args is not null && args.GameName is not null)
                {
                    var gameContext = botFactory.BuildGameContext(args.GameName, -1, newPos, args.SoundOn);
                    var bot = botFactory.CreateBot(gameContext);
                    await bot.LaunchAsync();
                    botEvents.Add(newPos, new BotAndOrEvent(bot) { BotPosition = newPos });
                    return GetBotDashboardState();
                }
                else
                {
                    throw new ArgumentException("No game name was supplied to start bot");
                }
            }
            else if(args.EventName == "Launch Bot + Continue")
            {
                if (args is not null && args.GameName is not null)
                {
                    var gameContext = botFactory.BuildGameContext(args.GameName, -1, newPos, args.SoundOn);
                    var bot = botFactory.CreateBot(gameContext);
                    await bot.LaunchAsync();
                    botEvents.Add(newPos, new BotAndOrEvent(bot) { BotPosition = newPos });
                    await Task.Delay(4000);
                    bot.RunMacroString(MacroRegistry.Instance.GetMacro(gameContext.GameFlavor, "ContinueGame"));
                    await bot.WaitForMacroAsync();
                    return GetBotDashboardState();
                }
                else
                {
                    throw new ArgumentException("No game name was supplied to start bot");
                }
            }
            else if(args.EventName == "HGSS Shiny Starter Hunt")
            {
                if(positions.Count() == 0)
                {
                    var ev = provider.GetRequiredService<MultiShinyHunterEvent>();
                    var botEvent = new BotAndOrEvent();
                    botEvent.StartSpecialEvent(ev, "HGSS Shiny Starter Hunt", botEvents);
                    botEvents.Add(newPos, botEvent);
                    return GetBotDashboardState();
                }
                else
                {
                    throw new Exception("Multi shiny hunting in HGSS can be the only active bot[s].");
                }
            }
            else
            {
                throw new Exception($"{args.EventName} does not exist.");
            }
        }

        public Dictionary<int, EmulatorState> ExecuteEvent(int pos, NewEventArguments args)
        {
            if (!botEvents.ContainsKey(pos))
            {
                throw new ArgumentException($"There is not bot at position: {pos}");
            }
            var validActions = GetAvailableActions(pos);
            if(validActions.Where(x => x.Name == args.EventName).Count() == 0)
            {
                throw new ArgumentException($"{args.EventName} is not a valid action to run currently.");
            }
            if(args.EventName == "Wild Encounter")
            {
                if(args.PhaseName is null || args.TargetType is null)
                {
                    throw new ArgumentException($"PhaseName cannot be null for Wild Pokemon Encounter Event.");
                }
                if(args.EncounterType is null)
                {
                    throw new ArgumentException($"EncounterType must be supplied for Wild Pokemon Encounter Event");
                }
                var targetType = Enum.Parse<TargetType>(args.TargetType);
                ITarget target = targetType == TargetType.PhaseTarget
                                                    ? new PhaseTarget(provider, GameFlavor.HGSS, args.PhaseName, botEvents[pos].Bot!.GameFileName)
                                                    : new ShinyTarget();
                int xRange = args.xRange ?? 0;
                int yRange = args.yRange ?? 0;

                if(args.EncounterType == EncounterType.Run)
                {
                    if(yRange < 0 || xRange < 0)
                    {
                        throw new ArgumentException($"X and Y range must be non-negative values");
                    }
                    if(yRange == 0 && xRange == 0)
                    {
                        throw new ArgumentException($"A x or y range must be supplied for Run Encounter Event");
                    }
                    if(yRange > 0 && xRange > 0)
                    {
                        throw new ArgumentException($"Only x or y range must be supplied, not both.");
                    }
                }
                var wildPokemonEvent = new OrchestratorEvent(provider, new EncounterParams(args.EncounterType.Value, yRange, xRange));
                botEvents[pos].GameState = new GameState()
                {
                    OriginalBattleMode = BattleMode.Flee,
                    GameFlavor = GameFlavor.HGSS,
                    BotService = this,
                    Target = target,
                    Milestone = args.PhaseName,
                    Bot = botEvents[pos].Bot!,
                };
                botEvents[pos].GameState!.State.Push(EventState.Init);
                botEvents[pos].StartEvent(wildPokemonEvent, botEvents[pos].GameState!, args.PhaseName, this);

                return GetBotDashboardState();
            }
            else if(args.EventName == "Resume")
            {
                botEvents[pos].GameState!.SwapState(EventState.Resume);
                botEvents[pos].StartEvent(botEvents[pos].Event!, botEvents[pos].GameState!, botEvents[pos].GetName(pos), this);
                return GetBotDashboardState();
            }
            else
            {
                throw new Exception($"{args.EventName} does not exist.");
            }
        }

        public List<EventArgumentOptions> GetAvailableActions(int pos)
        {
            if (!botEvents.ContainsKey(pos))
            {
                return [];
            }
            var botEvent = botEvents[pos];
            if(botEvent.GameState is not null && botEvent.GameState.State.Count > 0 && botEvent.GameState.State.Peek() == EventState.Waiting)
            {
                return [actionMap["Resume"]];
            }
            else if(botEvent.Event is null || (botEvent.GameState is not null && (botEvent.GameState.State.Count == 0 || botEvent.GameState.State.Peek() == EventState.Complete)))
            {
                // TODO continue game.
                return [actionMap["Wild Encounter"]];
            }
            else
            {
                return [];
            }
        }

        private static Dictionary<string, EventArgumentOptions> actionMap = new Dictionary<string, EventArgumentOptions>()
        {
            {"Continue Game", new EventArgumentOptions() { Name = "Continue Game" } },
            {"Wild Encounter", new EventArgumentOptions() { Name = "Wild Encounter", RequiredParameters = ["PhaseName", "TargetType", "EncounterType"], 
                                ConditionalParameters = [new ConditionalParameters() { Condition = new Condition() { ConditionType = ConditionType.Eq, Name = "EncounterType", Value = "Run" }, Name = "xRange" },
                                    new ConditionalParameters() { Condition = new Condition() { ConditionType = ConditionType.Eq, Name = "EncounterType", Value = "Run" }, Name = "yRange" }
                                ]} },
            {"Resume", new EventArgumentOptions() { Name = "Resume" } }
        };

        public Dictionary<int, EmulatorState> GetBotDashboardState()
        {
            return botEvents.Select(x => new KeyValuePair<int, EmulatorState>(x.Key, new EmulatorState(x.Value.GetName(x.Key), x.Value.GameState is not null && x.Value.GameState.State.Count > 0 ? x.Value.GameState.State.Peek() : EventState.None, GameFlavor.HGSS))).ToDictionary();
        }

        public async Task<Dictionary<int, EmulatorState>> CancelBotOrEventAsync(int pos)
        {
            bool close = await botEvents[pos].CancelAsync();
            if (close)
            {
                botEvents.Remove(pos);
            }
            return GetBotDashboardState();
        }

        public EventArgumentOptions[] GetTopLevelOptions()
        {
            return specialEvents;
        }

        public async Task RefreshDashboardState()
        {
            await this.hubContext.Clients.All.ReceiveDashboardState(GetBotDashboardState());
        }
    }

    public enum TargetType
    {
        PhaseTarget,
        ShinyTarget
    }

    public record EmulatorState(string Name, EventState EventState, GameFlavor GameFlavor);

    

    public class EventArgumentOptions
    {
        public string Name { get; set; } = null!;
        public string[]? RequiredParameters { get; set; }
        public string[]? OptionalParameters { get; set; }
        public ConditionalParameters[] ConditionalParameters { get; set; } = [];
    }

    public class ConditionalParameters
    {
        public Condition Condition { get; set; } = null!;
        public string Name { get; set; } = null!;
        public bool HideIfConditionNotMet { get; set; } = true;
    }

    public class Condition
    {
        public ConditionType ConditionType { get; set; }
        public string Name { get; set; } = null!;
        public string Value { get; set; } = null!;
    }


    public enum ConditionType
    {
        Eq
    }

    public class NewEventArguments
    {
        public string EventName { get; set; } = "";
        public string? GameName { get; set; }
        public string? PhaseName { get; set; }
        public string? TargetType { get; set; }
        public bool? SoundOn { get; set; }
        public EncounterType? EncounterType { get; set; }
        public int? yRange { get; set; }
        public int? xRange { get; set; }
    }

    public class BotAndOrEvent
    {
        public BotAndOrEvent() { }
        public BotAndOrEvent(IBot bot) 
        {
            Bot = bot;
        }
        public int? BotPosition { get; set; }
        public IBot? Bot { get; set; }
        public IBotEvent? Event { get; set; }
        public Task? EventTask { get; set; }
        public GameState? GameState { get; set; }
        public CancellationTokenSource? CancellationTokenSource { get; set; }
        private string? eventName;
        public string GetName(int pos)
        {
            return eventName is not null ? eventName : $"Bot {pos}";
        }
        public async Task<bool> CancelAsync()
        {
            bool hasEvent = false;
            if (EventTask is not null)
            {
                CancellationTokenSource!.Cancel();
                await EventTask;
                hasEvent = true;
                EventTask = null;
                Event = null;
                GameState = null;
                CancellationTokenSource = null;
            }
            if(Event is not null)
            {
                Event = null;
                hasEvent = true;
            }
            if (Bot is not null && !hasEvent)
            {
                await Bot.DisposeAsync();
                Bot = null;
            }
            return Bot is null;
        }

        public int[] GetBotPositions()
        {
            if (Event is not null)
            {
                if(Event.BotPositions is not null && Event.BotPositions.Length > 0)
                {
                    return Event.BotPositions;
                }
            }
            if(BotPosition is not null)
            {
                return [BotPosition.Value];
            }
            return [];
        }

        // TODO refactor this we shouldn't pass botService like this.
        public void StartEvent(IBotEvent botEvent, GameState gameState, string name, BotService botService)
        {
            Event = botEvent;
            eventName = name;
            CancellationTokenSource = new CancellationTokenSource();
            EventTask = Event.ExecuteAsync(gameState, CancellationTokenSource.Token)
                .ContinueWith(async (t) =>
                {
                    if (!CancellationTokenSource.IsCancellationRequested)
                    {
                        await CancellationTokenSource.CancelAsync();
                        if (gameState.State.Count > 0 && gameState.State.Peek() == EventState.Waiting)
                        {
                            EventTask = null;
                            CancellationTokenSource = null;
                        }
                        else
                        {
                            Event = null;
                            EventTask = null;
                            CancellationTokenSource = null;
                        }
                        await botService.RefreshDashboardState();
                    }
                });
        }

        public void StartSpecialEvent(IBotEvent botEvent, string name, Dictionary<int, BotAndOrEvent> placements)
        {
            Event = botEvent;
            eventName = name;
            CancellationTokenSource = new CancellationTokenSource();
            EventTask = Event.ExecuteAsync(new GameState(), CancellationTokenSource.Token)
                .ContinueWith(async (t) =>
                {
                    if (!CancellationTokenSource.IsCancellationRequested)
                    { 
                        await CancellationTokenSource.CancelAsync();
                        placements.Remove(GetBotPositions()[0]);
                    }
                });
        }
    }
}
