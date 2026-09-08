using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Context;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.BotEvents.Target;
using ShinyBotHunter.Macro;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        private ApplicationSettings settings;
        private RomInfoService romInfoService;
        public BotService(IOptions<ApplicationSettings> appSettings, BotFactory botFactory, IServiceProvider provider, IHubContext<ElectronHub, IElectronClient> hubContext, RomInfoService romInfoService)
        {
            this.settings = appSettings.Value;
            this.romInfoService = romInfoService;
            // Display slots and ports are one pool shared by every console, so the ceiling on
            // concurrently running emulators is the sum of the per-console budgets.
            this.maxEmulators = appSettings.Value.TotalMaxEmulators;
            this.bizHawkMode = appSettings.Value.BizHawkMode;
            this.botFactory = botFactory;
            this.provider = provider;
            specialEvents = Array.Empty<EventArgumentOptions>();
            this.hubContext = hubContext;
        }

        public void Init()
        {
            string json = File.ReadAllText("./Configs/SpecialEvents/SpecialEvents.json");
            // String enum names so a condition reads "Eq" in the config rather than 0, and comments
            // so the file can explain itself the way the event configs do.
            var options = new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter() },
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            specialEvents = JsonSerializer.Deserialize<EventArgumentOptions[]>(json, options)!;
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
                    botEvents.Add(newPos, new BotAndOrEvent(bot) { BotPosition = newPos, Context = gameContext });
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
                    botEvents.Add(newPos, new BotAndOrEvent(bot) { BotPosition = newPos, Context = gameContext });
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
                // Off the menu, which now reaches this through the unified action's HGSS mode, but
                // still accepted so a direct API call or a saved script keeps working.
                return StartHgssStarterHunt(positions, newPos);
            }
            else if(args.EventName == "Shiny Starter Hunt" && StarterHuntMode(args.Mode) == GameFlavor.HGSS)
            {
                // One menu entry, two hunts. HGSS keeps the event it has always had; only the way it
                // is reached changed.
                return StartHgssStarterHunt(positions, newPos);
            }
            else if(args.EventName == "Shiny Starter Hunt")
            {
                if (positions.Count() != 0)
                {
                    throw new Exception("The shiny starter hunt manages its own emulators, so it has to be the only thing running.");
                }
                if (string.IsNullOrWhiteSpace(args.PhaseName)) throw new ArgumentException("A milestone must be chosen for the shiny starter hunt.");

                // Each queue entry picks its own ROM, starter and recording label, like chained
                // encounter legs. The flat fields remain usable as a single-entry shorthand.
                List<NewEventLegArguments> rawEntries;
                if (args.Legs is { Count: > 0 })
                {
                    rawEntries = args.Legs;
                }
                else if (!string.IsNullOrWhiteSpace(args.GameName) && !string.IsNullOrWhiteSpace(args.Starter))
                {
                    rawEntries = [new NewEventLegArguments { GameName = args.GameName, Starter = args.Starter, RecordingName = args.RecordingName }];
                }
                else
                {
                    throw new ArgumentException("At least one starter entry (game + starter) is required for the shiny starter hunt.");
                }

                var entries = new List<StarterHuntEntry>();
                RomInfo? firstRom = null;
                foreach (NewEventLegArguments raw in rawEntries)
                {
                    if (string.IsNullOrWhiteSpace(raw.GameName)) throw new ArgumentException("Every starter entry must pick a game.");
                    if (string.IsNullOrWhiteSpace(raw.Starter)) throw new ArgumentException("Every starter entry must pick a starter.");

                    RomInfo rom = romInfoService.GetByHandle(raw.GameName)
                        ?? throw new ArgumentException($"No ROM named '{raw.GameName}' was found.");
                    if (rom.Flavor != GameFlavor.RS)
                    {
                        throw new ArgumentException(
                            $"'{raw.GameName}' is {rom.DisplayName}; the starter hunt is implemented for Ruby/Sapphire.");
                    }
                    firstRom ??= rom;

                    string entryRecording = string.IsNullOrWhiteSpace(raw.RecordingName) ? raw.GameName : raw.RecordingName;
                    entries.Add(new StarterHuntEntry(raw.GameName, raw.Starter, entryRecording, raw.RequireFemale ?? false));
                }

                int budget = settings.MaxEmulatorsFor(firstRom!.Console);
                int requested = args.Emulators ?? 1;
                if (requested < 1 || requested > budget)
                {
                    throw new ArgumentException($"Emulators must be between 1 and {budget} for {firstRom.Console}.");
                }

                var hunt = new MultiStarterHuntEvent(entries, args.PhaseName, requested, budget, StarterHuntConfig.Load(), provider);

                // The shared state is what the dashboard reads: the event holds it at Waiting while a
                // shiny is parked, which is what surfaces Resume.
                var huntState = new GameState { GameFlavor = GameFlavor.RS, BotService = this, Milestone = args.PhaseName, Target = new ShinyTarget() };
                huntState.State.Push(EventState.Running);
                var huntEvent = new BotAndOrEvent { GameState = huntState };
                huntEvent.StartSpecialEvent(hunt, $"Shiny hunt: {string.Join(", ", entries.Select(e => e.Starter))}", botEvents, huntState);
                botEvents.Add(newPos, huntEvent);
                return GetBotDashboardState();
            }
            else
            {
                throw new Exception($"{args.EventName} does not exist.");
            }
        }

        /// <summary>
        /// Which hunt the unified action means. Absent is RS, so a caller that posts starter entries
        /// without a mode behaves exactly as it did before the two hunts shared one action.
        /// </summary>
        private static GameFlavor StarterHuntMode(string? mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
            {
                return GameFlavor.RS;
            }
            if (!Enum.TryParse(mode, ignoreCase: true, out GameFlavor flavor))
            {
                throw new ArgumentException(
                    $"'{mode}' is not a starter hunt mode. Available: {string.Join(", ", Enum.GetNames<GameFlavor>())}.");
            }
            return flavor;
        }

        /// <summary>
        /// The HGSS hunt, unchanged. It predates the generalized RS hunt and stays its own event; the
        /// unified dashboard action is a grouping, not a merge.
        /// </summary>
        private Dictionary<int, EmulatorState> StartHgssStarterHunt(List<int> positions, int newPos)
        {
            if (positions.Count() != 0)
            {
                throw new Exception("Multi shiny hunting in HGSS can be the only active bot[s].");
            }

            var ev = provider.GetRequiredService<MultiShinyHunterEvent>();
            var botEvent = new BotAndOrEvent();
            botEvent.StartSpecialEvent(ev, "HGSS Shiny Starter Hunt", botEvents);
            botEvents.Add(newPos, botEvent);
            return GetBotDashboardState();
        }

        public async Task<Dictionary<int, EmulatorState>> ExecuteEvent(int pos, NewEventArguments args)
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
                var leg = ValidateAndBuildLeg(args.ToLeg(), botEvents[pos].Bot!.GameFileName, botEvents[pos].Bot!.GameFlavor);

                var wildPokemonEvent = new OrchestratorEvent(provider, leg.EncounterParams);
                botEvents[pos].GameState = NewGameStateForLeg(pos, leg);
                botEvents[pos].GameState!.State.Push(EventState.Init);
                botEvents[pos].StartEvent(wildPokemonEvent, botEvents[pos].GameState!, leg.PhaseName, this);

                return GetBotDashboardState();
            }
            else if(args.EventName == "Chained Encounter")
            {
                if (args.Legs is null || args.Legs.Count == 0)
                {
                    throw new ArgumentException("At least one leg must be supplied for Chained Encounter.");
                }
                // Validate every leg before anything starts -- a bad leg 3 shouldn't be discovered
                // after leg 1 has already been running for an hour.
                var legs = args.Legs.Select(x => ValidateAndBuildLeg(x, botEvents[pos].Bot!.GameFileName, botEvents[pos].Bot!.GameFlavor)).ToList();

                var chainedEvent = new ChainedOrchestratorEvent(provider, legs);
                // The chain shares one GameState across all legs so the start tile survives leg
                // boundaries; ChainedOrchestratorEvent re-applies each leg's own fields as it goes.
                botEvents[pos].GameState = NewGameStateForLeg(pos, legs[0]);
                botEvents[pos].GameState!.State.Push(EventState.Init);
                string name = $"Chain ({legs.Count}): {string.Join(" -> ", legs.Select(x => x.PhaseName))}";
                botEvents[pos].StartEvent(chainedEvent, botEvents[pos].GameState!, name, this);

                return GetBotDashboardState();
            }
            else if(args.EventName == "HGSS Togepi Egg")
            {
                if (args.PhaseName is null)
                {
                    throw new ArgumentException("PhaseName cannot be null for the Togepi egg hunt.");
                }
                // Before the event is constructed or started, so a full party comes back as a plain
                // error on the start request instead of faulting a background task the dashboard
                // cannot report on.
                await EnsurePartyHasRoomForEggAsync(pos);
                // Constructing the event loads and validates its config, so a corridor or a touch
                // coordinate that was never measured fails here with a message rather than after the
                // dashboard has already reported the hunt as running.
                var eggEvent = new HgssTogepiEggEvent(botEvents[pos].Bot!, botEvents[pos].Bot!.GameFlavor, args.PhaseName, provider, this);
                botEvents[pos].GameState = new GameState()
                {
                    GameFlavor = botEvents[pos].Bot!.GameFlavor,
                    BotService = this,
                    Bot = botEvents[pos].Bot!,
                    Milestone = args.PhaseName,
                    // Nothing in this event consults the target -- the egg is the only thing it ever
                    // looks at -- but GameState requires one and shiny is what the hunt is for.
                    Target = new ShinyTarget(),
                };
                botEvents[pos].StartEvent(eggEvent, botEvents[pos].GameState!, $"Togepi Egg: {args.PhaseName}", this);

                return GetBotDashboardState();
            }
            else if(args.EventName == "Resume")
            {
                if (botEvents[pos].Event is MultiStarterHuntEvent multiHunt)
                {
                    // The hunt's main task is still running; Resume is a call into it, not a restart.
                    var resumeState = new GameState { GameFlavor = GameFlavor.RS, BotService = this, Target = new ShinyTarget() };
                    resumeState.State.Push(EventState.Resume);
                    _ = multiHunt.ExecuteWithResultAsync(resumeState, CancellationToken.None)
                        .ContinueWith(t =>
                        {
                            if (t.IsFaulted) Console.WriteLine($"[StarterHunt] Resume failed: {t.Exception?.Flatten().InnerException?.Message}");
                            _ = hubContext.Clients.All.ReceiveDashboardState(GetBotDashboardState());
                        });
                    return GetBotDashboardState();
                }
                botEvents[pos].GameState!.SwapState(EventState.Resume);
                botEvents[pos].StartEvent(botEvents[pos].Event!, botEvents[pos].GameState!, botEvents[pos].GetName(pos), this);
                return GetBotDashboardState();
            }
            else
            {
                throw new Exception($"{args.EventName} does not exist.");
            }
        }

        /// <summary>
        /// Rejects an egg hunt whose party has no free slot.
        ///
        /// A gift egg has to go somewhere: with a full party the NPC never hands it over, so no
        /// attempt could ever complete. Checked here rather than inside the event so the operator gets
        /// a plain error back from the start request -- an event that throws on its first await only
        /// faults its own task, which the dashboard reports as nothing more than the bot vanishing.
        /// </summary>
        private async Task EnsurePartyHasRoomForEggAsync(int pos)
        {
            const int fullPartySize = 6;
            List<PartySlotInfo> party;
            try
            {
                party = await GetPartyAsync(pos);
            }
            catch (Exception ex)
            {
                // A transient bad read is not grounds for refusing to start -- the event polls the
                // party itself and reports properly if this is more than a blip.
                Console.WriteLine($"[BotService] Could not read the party to check for a free egg slot: {ex.Message}");
                return;
            }
            if (party.Count >= fullPartySize)
            {
                throw new InvalidOperationException(
                    $"Party is full ({party.Count}/{fullPartySize}). The egg needs a free party slot or the NPC will not hand it over. Deposit or release a pokemon before starting this hunt.");
            }
        }

        /// <summary>
        /// Validates one encounter leg's arguments and resolves its target. Shared by the single
        /// "Wild Encounter" action and every leg of a "Chained Encounter", so both reject the same
        /// bad input.
        /// </summary>
        private EventLeg ValidateAndBuildLeg(NewEventLegArguments args, string gameFileName, GameFlavor flavor)
        {
            if (args.PhaseName is null || args.TargetType is null)
            {
                throw new ArgumentException($"PhaseName cannot be null for Wild Pokemon Encounter Event.");
            }
            if (args.EncounterType is null)
            {
                throw new ArgumentException($"EncounterType must be supplied for Wild Pokemon Encounter Event");
            }
            var targetType = Enum.Parse<TargetType>(args.TargetType);
            ITarget target = targetType switch
            {
                TargetType.PhaseTarget => new PhaseTarget(provider, flavor, args.PhaseName, gameFileName),
                TargetType.NoTarget => new NoTarget(),
                _ => new ShinyTarget(),
            };
            int xRange = args.xRange ?? 0;
            int yRange = args.yRange ?? 0;

            if (args.EncounterType == EncounterType.Run)
            {
                if (yRange < 0 || xRange < 0)
                {
                    throw new ArgumentException($"X and Y range must be non-negative values");
                }
                if (yRange == 0 && xRange == 0)
                {
                    throw new ArgumentException($"A x or y range must be supplied for Run Encounter Event");
                }
                if (yRange > 0 && xRange > 0)
                {
                    throw new ArgumentException($"Only x or y range must be supplied, not both.");
                }
            }
            TrainType trainType = args.TrainType ?? TrainType.None;
            if (trainType != TrainType.None && (args.TargetLevel is null || args.TargetLevel <= 0))
            {
                throw new ArgumentException("TargetLevel must be a positive value when a train mode is enabled.");
            }
            // Nothing is ever a target here and there's no milestone to finish, so the training
            // requirement is the only thing that can end the event. Without one it runs forever.
            if (targetType == TargetType.NoTarget && trainType == TrainType.None)
            {
                throw new ArgumentException("A train mode must be supplied when TargetType is NoTarget, otherwise the event has no stopping condition.");
            }
            int? switchSlot = null;
            if (trainType == TrainType.Switch)
            {
                // Slot 1 holds the pokemon being trained, so it can never also be the battler.
                switchSlot = args.SwitchSlot ?? 2;
                if (switchSlot < 2 || switchSlot > 6)
                {
                    throw new ArgumentException("SwitchSlot must be between 2 and 6 for Switch train mode.");
                }
            }

            return new EventLeg(
                target,
                args.PhaseName,
                new EncounterParams(args.EncounterType.Value, yRange, xRange),
                trainType,
                trainType != TrainType.None ? args.TargetLevel : null,
                switchSlot);
        }

        private GameState NewGameStateForLeg(int pos, EventLeg leg)
        {
            return new GameState()
            {
                OriginalBattleMode = leg.TrainType != TrainType.None ? BattleMode.Battle : BattleMode.Flee,
                GameFlavor = botEvents[pos].Bot!.GameFlavor,
                BotService = this,
                Target = leg.Target,
                Milestone = leg.PhaseName,
                Bot = botEvents[pos].Bot!,
                TrainType = leg.TrainType,
                TargetLevel = leg.TargetLevel,
                SwitchSlot = leg.SwitchSlot,
            };
        }

        /// <summary>
        /// Reads the live party for the bot at <paramref name="pos"/> so the dashboard can offer real
        /// pokemon when picking a switch-train battler. Party-only read -- deliberately not GetPokemonState,
        /// which also pulls all 18 boxes and is far too slow for a UI dropdown.
        /// </summary>
        public async Task<List<PartySlotInfo>> GetPartyAsync(int pos)
        {
            if (!botEvents.ContainsKey(pos) || botEvents[pos].Bot is null)
            {
                throw new ArgumentException($"There is not bot at position: {pos}");
            }
            var reader = provider.GetRequiredService<PokemonStateReader>();
            var partyState = await reader.GetPartyState(botEvents[pos].Bot!);
            var result = new List<PartySlotInfo>();
            for (int i = 0; i < partyState.PartySize; i++)
            {
                var pokemon = partyState.PartyPokemon[i];
                if (pokemon is null)
                {
                    continue;
                }
                result.Add(new PartySlotInfo(i + 1, pokemon.Name, pokemon.Level, pokemon.HP, pokemon.MaxHP, pokemon.IsShiny));
            }
            return result;
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
                var actions = new List<EventArgumentOptions>
                {
                    actionMap["Wild Encounter"], actionMap["Chained Encounter"], actionMap["HGSS Togepi Egg"],
                };
                return actions;
            }
            else
            {
                return [];
            }
        }

        // The parameters of a single encounter. Also serves as the per-leg schema of a chain, so the
        // dashboard renders one set of fields per leg without a second definition to keep in sync.
        private static EventArgumentOptions EncounterParameters(string name) => new EventArgumentOptions()
        {
            Name = name,
            RequiredParameters = ["PhaseName", "TargetType", "EncounterType"],
            OptionalParameters = ["TrainType"],
            ConditionalParameters = [new ConditionalParameters() { Condition = new Condition() { ConditionType = ConditionType.Eq, Name = "EncounterType", Value = "Run" }, Name = "xRange" },
                new ConditionalParameters() { Condition = new Condition() { ConditionType = ConditionType.Eq, Name = "EncounterType", Value = "Run" }, Name = "yRange" },
                new ConditionalParameters() { Condition = new Condition() { ConditionType = ConditionType.Neq, Name = "TrainType", Value = "None" }, Name = "TargetLevel" },
                new ConditionalParameters() { Condition = new Condition() { ConditionType = ConditionType.Eq, Name = "TrainType", Value = "Switch" }, Name = "SwitchSlot" }
            ]
        };

        private static Dictionary<string, EventArgumentOptions> actionMap = new Dictionary<string, EventArgumentOptions>()
        {
            {"Continue Game", new EventArgumentOptions() { Name = "Continue Game" } },
            {"Wild Encounter", EncounterParameters("Wild Encounter") },
            {"Chained Encounter", new EventArgumentOptions() { Name = "Chained Encounter", LegParameters = EncounterParameters("Leg") } },
            // Everything else the egg hunt needs -- macros, corridor, touch coordinates -- lives in
            // Configs/Events/TogepiEgg.json, so the only thing to pick here is where to log it.
            {"HGSS Togepi Egg", new EventArgumentOptions() { Name = "HGSS Togepi Egg", RequiredParameters = ["PhaseName"] } },
            // Macros, timings and the save-menu id live in Configs/Events/RsStarterHunt.json, so the
            // only per-hunt choices are which starter to take and what to file the encounters under.
            {"Resume", new EventArgumentOptions() { Name = "Resume" } }
        };

        /// <summary>The bot at a display position, or null when nothing is running there.</summary>
        public IBot? GetBotAt(int pos)
            => botEvents.TryGetValue(pos, out var entry) ? entry.Bot : null;

        /// <summary>True when an event currently owns the bot at this position.</summary>
        public bool HasRunningEvent(int pos)
            => botEvents.TryGetValue(pos, out var entry) && entry.Event is not null;

        public Dictionary<int, EmulatorState> GetBotDashboardState()
        {
            return botEvents.Select(x => new KeyValuePair<int, EmulatorState>(x.Key, new EmulatorState(x.Value.GetName(x.Key), x.Value.GameState is not null && x.Value.GameState.State.Count > 0 ? x.Value.GameState.State.Peek() : EventState.None, x.Value.Bot?.GameFlavor ?? x.Value.GameState?.GameFlavor ?? GameFlavor.HGSS))).ToDictionary();
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
        ShinyTarget,
        NoTarget
    }

    public record EmulatorState(string Name, EventState EventState, GameFlavor GameFlavor);

    /// <summary>One occupied party slot, as offered to the dashboard for picking a switch-train battler.</summary>
    public record PartySlotInfo(int Slot, string Name, int Level, int Hp, int MaxHp, bool IsShiny);

    

    public class EventArgumentOptions
    {
        public string Name { get; set; } = null!;
        public string[]? RequiredParameters { get; set; }
        public string[]? OptionalParameters { get; set; }
        public ConditionalParameters[] ConditionalParameters { get; set; } = [];
        // Set on actions that take a list of legs instead of a single set of parameters. The
        // dashboard renders one copy of this schema per leg.
        public EventArgumentOptions? LegParameters { get; set; }
        // When set, the legs are only offered while this holds -- an action whose modes do not all
        // take a list. Null means always, which is every action that existed before modes.
        public Condition? LegCondition { get; set; }
    }

    public class ConditionalParameters
    {
        public Condition Condition { get; set; } = null!;
        public string Name { get; set; } = null!;
        public bool HideIfConditionNotMet { get; set; } = true;
        /// <summary>Must be filled before the action can start, while its condition holds.</summary>
        public bool Required { get; set; }
    }

    public class Condition
    {
        public ConditionType ConditionType { get; set; }
        public string Name { get; set; } = null!;
        public string Value { get; set; } = null!;
    }


    public enum ConditionType
    {
        Eq,
        Neq
    }

    /// <summary>Parameters of one encounter leg. A chained action posts a list of these.</summary>
    public class NewEventLegArguments
    {
        public string? PhaseName { get; set; }
        public string? TargetType { get; set; }
        public EncounterType? EncounterType { get; set; }
        public int? yRange { get; set; }
        public int? xRange { get; set; }
        public TrainType? TrainType { get; set; }
        public int? TargetLevel { get; set; }
        public int? SwitchSlot { get; set; }

        // Starter-hunt queue entries. Each entry picks its own ROM and label, the way chained
        // encounter legs each pick their own phase.
        public string? GameName { get; set; }
        public string? Starter { get; set; }
        public string? RecordingName { get; set; }
        public bool? RequireFemale { get; set; }
    }

    public class NewEventArguments
    {
        public string EventName { get; set; } = "";
        /// <summary>Which hunt an action that covers several game flavors should run.</summary>
        public string? Mode { get; set; }
        public string? GameName { get; set; }
        public string? PhaseName { get; set; }
        public string? TargetType { get; set; }
        public bool? SoundOn { get; set; }
        public EncounterType? EncounterType { get; set; }
        public int? yRange { get; set; }
        public int? xRange { get; set; }
        public TrainType? TrainType { get; set; }
        public int? TargetLevel { get; set; }
        public int? SwitchSlot { get; set; }
        /// <summary>Which starter to take, for the shiny starter hunt.</summary>
        public string? Starter { get; set; }
        /// <summary>How many emulators the shiny starter hunt should run at once.</summary>
        public int? Emulators { get; set; }
        /// <summary>
        /// What to file this hunt's encounters under. Deliberately not GameName, which already means
        /// which ROM to launch and renders as a ROM picker.
        /// </summary>
        public string? RecordingName { get; set; }
        // Only used by chained actions. The flat fields above stay in place for single encounters.
        public List<NewEventLegArguments>? Legs { get; set; }

        public NewEventLegArguments ToLeg()
        {
            return new NewEventLegArguments()
            {
                PhaseName = PhaseName,
                TargetType = TargetType,
                EncounterType = EncounterType,
                yRange = yRange,
                xRange = xRange,
                TrainType = TrainType,
                TargetLevel = TargetLevel,
                SwitchSlot = SwitchSlot,
            };
        }
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

        /// <summary>
        /// How this bot's emulator was built: which ROM file, which console, which flavor. Kept so an
        /// event started later can copy a ROM or resolve a macro without re-deriving any of it.
        /// </summary>
        public GameContext? Context { get; set; }
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
                    // Without this, an unhandled exception in the event loop is indistinguishable
                    // from a clean finish: the task faults, Event gets cleared below, and the bot
                    // silently vanishes from the dashboard with no trace of what happened.
                    if (t.IsFaulted && t.Exception is not null)
                    {
                        Console.WriteLine($"[BotService] Event '{name}' crashed. State: {(gameState.State.Count > 0 ? gameState.State.Peek() : EventState.None)}");
                        Console.WriteLine(t.Exception.Flatten().ToString());
                    }
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

        public void StartSpecialEvent(IBotEvent botEvent, string name, Dictionary<int, BotAndOrEvent> placements, GameState? gameState = null)
        {
            Event = botEvent;
            eventName = name;
            CancellationTokenSource = new CancellationTokenSource();
            // A caller that supplies the state can watch and steer the event while it runs, which is
            // how the multi-starter hunt surfaces Waiting/Resume without ending its task.
            EventTask = Event.ExecuteAsync(gameState ?? new GameState(), CancellationTokenSource.Token)
                .ContinueWith(async (t) =>
                {
                    // A special event dying without a word is how a whole hunt vanishes with no
                    // emulators and no explanation. Whatever else happens, the fault gets printed.
                    if (t.IsFaulted)
                    {
                        Console.WriteLine($"[BotService] Special event '{name}' crashed:");
                        Console.WriteLine(t.Exception!.Flatten().ToString());
                    }
                    if (!CancellationTokenSource.IsCancellationRequested)
                    { 
                        await CancellationTokenSource.CancelAsync();
                        placements.Remove(GetBotPositions()[0]);
                    }
                });
        }
    }
}
