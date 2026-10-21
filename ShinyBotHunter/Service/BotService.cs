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
        private readonly EmulatorSlotRegistry slots;
        private BotFactory botFactory;
        private IServiceProvider provider;
        private EventArgumentOptions[] specialEvents;
        private IHubContext<ElectronHub, IElectronClient> hubContext;
        private ApplicationSettings settings;
        private RomInfoService romInfoService;
        public BotService(IOptions<ApplicationSettings> appSettings, BotFactory botFactory, IServiceProvider provider, IHubContext<ElectronHub, IElectronClient> hubContext, RomInfoService romInfoService)
        {
            this.settings = appSettings.Value;
            this.romInfoService = romInfoService;
            // Display slots and ports are one pool shared by every console, so the ceiling on
            // concurrently running emulators is the sum of the per-console budgets.
            slots = new EmulatorSlotRegistry(appSettings.Value.TotalMaxEmulators);
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

        public async Task<Dictionary<int, EmulatorState>> ExecuteNewBotOrSpecialEvent(NewEventArguments args)
        {
            await slots.Gate.WaitAsync();
            try
            {
                return await ExecuteNewBotOrSpecialEventUnderGate(args);
            }
            finally
            {
                slots.Gate.Release();
            }
        }

        private async Task<Dictionary<int, EmulatorState>> ExecuteNewBotOrSpecialEventUnderGate(NewEventArguments args)
        {
            var (positions, newPos) = slots.AllocatePosition();
            if (args.EventName == "Launch Bot")
            {
                if(args is not null && args.GameName is not null)
                {
                    var gameContext = botFactory.BuildGameContext(args.GameName, -1, newPos, args.SoundOn);
                    var bot = botFactory.CreateBot(gameContext);
                    await bot.LaunchAsync();
                    if (!slots.Entries.TryAdd(newPos, new BotAndOrEvent(bot) { BotPosition = newPos, Context = gameContext }))
                    {
                        throw new InvalidOperationException($"Position {newPos} is already taken.");
                    }
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
                    if (!slots.Entries.TryAdd(newPos, new BotAndOrEvent(bot) { BotPosition = newPos, Context = gameContext }))
                    {
                        throw new InvalidOperationException($"Position {newPos} is already taken.");
                    }
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
                slots.Entries[newPos] = huntEvent;
                StartEvent(newPos, huntEvent, hunt, huntState, $"Shiny hunt: {string.Join(", ", entries.Select(e => e.Starter))}", removeWhenDone: true);
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
            slots.Entries[newPos] = botEvent;
            StartEvent(newPos, botEvent, ev, new GameState(), "HGSS Shiny Starter Hunt", removeWhenDone: true);
            return GetBotDashboardState();
        }

        public async Task<Dictionary<int, EmulatorState>> ExecuteEvent(int pos, NewEventArguments args)
        {
            await slots.Gate.WaitAsync();
            try
            {
                return await ExecuteEventUnderGate(pos, args);
            }
            finally
            {
                slots.Gate.Release();
            }
        }

        private async Task<Dictionary<int, EmulatorState>> ExecuteEventUnderGate(int pos, NewEventArguments args)
        {
            if (!slots.Entries.ContainsKey(pos))
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
                var leg = ValidateAndBuildLeg(args.ToLeg(), slots.Entries[pos].Bot!.GameFileName, slots.Entries[pos].Bot!.GameFlavor);

                var wildPokemonEvent = new OrchestratorEvent(provider, leg.EncounterParams);
                slots.Entries[pos].GameState = NewGameStateForLeg(pos, leg);
                slots.Entries[pos].GameState!.State.Push(EventState.Init);
                StartEvent(pos, slots.Entries[pos], wildPokemonEvent, slots.Entries[pos].GameState!, leg.PhaseName, removeWhenDone: false);

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
                var legs = args.Legs.Select(x => ValidateAndBuildLeg(x, slots.Entries[pos].Bot!.GameFileName, slots.Entries[pos].Bot!.GameFlavor)).ToList();

                var chainedEvent = new ChainedOrchestratorEvent(provider, legs);
                // The chain shares one GameState across all legs so the start tile survives leg
                // boundaries; ChainedOrchestratorEvent re-applies each leg's own fields as it goes.
                slots.Entries[pos].GameState = NewGameStateForLeg(pos, legs[0]);
                slots.Entries[pos].GameState!.State.Push(EventState.Init);
                string name = $"Chain ({legs.Count}): {string.Join(" -> ", legs.Select(x => x.PhaseName))}";
                StartEvent(pos, slots.Entries[pos], chainedEvent, slots.Entries[pos].GameState!, name, removeWhenDone: false);

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
                var eggEvent = new HgssTogepiEggEvent(slots.Entries[pos].Bot!, slots.Entries[pos].Bot!.GameFlavor, args.PhaseName, provider, this);
                slots.Entries[pos].GameState = new GameState()
                {
                    GameFlavor = slots.Entries[pos].Bot!.GameFlavor,
                    BotService = this,
                    Bot = slots.Entries[pos].Bot!,
                    Milestone = args.PhaseName,
                    // Nothing in this event consults the target -- the egg is the only thing it ever
                    // looks at -- but GameState requires one and shiny is what the hunt is for.
                    Target = new ShinyTarget(),
                };
                StartEvent(pos, slots.Entries[pos], eggEvent, slots.Entries[pos].GameState!, $"Togepi Egg: {args.PhaseName}", removeWhenDone: false);

                return GetBotDashboardState();
            }
            else if(args.EventName == "Resume")
            {
                if (slots.Entries[pos].Event is MultiStarterHuntEvent multiHunt)
                {
                    // The hunt's main task is still running; Resume is a call into it, not a restart.
                    // Awaited under the gate, so a second Resume click settles the next parked shiny
                    // rather than the same one, and a fault fails this request.
                    var resumeState = new GameState { GameFlavor = GameFlavor.RS, BotService = this, Target = new ShinyTarget() };
                    resumeState.State.Push(EventState.Resume);
                    await multiHunt.ExecuteWithResultAsync(resumeState, CancellationToken.None);
                    await RefreshDashboardState();
                    return GetBotDashboardState();
                }
                slots.Entries[pos].GameState!.SwapState(EventState.Resume);
                StartEvent(pos, slots.Entries[pos], slots.Entries[pos].Event!, slots.Entries[pos].GameState!, slots.Entries[pos].GetName(pos), removeWhenDone: false);
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
                GameFlavor = slots.Entries[pos].Bot!.GameFlavor,
                BotService = this,
                Target = leg.Target,
                Milestone = leg.PhaseName,
                Bot = slots.Entries[pos].Bot!,
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
            if (!slots.Entries.ContainsKey(pos) || slots.Entries[pos].Bot is null)
            {
                throw new ArgumentException($"There is not bot at position: {pos}");
            }
            var reader = provider.GetRequiredService<PokemonStateReader>();
            var partyState = await reader.GetPartyState(slots.Entries[pos].Bot!);
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
            if (!slots.Entries.TryGetValue(pos, out var botEvent))
            {
                return [];
            }
            // Each field read once: a writer may replace it between two reads. Idleness is
            // EventTask, which only the teardown clears under the gate, so a running loop is never
            // idle whatever its stack shows mid-swap or after it pushed Complete.
            GameState? gameState = botEvent.GameState;
            Task? eventTask = botEvent.EventTask;
            if(gameState is not null && gameState.State.TryPeek(out var top) && top == EventState.Waiting
                && (eventTask is null || botEvent.Event is MultiStarterHuntEvent))
            {
                return [actionMap["Resume"]];
            }
            else if(eventTask is null)
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
            => slots.Entries.TryGetValue(pos, out var entry) ? entry.Bot : null;

        /// <summary>True when an event currently owns the bot at this position.</summary>
        public bool HasRunningEvent(int pos)
            => slots.Entries.TryGetValue(pos, out var entry) && entry.Event is not null;

        public Dictionary<int, EmulatorState> GetBotDashboardState()
        {
            return slots.Entries.Select(x =>
            {
                GameState? gameState = x.Value.GameState;
                EventState top = gameState is not null && gameState.State.TryPeek(out var peeked) ? peeked : EventState.None;
                return new KeyValuePair<int, EmulatorState>(x.Key, new EmulatorState(x.Value.GetName(x.Key), top, x.Value.Bot?.GameFlavor ?? gameState?.GameFlavor ?? GameFlavor.HGSS));
            }).ToDictionary();
        }

        /// <summary>
        /// Cancels the running event at <paramref name="pos"/>, drops a parked one, or closes an idle
        /// bot. A running event is only signalled under the gate and awaited outside it, because its
        /// own teardown in <see cref="RunEventAsync"/> takes the gate; this returns once that teardown
        /// and its dashboard refresh have run.
        /// </summary>
        public async Task<Dictionary<int, EmulatorState>> CancelBotOrEventAsync(int pos)
        {
            Task? running = null;
            await slots.Gate.WaitAsync();
            try
            {
                var entry = slots.Entries[pos];
                if (entry.EventTask is not null)
                {
                    entry.CancellationTokenSource!.Cancel();
                    running = entry.EventTask;
                }
                else if (entry.Event is not null)
                {
                    entry.Event = null;
                    entry.GameState = null;
                }
                else
                {
                    // Dispose never calls back into BotService, so awaiting it under the gate is safe.
                    await entry.Bot!.DisposeAsync();
                    slots.Entries.TryRemove(pos, out _);
                }
            }
            finally
            {
                slots.Gate.Release();
            }
            if (running is not null)
            {
                await running;
            }
            return GetBotDashboardState();
        }

        /// <summary>
        /// Starts <paramref name="ev"/> on <paramref name="entry"/>. Only called under the gate, so
        /// <see cref="RunEventAsync"/> always reaches its gate wait asynchronously and cannot clear
        /// EventTask before this assigns it.
        /// </summary>
        private void StartEvent(int key, BotAndOrEvent entry, IBotEvent ev, GameState state, string name, bool removeWhenDone)
        {
            var cts = new CancellationTokenSource();
            entry.Event = ev;
            entry.EventName = name;
            entry.CancellationTokenSource = cts;
            entry.EventTask = RunEventAsync(key, entry, ev.ExecuteAsync(state, cts.Token), cts, state, name, removeWhenDone);
        }

        /// <summary>
        /// Owns the whole teardown of an event, whether it was cancelled or finished on its own, so
        /// the order is decided in one place. Never faults: an event crash is logged here, and the
        /// dashboard refresh is caught because nothing is guaranteed to await a natural finish.
        /// </summary>
        private async Task RunEventAsync(int key, BotAndOrEvent entry, Task eventTask, CancellationTokenSource cts, GameState state, string name, bool removeWhenDone)
        {
            try
            {
                await eventTask;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                // Without this, an unhandled exception in the event loop is indistinguishable from a
                // clean finish: the bot silently goes idle, or a hunt vanishes, with no trace of why.
                Console.WriteLine($"[BotService] Event '{name}' crashed. State: {(state.State.TryPeek(out var crashedAt) ? crashedAt : EventState.None)}");
                Console.WriteLine(ex.ToString());
            }

            bool cancelled = cts.IsCancellationRequested;
            if (!cancelled)
            {
                await cts.CancelAsync();
            }

            await slots.Gate.WaitAsync();
            try
            {
                if (cancelled)
                {
                    entry.Event = null;
                    entry.GameState = null;
                }
                else if (!(state.State.TryPeek(out var top) && top == EventState.Waiting))
                {
                    entry.Event = null;
                }
                entry.EventTask = null;
                entry.CancellationTokenSource = null;
                if (removeWhenDone)
                {
                    // The key captured at start, not a position read back from the event.
                    slots.Entries.TryRemove(key, out _);
                }
            }
            finally
            {
                slots.Gate.Release();
            }

            try
            {
                await RefreshDashboardState();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BotService] Dashboard refresh after event '{name}' failed: {ex}");
            }
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
}
