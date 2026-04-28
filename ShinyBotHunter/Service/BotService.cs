using Microsoft.Extensions.Options;
using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents;
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
        public BotService(IOptions<ApplicationSettings> appSettings, BotFactory botFactory, IServiceProvider provider)
        {
            this.maxEmulators = appSettings.Value.MaxEmulators["DS"];
            this.botFactory = botFactory;
            this.provider = provider;
            specialEvents = Array.Empty<EventArgumentOptions>();
        }

        public void Init()
        {
            
            string json = File.ReadAllText("./Configs/SpecialEvents/SpecialEvents.json");
            specialEvents = JsonSerializer.Deserialize<EventArgumentOptions[]>(json)!;
        }

        private Dictionary<int, BotAndOrEvent> botEvents = new Dictionary<int, BotAndOrEvent>();
        public async Task<Dictionary<int, string>> ExecuteNewBotOrSpecialEvent(NewEventArguments args)
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
            if (args.EventName == "Launch Bot")
            {
                if(args is not null && args.GameName is not null)
                {
                    var gameContext = botFactory.BuildGameContext(args.GameName, -1, newPos);
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
            else if(args.EventName == "HGSS Shiny Starter Hunt")
            {
                if(positions.Count() == 0)
                {
                    var ev = provider.GetRequiredService<MultiShinyHunterEvent>();
                    var botEvent = new BotAndOrEvent();
                    botEvent.StartEvent(ev, "HGSS Shiny Starter Hunt", botEvents);
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

        public Dictionary<int, string> GetBotDashboardState()
        {
            return botEvents.Select(x => new KeyValuePair<int, string>(x.Key, x.Value.GetName(x.Key))).ToDictionary();
        }

        public async Task<Dictionary<int, string>> CancelBotOrEventAsync(int pos)
        {
            await botEvents[pos].CancelAllAsync();
            botEvents.Remove(pos);
            return GetBotDashboardState();
        }

        public EventArgumentOptions[] GetTopLevelOptions()
        {
            return specialEvents;
        }
    }

    

    public class EventArgumentOptions
    {
        public string Name { get; set; } = null!;
        public string[]? RequiredParameters { get; set; }
    }

    public class NewEventArguments
    {
        public string EventName { get; set; } = "";
        public string? GameName { get; set; }
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
        public CancellationTokenSource? CancellationTokenSource { get; set; }
        private string? eventName;
        public string GetName(int pos)
        {
            return eventName is not null ? eventName : $"Bot {pos}";
        }
        public async Task CancelAllAsync()
        {
            if (EventTask is not null)
            {
                CancellationTokenSource!.Cancel();
                await EventTask;
            }
            if (Bot is not null)
            {
                await Bot.DisposeAsync();
            }
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
            else if(BotPosition is not null)
            {
                return [BotPosition.Value];
            }
            return [];
        }

        public void StartEvent(IBotEvent botEvent, string name, Dictionary<int, BotAndOrEvent> placements)
        {
            Event = botEvent;
            eventName = name;
            CancellationTokenSource = new CancellationTokenSource();
            EventTask = Event.ExecuteAsync(CancellationTokenSource.Token)
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
