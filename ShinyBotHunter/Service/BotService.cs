using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Context;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.BotEvents.Target;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Util;
using System.Runtime.CompilerServices;
using static ShinyBotHunter.Service.PhaseInfoService;

namespace ShinyBotHunter.Service
{
    public class BotService
    {
        private readonly EmulatorSlotRegistry slots;
        private readonly BotEventRunner runner;
        private BotFactory botFactory;
        private IServiceProvider provider;
        private IHubContext<ElectronHub, IElectronClient> hubContext;
        private ApplicationSettings settings;
        private RomInfoService romInfoService;
        private GameProfiles profiles;
        public BotService(IOptions<ApplicationSettings> appSettings, BotFactory botFactory, IServiceProvider provider, IHubContext<ElectronHub, IElectronClient> hubContext, RomInfoService romInfoService, GameProfiles profiles)
        {
            this.profiles = profiles;
            this.settings = appSettings.Value;
            this.romInfoService = romInfoService;
            // Display slots and ports are one pool shared by every console, so the ceiling on
            // concurrently running emulators is the sum of the per-console budgets.
            slots = new EmulatorSlotRegistry(appSettings.Value.TotalMaxEmulators);
            runner = new BotEventRunner(slots, RefreshDashboardState);
            this.botFactory = botFactory;
            this.provider = provider;
            this.hubContext = hubContext;
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
            var entry = EventRegistry.Get(args.EventName);
            if (!entry.OwnsEmulators) throw new ArgumentException($"{args.EventName} is not a top-level action.");
            var (_, newPos) = slots.AllocatePosition();
            return await entry.Create(this, args, newPos);
        }

        internal async Task<Dictionary<int, EmulatorState>> StartLaunchBotAsync(NewEventArguments args, int newPos)
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

        internal async Task<Dictionary<int, EmulatorState>> StartLaunchBotAndContinueAsync(NewEventArguments args, int newPos)
        {
            if (args is not null && args.GameName is not null)
            {
                var gameContext = botFactory.BuildGameContext(args.GameName, -1, newPos, args.SoundOn);
                var bot = botFactory.CreateBot(gameContext);
                await bot.LaunchAsync();
                try
                {
                    await Task.Delay(4000);
                    bot.RunMacroString(MacroRegistry.Instance.GetMacro(gameContext.GameFlavor, "ContinueGame"));
                    await bot.WaitForMacroAsync();
                    // The bot is offered no actions until the save has loaded, so nothing reads the party
                    // at the title screen. Only a profile with a state reader can tell when the field is up.
                    if (bot.Profile.CreateStateReader is not null)
                    {
                        await Overworld.WaitForReadyAsync(bot.State, CancellationToken.None);
                    }
                }
                finally
                {
                    // Registered on a failed load too, so the emulator can still be cancelled from the dashboard.
                    if (!slots.Entries.TryAdd(newPos, new BotAndOrEvent(bot) { BotPosition = newPos, Context = gameContext }))
                    {
                        throw new InvalidOperationException($"Position {newPos} is already taken.");
                    }
                }
                return GetBotDashboardState();
            }
            else
            {
                throw new ArgumentException("No game name was supplied to start bot");
            }
        }

        internal Task<Dictionary<int, EmulatorState>> StartShinyStarterHuntAsync(NewEventArguments args, int newPos)
        {
            if (StarterHuntMode(args.Mode) == GameFlavor.HGSS)
            {
                // One menu entry, two hunts. HGSS keeps the event it has always had; only the way it
                // is reached changed.
                return StartHgssStarterHunt(newPos);
            }
            if (!slots.Entries.IsEmpty)
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
                if (rom.Flavor is not { } flavor)
                {
                    throw new ArgumentException(
                        $"Shiny Starter Hunt needs {nameof(GameCapabilities.HasSoftResetStarterHunt)}; '{raw.GameName}' is {rom.DisplayName}, which has no game profile.");
                }
                RequireCapability(EventRegistry.Get("Shiny Starter Hunt"), profiles.Get(flavor));
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
            var huntState = new GameState { Profile = profiles.Get(GameFlavor.RS), BotService = this, Milestone = args.PhaseName, Target = new ShinyTarget() };
            huntState.State.Push(EventState.Running);
            var huntEvent = new BotAndOrEvent { GameState = huntState };
            slots.Entries[newPos] = huntEvent;
            runner.StartEvent(newPos, huntEvent, hunt, huntState, $"Shiny hunt: {string.Join(", ", entries.Select(e => e.Starter))}", removeWhenDone: true);
            return Task.FromResult(GetBotDashboardState());
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
        internal Task<Dictionary<int, EmulatorState>> StartHgssStarterHunt(int newPos)
        {
            RequireCapability(EventRegistry.Get("HGSS Shiny Starter Hunt"), profiles.Get(GameFlavor.HGSS));
            if (!slots.Entries.IsEmpty)
            {
                throw new Exception("Multi shiny hunting in HGSS can be the only active bot[s].");
            }

            var ev = provider.GetRequiredService<MultiShinyHunterEvent>();
            var botEvent = new BotAndOrEvent();
            slots.Entries[newPos] = botEvent;
            runner.StartEvent(newPos, botEvent, ev, new GameState { Profile = profiles.Get(GameFlavor.HGSS) }, "HGSS Shiny Starter Hunt", removeWhenDone: true);
            return Task.FromResult(GetBotDashboardState());
        }

        internal Task<Dictionary<int, EmulatorState>> StartWishmakerJirachiAsync(NewEventArguments args, int newPos)
        {
            // The hunt holds the one Dolphin, the one DSU port and the one GBA window. Other bots may
            // run beside it.
            if (slots.Entries.Values.Any(e => e.Event is WishmakerJirachiEvent))
            {
                throw new ArgumentException("A Wishmaker Jirachi Hunt is already running.");
            }
            if (string.IsNullOrWhiteSpace(args.GameName)) throw new ArgumentException("A game must be chosen for the Wishmaker Jirachi Hunt.");
            if (string.IsNullOrWhiteSpace(args.PhaseName)) throw new ArgumentException("A milestone must be chosen for the Wishmaker Jirachi Hunt.");

            RomInfo rom = romInfoService.GetByHandle(args.GameName)
                ?? throw new ArgumentException($"No ROM named '{args.GameName}' was found.");
            EventRegistration entry = EventRegistry.Get("Wishmaker Jirachi Hunt");
            if (!entry.Options.GameFilter!.Matches(rom))
            {
                throw new ArgumentException(
                    $"Wishmaker Jirachi Hunt runs only on a tested Ruby or Sapphire ROM (revision 2); {args.GameName} is {rom.DisplayName}.");
            }
            RequireCapability(entry, profiles.Get(GameFlavor.RS));

            // The constructor checks the save and the Dolphin files, so each FR-5 refusal comes back
            // as a 400 before anything launches.
            var hunt = new WishmakerJirachiEvent(args.GameName, args.PhaseName, rom.Extension, newPos, WishmakerJirachiConfig.Load(), provider);

            var huntState = new GameState { Profile = profiles.Get(GameFlavor.RS), BotService = this, Milestone = args.PhaseName, Target = new ShinyTarget() };
            huntState.State.Push(EventState.Running);
            var huntEvent = new BotAndOrEvent { GameState = huntState };
            slots.Entries[newPos] = huntEvent;
            runner.StartEvent(newPos, huntEvent, hunt, huntState, $"Wishmaker Jirachi: {args.GameName}", removeWhenDone: true);
            return Task.FromResult(GetBotDashboardState());
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
                throw new ArgumentException($"There is no bot at position: {pos}");
            }
            var entry = EventRegistry.Get(args.EventName);
            // Before the valid-actions check, which would refuse a filtered-out action without
            // naming the capability. A hunt's entry has no bot; the check below refuses it as before.
            if (slots.Entries[pos].Bot is { } bot) RequireCapability(entry, bot.Profile);
            var validActions = GetAvailableActions(pos);
            if(validActions.Where(x => x.Name == args.EventName).Count() == 0)
            {
                throw new ArgumentException($"{args.EventName} is not a valid action to run currently.");
            }
            var started = await entry.Create(this, args, pos);
            // FR-39b: a hunt that started on a game file moves that file's follows-the-hunt card to its milestone.
            if (slots.Entries[pos] is { Bot: { } hunter, GameState.Milestone: { } milestone }) await provider.GetRequiredService<AdminService>().FollowHuntAsync(hunter.GameFlavor, hunter.GameFileName, milestone);
            return started;
        }

        internal async Task<Dictionary<int, EmulatorState>> StartWildEncounter(NewEventArguments args, int pos)
        {
            var (leg, anchor, lead) = await BuildWildLegAsync(slots.Entries[pos].Bot!, args);
            var wildPokemonEvent = new OrchestratorEvent(provider, leg.EncounterParams, anchor);
            slots.Entries[pos].GameState = NewGameStateForLeg(pos, leg);
            if (lead is not null)
            {
                IReadOnlyList<string> lines = SpeedLinesAtRun(lead);
                foreach (string line in lines)
                {
                    Console.WriteLine($"[BotService] {line}");
                }
                slots.Entries[pos].GameState!.SpeedLines = lines;
                slots.Entries[pos].GameState!.LeadLevel = lead.Level;
            }
            slots.Entries[pos].GameState!.State.Push(EventState.Init);
            runner.StartEvent(pos, slots.Entries[pos], wildPokemonEvent, slots.Entries[pos].GameState!, leg.PhaseName, removeWhenDone: false);

            return GetBotDashboardState();
        }

        // Every refusal a Wild Encounter Start makes, and its leg and anchor; shared by the Wild Encounter Start and each Wild
        // Encounter step of a plan. The lead is read only where the game has the wild run checks.
        private async Task<(EventLeg Leg, MilestoneAnchor? Anchor, PokemonData? Lead)> BuildWildLegAsync(IBot bot, NewEventArguments args)
        {
            GameCapabilities caps = bot.Profile.Capabilities;
            // A greyed choice is refused with its reason before anything is read.
            var ruledOut = DisabledByCapabilities(caps);
            foreach (var (param, value) in new[] { ("EncounterType", args.EncounterType?.ToString()), ("TargetType", args.TargetType), ("TrainType", args.TrainType?.ToString()), ("Pickup", args.Pickup == true ? "true" : null) })
            {
                if (value is not null && ruledOut.TryGetValue(param, out var reasons) && reasons.TryGetValue(value, out var reason))
                {
                    throw new ArgumentException(reason);
                }
            }
            Milestone? generated = RefuseMilestone(bot, args.PhaseName, args.EncounterType);
            // The Plan Editor offers every milestone of the game, so a step's is checked against the file here, as the launch list is.
            // As in Located, the saved milestone is looked up only when there is generated data and a version to read it with.
            if (args.PhaseName is { } phaseName && bot.Profile.Milestones is not null && romInfoService.GetByHandle(bot.GameFileName)?.Version is not null
                && OffFileRefusal(provider.GetRequiredService<PhaseInfoService>().GetPhaseInfo(bot.GameFlavor, phaseName), bot.GameFileName) is { } offFile)
            {
                throw new ArgumentException(offFile);
            }
            // A ROM whose address table has no per-game part cannot train; refused before anything is read (FR-12).
            if ((args.TrainType ?? TrainType.None) != TrainType.None && bot.Profile.TrainingRefusal?.Invoke(bot.Addresses) is { } refusal)
            {
                throw new ArgumentException(refusal);
            }
            if (args.Pickup == true && (args.TrainType ?? TrainType.None) == TrainType.None)
            {
                throw new ArgumentException(PickupNeedsTrainMode);
            }
            PokemonData? lead = null;
            if (caps.HasWildRunChecks)
            {
                lead = await WildRefusalAsync(bot);
            }
            PokemonState? party = await RefuseFromFreshPartyAsync(bot, args.EncounterType == EncounterType.Headbutt, args.Pickup == true);
            EventLeg leg = ValidateAndBuildLeg(args.ToLeg(), bot.GameFileName, bot.GameFlavor);
            var gameFile = await provider.GetRequiredService<MongoGameFileDao>().LoadAsync(bot.GameFileName);
            var (trainerId, caught) = await HeadbuttTreeInputsAsync(bot, gameFile, [(leg.PhaseName, generated)], party);
            (leg, MilestoneAnchor? anchor) = AnchoredLeg(bot, leg, generated, gameFile, trainerId, caught);
            return (leg, anchor, lead);
        }

        // The Plan action (R6b): each step is refused at Start as its own action's Start would refuse it, named by the step
        // (default 7). The run reads the clock first thing, so nothing is read for the pick here.
        internal async Task<Dictionary<int, EmulatorState>> StartPlanAsync(NewEventArguments args, int pos)
        {
            IBot bot = slots.Entries[pos].Bot!;
            Plan plan = await provider.GetRequiredService<MongoHuntPlanDao>().LoadAsync(bot.GameFlavor, args.HuntPlan!)
                ?? throw new ArgumentException($"No hunt plan named {args.HuntPlan}.");
            var steps = new List<(NewEventArguments Body, string Name, EventLeg? Leg, MilestoneAnchor? Anchor)>();
            foreach (var (body, label) in plan.Rules.Select((r, i) => (r.Arguments, $"Rule {i + 1}")).Append((plan.Default, "The default")))
            {
                if (body.EventName == Plan.Wait)
                {
                    steps.Add((body, Plan.Wait, null, null));
                    continue;
                }
                var (leg, anchor, _) = await RefusedAsStepAsync(label, () => BuildWildLegAsync(bot, body));
                steps.Add((body, leg.PhaseName, leg, anchor));
            }
            var state = new GameState { Profile = bot.Profile, BotService = this, Bot = bot };
            state.State.Push(EventState.Init);
            slots.Entries[pos].GameState = state;
            runner.StartEvent(pos, slots.Entries[pos], new PlanEvent(provider, plan, steps), state, PlanEventName(plan.Name), removeWhenDone: false);
            return GetBotDashboardState();
        }

        // A step's refusal with the step's label in front, keeping its type and so its 400.
        internal static async Task<T> RefusedAsStepAsync<T>(string label, Func<Task<T>> build)
        {
            try
            {
                return await build();
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"{label}: {ex.Message}", ex);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException($"{label}: {ex.Message}", ex);
            }
        }

        // Start names a plan's hunt with this, and the in-use check looks for it.
        internal static string PlanEventName(string plan) => "Plan " + plan;

        // FR-37b: a plan is in use while a hunt of it holds its event, running or parked in Waiting, which resumes the same plan.
        internal static string? PlanInUseRefusal(IEnumerable<KeyValuePair<int, BotAndOrEvent>> entries, GameFlavor game, string name)
            => entries.FirstOrDefault(e => e.Value.Event is not null && e.Value.Bot?.GameFlavor == game && e.Value.EventName == PlanEventName(name)) is { Value: not null } inUse
                ? $"{name} is in use by the hunt on bot #{inUse.Key}; cancel that hunt first."
                : null;

        // Under the gate, as Start reads the plan, so a delete never lands between Start's read and its hunt's event name.
        public async Task DeletePlanAsync(GameFlavor game, string name)
        {
            await slots.Gate.WaitAsync();
            try
            {
                if (PlanInUseRefusal(slots.Entries, game, name) is { } inUse)
                {
                    throw new InvalidOperationException(inUse);
                }
                if (!await provider.GetRequiredService<MongoHuntPlanDao>().DeleteAsync(game, name))
                {
                    throw new ArgumentException($"No hunt plan named {name}.");
                }
            }
            finally
            {
                slots.Gate.Release();
            }
        }

        // Under the gate, as DeletePlanAsync, so a save never lands while a hunt of the plan starts (FR-37b).
        public async Task SavePlanAsync(Plan plan)
        {
            await slots.Gate.WaitAsync();
            try
            {
                if (PlanInUseRefusal(slots.Entries, plan.Game, plan.Name) is { } inUse)
                {
                    throw new InvalidOperationException(inUse);
                }
                await provider.GetRequiredService<MongoHuntPlanDao>().SaveAsync(plan);
            }
            finally
            {
                slots.Gate.Release();
            }
        }

        // Refused while in use: the hunt's name would then name no plan, and a delete of the new name would not see the hunt.
        public async Task RenamePlanAsync(GameFlavor game, string name, string? to)
        {
            if (string.IsNullOrWhiteSpace(to))
            {
                throw new ArgumentException("The plan needs a name.");
            }
            await slots.Gate.WaitAsync();
            try
            {
                if (PlanInUseRefusal(slots.Entries, game, name) is { } inUse)
                {
                    throw new InvalidOperationException(inUse);
                }
                var plans = provider.GetRequiredService<MongoHuntPlanDao>();
                Plan plan = await plans.LoadAsync(game, name) ?? throw new ArgumentException($"No hunt plan named {name}.");
                if (await plans.LoadAsync(game, to) is not null)
                {
                    throw new ArgumentException($"A plan named {to} already exists.");
                }
                await plans.SaveAsync(plan with { Name = to });
                await plans.DeleteAsync(game, name);
            }
            finally
            {
                slots.Gate.Release();
            }
        }

        // FR-15a and B2's type rule for one milestone, before anything is read; its location, or null for none.
        private Milestone? RefuseMilestone(IBot bot, string? phaseName, EncounterType? type)
        {
            RefuseNoHuntYet(bot, phaseName);
            Milestone? generated = Located(bot, phaseName);
            if (EncounterTypeRefusal(generated, type) is { } wrongType)
            {
                throw new ArgumentException(wrongType);
            }
            return generated;
        }

        // The saved milestone's location on the file's version, or null: no anchor and no location checks, so it is hunted where
        // the bot stands. The saved milestone is looked up only when there is generated data and a version to read it with.
        private Milestone? Located(IBot bot, string? phaseName)
            => phaseName is not null && bot.Profile.Milestones is not null && romInfoService.GetByHandle(bot.GameFileName)?.Version is { } version
                ? PhaseTarget.FindMilestone(bot.Profile.Milestones, version, provider.GetRequiredService<PhaseInfoService>().GetPhaseInfo(bot.GameFlavor, phaseName)?.Location)
                : null;

        // FR-24 and FR-23 for one validated leg on the file: the leg hunted from its anchor, and the anchor, or null.
        private (EventLeg Leg, MilestoneAnchor? Anchor) AnchoredLeg(IBot bot, EventLeg leg, Milestone? generated, GameFile gameFile,
            uint? trainerId, IReadOnlyList<CaughtPokemon> caught)
        {
            // A location comes only from a saved milestone (Located), so there is one whenever generated is set.
            IEnumerable<int> wanted = generated is null ? [] : MilestoneRows.Wanted(MilestoneRows.Counted(
                provider.GetRequiredService<PhaseInfoService>().GetPhaseInfo(bot.GameFlavor, leg.PhaseName)!, bot.Profile.Milestones,
                romInfoService.GetByHandle(bot.GameFileName)?.Version), caught.Where(c => c.GameFileName == bot.GameFileName));
            MilestoneAnchor? anchor = gameFile.AnchorFor(leg.PhaseName, generated, trainerId, wanted);
            if (anchor is not null && generated is { Modality: Modality.Land }
                && AnchorTileStop(generated, anchor.Anchor, bot.Profile.Maps!, bot.Profile.LandTileKind!(generated)) is { } notOfTheKind)
            {
                throw new InvalidOperationException(notOfTheKind);
            }
            if (anchor is not null)
            {
                leg = leg with { EncounterParams = AnchorLeg(leg.EncounterParams, anchor.Anchor) };
                Console.WriteLine($"[BotService] {generated?.Name ?? leg.PhaseName}: hunting from the anchor map {anchor.Anchor.Map} ({anchor.Anchor.X}, {anchor.Anchor.Y}), "
                    + $"{HowHunted(leg.EncounterParams)} ({(gameFile.Anchors.Any(a => a.Milestone == leg.PhaseName) ? "set on this file" : "generated")})");
            }
            else if (generated is { Modality: Modality.Headbutt })
            {
                Console.WriteLine($"[BotService] {generated.Name}: no generated headbutt stand for this file; hunting where the bot stands");
            }
            return (leg, anchor);
        }

        internal const string TrainerIdUnreadable = "The trainer ID could not be read; start the hunt once the game is in the field.";

        // FR-21 and FR-42: the trainer ID and the catches are read only when a leg's saved milestone is at a headbutt location
        // with no replacement under its name on the file. One with no location has no generated milestone, so it reads nothing new.
        internal static bool NeedsTrainerId(IEnumerable<(string Milestone, Milestone? Generated)> legs, GameFile gameFile)
            => legs.Any(l => l.Generated is { Modality: Modality.Headbutt } && gameFile.Anchors.All(a => a.Milestone != l.Milestone));

        // The ID is a local of this Start, never kept. The party is the one the Headbutt check read, which a headbutt leg
        // always has.
        private async Task<(uint? TrainerId, IReadOnlyList<CaughtPokemon> Caught)> HeadbuttTreeInputsAsync(IBot bot, GameFile gameFile,
            IEnumerable<(string Milestone, Milestone? Generated)> legs, PokemonState? party)
        {
            if (!NeedsTrainerId(legs, gameFile))
            {
                return (null, []);
            }
            uint trainerId = await ReadTrainerIdAsync(bot, (HgssPointerService)bot.Addresses, CancellationToken.None)
                ?? throw new InvalidOperationException(TrainerIdUnreadable);
            var (compared, refusal) = CrossCheckTrainerId(trainerId, party!);
            if (refusal is not null)
            {
                throw new InvalidOperationException(refusal);
            }
            Console.WriteLine($"[BotService] Trainer ID {trainerId} (visible {(ushort)trainerId}), "
                + (compared ? "matching the party's own Pokémon" : "not cross-checked: no party Pokémon has the visible ID"));
            return (trainerId, await provider.GetRequiredService<PokemonPersistenceService>().GetCaughtPokemonAsync(bot.GameFlavor));
        }

        // The full 32-bit trainer ID (profile->id), 4 bytes at [[FieldSystem + 0x0C] + 0x84]: the visible ID in the low half
        // (observed, HgssPrimoEggEvent.ReadTrainerIdAsync), the secret ID in the high half, which is from the source, not
        // observed. Null when the chain does not read, as at the title screen.
        internal static async Task<uint?> ReadTrainerIdAsync(IMemoryClient memory, HgssPointerService addresses, CancellationToken ct)
        {
            ChainBlockResult read = (await memory.ReadChainBlocksAsync(
                new[] { new ChainBlockRequest(addresses.Anchor, new[] { addresses.SaveDataOffset, addresses.TrainerIdOffset }, 4) }, ct: ct))[0];
            return read.Status == ChainBlockStatus.Ok ? BitConverter.ToUInt32(read.Bytes, 0) : null;
        }

        // The check of the unobserved high half: a party Pokémon whose OT ID has the read ID's visible half is the player's own,
        // so its full OT ID (decoded by the party parser, which shiny detection relies on) must be the read ID. Compared is
        // false when no party Pokémon has the visible half (traded ones and eggs with another OT are skipped); Refusal names
        // both values when one differs.
        internal static (bool Compared, string? Refusal) CrossCheckTrainerId(uint trainerId, PokemonState party)
        {
            var own = party.PartyPokemon.Take(party.PartySize).Select(p => p!.OTId).Where(ot => (ushort)ot == (ushort)trainerId).ToList();
            uint? differing = own.Where(ot => ot != trainerId).Select(ot => (uint?)ot).FirstOrDefault();
            return (own.Count > 0, differing is { } ot
                ? $"The trainer ID read as {trainerId}, but a party Pokémon with the visible ID {(ushort)trainerId} has the full ID {ot}; no headbutt tree can be chosen until the read is fixed."
                : null);
        }

        internal async Task<Dictionary<int, EmulatorState>> StartChainedEncounter(NewEventArguments args, int pos)
        {
            if (args.Legs is null || args.Legs.Count == 0)
            {
                throw new ArgumentException("At least one leg must be supplied for Chained Encounter.");
            }
            if (args.Legs.Any(l => l.Pickup == true && (l.TrainType ?? TrainType.None) == TrainType.None))
            {
                throw new ArgumentException(PickupNeedsTrainMode);
            }
            foreach (var leg in args.Legs)
            {
                RefuseNoHuntYet(slots.Entries[pos].Bot!, leg.PhaseName);
            }
            await RefuseFromFreshPartyAsync(slots.Entries[pos].Bot!, args.Legs.Any(l => l.EncounterType == EncounterType.Headbutt), args.Legs.Any(l => l.Pickup == true));
            // Validate every leg before anything starts -- a bad leg 3 shouldn't be discovered
            // after leg 1 has already been running for an hour.
            var legs = args.Legs.Select(x => ValidateAndBuildLeg(x, slots.Entries[pos].Bot!.GameFileName, slots.Entries[pos].Bot!.GameFlavor)).ToList();

            var chainedEvent = new ChainedOrchestratorEvent(provider, legs);
            // The chain shares one GameState across all legs so the start tile survives leg
            // boundaries; ChainedOrchestratorEvent re-applies each leg's own fields as it goes.
            slots.Entries[pos].GameState = NewGameStateForLeg(pos, legs[0]);
            slots.Entries[pos].GameState!.State.Push(EventState.Init);
            string name = $"Chain ({legs.Count}): {string.Join(" -> ", legs.Select(x => x.PhaseName))}";
            runner.StartEvent(pos, slots.Entries[pos], chainedEvent, slots.Entries[pos].GameState!, name, removeWhenDone: false);

            return GetBotDashboardState();
        }

        internal async Task<Dictionary<int, EmulatorState>> StartTogepiEggAsync(NewEventArguments args, int pos)
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
                Profile = slots.Entries[pos].Bot!.Profile,
                BotService = this,
                Bot = slots.Entries[pos].Bot!,
                Milestone = args.PhaseName,
                // Nothing in this event consults the target -- the egg is the only thing it ever
                // looks at -- but GameState requires one and shiny is what the hunt is for.
                Target = new ShinyTarget(),
            };
            runner.StartEvent(pos, slots.Entries[pos], eggEvent, slots.Entries[pos].GameState!, $"Togepi Egg: {args.PhaseName}", removeWhenDone: false);

            return GetBotDashboardState();
        }

        internal async Task<Dictionary<int, EmulatorState>> StartPrimoEggAsync(NewEventArguments args, int pos)
        {
            if (args.PhaseName is null)
            {
                throw new ArgumentException("PhaseName cannot be null for the Primo egg hunt.");
            }
            await EnsurePartyHasRoomForEggAsync(pos);
            // Loaded once here, because the place check needs the start tile before the event exists.
            PrimoEggConfig config = PrimoEggConfig.Load(PrimoEggConfig.DefaultPath);
            IBot bot = slots.Entries[pos].Bot!;
            if (await HgssPrimoEggEvent.WrongPlaceAsync(bot.Overworld, config, CancellationToken.None) is not null)
            {
                throw new InvalidOperationException(HgssPrimoEggEvent.StandFacingPrimo(config));
            }
            var eggEvent = new HgssPrimoEggEvent(bot, args.PhaseName, config, provider, this);
            slots.Entries[pos].GameState = new GameState()
            {
                Profile = bot.Profile,
                BotService = this,
                Bot = bot,
                Milestone = args.PhaseName,
                // GameState requires a target; the event looks only at the egg.
                Target = new ShinyTarget(),
            };
            runner.StartEvent(pos, slots.Entries[pos], eggEvent, slots.Entries[pos].GameState!, $"Primo Egg: {args.PhaseName}", removeWhenDone: false);

            return GetBotDashboardState();
        }

        private const int SpeciesArceus = 493;
        private const string OffTheStage = "Stand on the Mystri Stage in the Sinjoh Ruins.";
        private const string FieldNotFree = "Close any menu or text first: the player must be free to walk on the stage.";
        private const string NotOneArceus = "The party must be one Arceus and nothing else.";
        private const string NotTheEventArceus = "This Arceus is not the event Arceus or the Hall of Origin one.";
        // MAPSEC_HALL_OF_ORIGIN as a Gen 4 met location; the rule is MonMetadataMatchesEvent's, src/trainer_memo.c.
        private const int MetLocationHallOfOrigin = 86;
        private const string NoPathToTheCircle = "No path from here to the circle. Stand on the stage in front of the circles.";

        // Null when the live game can start the encounter, otherwise the sentence (FR-2). Each check is a fresh read.
        // Run calls it once; the hunt calls it again after every reset.
        internal async Task<string?> StaticEncounterRefusalAsync(IBot bot, StaticEncounterConfig.Entry encounter, CancellationToken ct)
        {
            if (await bot.Overworld.GetMapIdAsync(ct) != encounter.MapId)
            {
                return OffTheStage;
            }
            if ((await bot.State.ReadAsync(ct)).Screen != GameScreen.FieldFree)
            {
                return FieldNotFree;
            }
            PokemonState? party = await ReadPartyAsync(bot);
            if (party is null)
            {
                return PartyUnreadable;
            }
            if (party.PartySize != 1 || party.PartyPokemon[0]!.Species != SpeciesArceus || party.PartyPokemon[0]!.IsEgg)
            {
                return NotOneArceus;
            }
            // A copy, because PK4 decrypts the block it is given in place.
            var arceus = new PKHeX.Core.PK4(party.PartyPokemon[0]!.RawBytes!.ToArray());
            bool hallOfOrigin = arceus.Version is PKHeX.Core.GameVersion.D or PKHeX.Core.GameVersion.P or PKHeX.Core.GameVersion.Pt
                && arceus.MetLocation == MetLocationHallOfOrigin;
            if (!arceus.FatefulEncounter && !hallOfOrigin)
            {
                return NotTheEventArceus;
            }
            return null;
        }

        internal async Task<Dictionary<int, EmulatorState>> StartStaticEncounterAsync(NewEventArguments args, int pos)
        {
            if (args.PhaseName is null)
            {
                throw new ArgumentException("PhaseName cannot be null for the static encounter hunt.");
            }
            StaticEncounterConfig config = StaticEncounterConfig.Load(StaticEncounterConfig.DefaultPath);
            StaticEncounterConfig.Entry encounter = config.Find(args.StaticEncounter);
            IBot bot = slots.Entries[pos].Bot!;
            if (await StaticEncounterRefusalAsync(bot, encounter, CancellationToken.None) is { } refusal)
            {
                throw new InvalidOperationException(refusal);
            }
            ushort map = await bot.Overworld.GetMapIdAsync(CancellationToken.None);
            var (x, y) = await bot.Overworld.GetPositionAsync(CancellationToken.None);
            Direction facing = await bot.Overworld.GetFacingAsync(CancellationToken.None);
            // The run's one search (FR-4): the hunt replays this macro from this tile and facing after every reset.
            string? walkMacro = bot.Profile.Pathfinding!.Pathfind(map, x, y, encounter.Spot[0], encounter.Spot[1], new Capability(), (byte)facing);
            if (walkMacro is null)
            {
                throw new InvalidOperationException(NoPathToTheCircle);
            }
            var staticEvent = new HgssStaticEncounterEvent(bot, config, encounter, walkMacro, x, y, facing, provider, this);
            slots.Entries[pos].GameState = new GameState()
            {
                Profile = bot.Profile,
                BotService = this,
                Bot = bot,
                Milestone = args.PhaseName,
                // GameState requires a target; the event looks only at the encounter.
                Target = new ShinyTarget(),
            };
            runner.StartEvent(pos, slots.Entries[pos], staticEvent, slots.Entries[pos].GameState!, $"Static: {encounter.Name}", removeWhenDone: false);

            return GetBotDashboardState();
        }

        internal async Task<Dictionary<int, EmulatorState>> ResumeAsync(NewEventArguments args, int pos)
        {
            if (slots.Entries[pos].Event is MultiStarterHuntEvent multiHunt)
            {
                // The hunt's main task is still running; Resume is a call into it, not a restart.
                // Awaited under the gate, so a second Resume click settles the next parked shiny
                // rather than the same one, and a fault fails this request.
                var resumeState = new GameState { Profile = profiles.Get(GameFlavor.RS), BotService = this, Target = new ShinyTarget() };
                resumeState.State.Push(EventState.Resume);
                await multiHunt.ExecuteWithResultAsync(resumeState, CancellationToken.None);
                await RefreshDashboardState();
                return GetBotDashboardState();
            }
            slots.Entries[pos].GameState!.SwapState(EventState.Resume);
            runner.StartEvent(pos, slots.Entries[pos], slots.Entries[pos].Event!, slots.Entries[pos].GameState!, slots.Entries[pos].GetName(pos), removeWhenDone: false);
            return GetBotDashboardState();
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
                switchSlot,
                args.Pickup ?? false);
        }

        private GameState NewGameStateForLeg(int pos, EventLeg leg)
        {
            return new GameState()
            {
                OriginalBattleMode = leg.TrainType != TrainType.None ? BattleMode.Battle : BattleMode.Flee,
                Profile = slots.Entries[pos].Bot!.Profile,
                BotService = this,
                Target = leg.Target,
                Milestone = leg.PhaseName,
                Bot = slots.Entries[pos].Bot!,
                TrainType = leg.TrainType,
                Pickup = leg.Pickup,
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
                throw new ArgumentException($"There is no bot at position: {pos}");
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

        // MOVE_HEADBUTT, include/constants/moves.h:33
        private const ushort MoveHeadbutt = 29;
        // ABILITY_PICKUP, include/constants/abilities.h
        private const byte AbilityPickup = 53;

        private const string PartyUnreadable = "The party could not be read; try again once the game is in the field.";
        private const string NoPickupMember = "No Pokémon in your party has the Pickup ability.";
        private const string PickupNeedsTrainMode = "Pickup needs a train mode: the Pokémon must be knocked out, not fled from.";
        internal const string NoHeadbuttTrees = "Ruby and Sapphire have no Headbutt trees.";
        internal const string NoPickup = "Pickup is not available on Ruby and Sapphire.";

        // The choices a profile's capabilities rule out, with the reason the dashboard shows and Run refuses with.
        // Empty for HGSS, so its list and its start are unchanged.
        internal static Dictionary<string, Dictionary<string, string>> DisabledByCapabilities(GameCapabilities c)
        {
            var disabled = new Dictionary<string, Dictionary<string, string>>();
            if (!c.HasHeadbuttTrees)
            {
                disabled["EncounterType"] = new() { [nameof(EncounterType.Headbutt)] = NoHeadbuttTrees };
            }
            if (!c.HasPickup)
            {
                disabled["Pickup"] = new() { ["true"] = NoPickup };
            }
            return disabled;
        }

        /// <summary>
        /// The live party, or null when it could not be read or is empty. Never faults: a failed
        /// read is logged.
        /// </summary>
        private async Task<PokemonState?> ReadPartyAsync(IBot bot)
        {
            PokemonState party;
            try
            {
                party = await provider.GetRequiredService<PokemonStateReader>().GetPartyState(bot);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BotService] Could not read the party for the Run checks: {ex}");
                return null;
            }
            // A game in the field always has a party member; none means the title screen.
            return party.PartySize == 0 ? null : party;
        }

        /// <summary>Null when a party member knows Headbutt, otherwise the reason the dashboard shows.</summary>
        private static string? HeadbuttRefusal(PokemonState? party)
        {
            if (party is null)
            {
                return PartyUnreadable;
            }
            for (int i = 0; i < party.PartySize; i++)
            {
                var pokemon = party.PartyPokemon[i]!;
                if (pokemon.Move1 == MoveHeadbutt || pokemon.Move2 == MoveHeadbutt || pokemon.Move3 == MoveHeadbutt || pokemon.Move4 == MoveHeadbutt)
                {
                    return null;
                }
            }
            return "No Pokémon in your party knows Headbutt.";
        }

        /// <summary>Null when a party member has the Pickup ability, otherwise the reason the dashboard shows.</summary>
        private static string? PickupRefusal(PokemonState? party)
        {
            if (party is null)
            {
                return PartyUnreadable;
            }
            for (int i = 0; i < party.PartySize; i++)
            {
                if (party.PartyPokemon[i]!.Ability == AbilityPickup)
                {
                    return null;
                }
            }
            return NoPickupMember;
        }

        internal const string StandInTheField = "Close any menu or message and stand in the field.";
        internal const string NotTheFirstObject = "The player is not the first object on this map, which the bot watches as it turns.";
        internal const string LeadFainted = "The first Pokémon in the party has fainted.";
        internal const string ShinyLeadLine = "A shiny first Pokémon adds about 1.2 s to every battle.";

        // FR-15's lines at Run. The slow-transition line cannot hold yet: no wild level has been seen in this run.
        internal static IReadOnlyList<string> SpeedLinesAtRun(PokemonData lead) => lead.IsShiny ? [ShinyLeadLine] : [];

        // The live game's refusals for a wild hunt on a profile without training (FR-12), and the lead they read. Each
        // check is a fresh read, and the first failure throws its sentence.
        private async Task<PokemonData> WildRefusalAsync(IBot bot)
        {
            if ((await bot.State.ReadAsync(CancellationToken.None)).Screen != GameScreen.FieldFree)
            {
                throw new InvalidOperationException(StandInTheField);
            }
            if (bot.Overworld is RsOverworldReader rs && await rs.GetPlayerObjectIdAsync(CancellationToken.None) != 0)
            {
                throw new InvalidOperationException(NotTheFirstObject);
            }
            // Not ReadPartyAsync, which folds an empty party into an unreadable one; an empty party has its own sentence.
            PokemonState party;
            try
            {
                party = await provider.GetRequiredService<PokemonStateReader>().GetPartyState(bot);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BotService] Could not read the party for the Run checks: {ex}");
                throw new InvalidOperationException(PartyUnreadable, ex);
            }
            if (party.PartySize == 0 || party.PartyPokemon[0]!.HP == 0)
            {
                throw new InvalidOperationException(LeadFainted);
            }
            return party.PartyPokemon[0]!;
        }

        /// <summary>
        /// Throws the Headbutt reason, then the Pickup reason, for the checks asked for. A fresh read,
        /// not the cached one, which may predate a party change; no read when neither is asked. The party read, never null
        /// once either check passed.
        /// </summary>
        private async Task<PokemonState?> RefuseFromFreshPartyAsync(IBot bot, bool headbutt, bool pickup)
        {
            if (!headbutt && !pickup)
            {
                return null;
            }
            PokemonState? party = await ReadPartyAsync(bot);
            if (headbutt && HeadbuttRefusal(party) is { } headbuttReason)
            {
                throw new ArgumentException(headbuttReason);
            }
            if (pickup && PickupRefusal(party) is { } pickupReason)
            {
                throw new ArgumentException(pickupReason);
            }
            return party;
        }

        // FR-15a: a saved milestone at a location no EncounterType hunts (surf, the rods, rock smash) is refused before anything
        // is read. One with no location is never refused (FR-42).
        private void RefuseNoHuntYet(IBot bot, string? phaseName)
        {
            if (NoHuntYet(Located(bot, phaseName)) is { } refusal)
            {
                throw new ArgumentException(refusal);
            }
        }

        // Decided at B2's plan review: Headbutt hunts a headbutt milestone, and Run and Spin a land one.
        internal static string? EncounterTypeRefusal(Milestone? generated, EncounterType? type) => (generated?.Modality, type) switch
        {
            (Modality.Land, EncounterType.Headbutt) or (Modality.Headbutt, EncounterType.Run or EncounterType.Spin) => $"{generated!.Name} is not hunted with {type}.",
            _ => null,
        };

        // FR-24, checked at Start against the committed map data at the anchor's tile, before any walk: no memory read gives a
        // tile's kind, so a check after the walk would read the same data.
        internal static string? AnchorTileStop(Milestone milestone, Anchor anchor, MapService maps, byte kind)
            => maps.GetMap(anchor.Map)[anchor.Y, anchor.X] == kind ? null : $"{milestone.Name}: the anchor ({anchor.X}, {anchor.Y}) is not {MilestoneFile.Word(milestone)}.";

        // The anchor decides Run or Spin and the range over the form's choice; Headbutt is left as asked. The anchor's ranges
        // passed the leg's range rules where they were made, by the generator or by SetAnchorAsync.
        internal static EncounterParams AnchorLeg(EncounterParams encounter, Anchor anchor) => encounter.type == EncounterType.Headbutt
            ? encounter
            : new EncounterParams(anchor.XRange == 0 && anchor.YRange == 0 ? EncounterType.Spin : EncounterType.Run, anchor.YRange, anchor.XRange);

        private static string HowHunted(EncounterParams encounter) => encounter switch
        {
            { type: EncounterType.Run, xRange: > 0 } => $"run left-right ±{encounter.xRange}",
            { type: EncounterType.Run } => $"run up-down ±{encounter.yRange}",
            { type: EncounterType.Spin } => "spin",
            _ => "headbutt",
        };

        // FR-15 at Start: a saved milestone that is archived, or ticked only for other files, is not hunted on this one; one ticked
        // for no file, or a name that is no saved milestone, is.
        internal static string? OffFileRefusal(PhaseInfo? phase, string gameFileName) => phase switch
        {
            { Archived: true } => $"{phase.Name} is archived.",
            { GameFiles.Count: > 0 } when !phase.GameFiles.Contains(gameFileName) => $"{phase.Name} is not ticked for {gameFileName}.",
            _ => null,
        };

        internal static string? NoHuntYet(Milestone? generated) => generated is { Huntable: false } ? $"{generated.Name}: no hunt yet." : null;

        // FR-15: the milestone picker for the bot's game file, worked out on each request; null when no bot is at the position.
        public async Task<BotMilestones?> GetMilestonesAsync(int pos)
        {
            if (GetBotAt(pos) is not { } bot)
            {
                return null;
            }
            // FR-21: the 4-byte read alone, no party read; a failed read (the title screen) gives no headbutt anchor.
            uint? trainerId = bot.Profile.Capabilities.HasHeadbuttTrees
                ? await ReadTrainerIdAsync(bot, (HgssPointerService)bot.Addresses, CancellationToken.None)
                : null;
            var list = MilestonePicker.List(provider.GetRequiredService<PhaseInfoService>().GetAllPhases([bot.GameFlavor])[bot.GameFlavor],
                profiles.Get(bot.GameFlavor).Milestones, romInfoService.GetByHandle(bot.GameFileName)?.Version,
                await provider.GetRequiredService<MongoGameFileDao>().LoadAsync(bot.GameFileName),
                await provider.GetRequiredService<PokemonPersistenceService>().GetCaughtPokemonAsync(bot.GameFlavor), trainerId);
            return new BotMilestones(bot.GameFileName, list);
        }

        // FR-22: the milestone's anchor on the bot's game file becomes where the bot stands and how it faces, hunted with the
        // form's range under the leg's range rules. Reads only; nothing is pressed.
        public async Task<MilestoneAnchor> SetAnchorAsync(int pos, string milestone, int xRange, int yRange)
        {
            if (GetBotAt(pos) is not { } bot)
            {
                throw new ArgumentException($"There is no bot at position: {pos}");
            }
            if (!bot.Profile.Capabilities.HasOverworldNavigation)
            {
                throw new ArgumentException($"{bot.GameFlavor} cannot walk to an anchor.");
            }
            if (xRange < 0 || yRange < 0)
            {
                throw new ArgumentException($"X and Y range must be non-negative values");
            }
            if (xRange > 0 && yRange > 0)
            {
                throw new ArgumentException($"Only x or y range must be supplied, not both.");
            }
            if ((await bot.State.ReadAsync(CancellationToken.None)).Screen != GameScreen.FieldFree)
            {
                throw new InvalidOperationException(StandInTheField);
            }
            int map = await bot.Overworld.GetMapIdAsync(CancellationToken.None);
            var (x, y) = await bot.Overworld.GetPositionAsync(CancellationToken.None);
            var anchor = new MilestoneAnchor(milestone, new Anchor(map, x, y, xRange, yRange), await bot.Overworld.GetFacingAsync(CancellationToken.None));
            await provider.GetRequiredService<MongoGameFileDao>().SetAnchorAsync(bot.GameFileName, anchor);
            return anchor;
        }

        public async Task ClearAnchorAsync(int pos, string milestone)
        {
            if (GetBotAt(pos) is not { } bot)
            {
                throw new ArgumentException($"There is no bot at position: {pos}");
            }
            await provider.GetRequiredService<MongoGameFileDao>().ClearAnchorAsync(bot.GameFileName, milestone);
        }

        /// <summary>
        /// <see cref="GetAvailableActions"/> with Headbutt and Pickup marked disabled on the encounter
        /// actions when no party member knows Headbutt or has the Pickup ability. Both come from the
        /// cached party read, made once per idle period, or again on the next call while the read
        /// fails: the read is started under the gate and awaited outside it.
        /// </summary>
        public async Task<List<EventArgumentOptions>> GetBotActionsAsync(int pos)
        {
            var actions = GetAvailableActions(pos);
            // Read per listing, as Run reads it, so an edit shows without a restart. A bad file leaves
            // the entry without choices, so its field is a text box, and Run returns the error as a 400.
            int staticIndex = actions.FindIndex(a => a.Name == "HGSS Static Encounter");
            if (staticIndex >= 0)
            {
                try
                {
                    StaticEncounterConfig config = StaticEncounterConfig.Load(StaticEncounterConfig.DefaultPath);
                    actions[staticIndex] = actions[staticIndex] with { Choices = new() { ["StaticEncounter"] = config.Encounters.Select(e => e.Name).ToArray() } };
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException)
                {
                    Console.WriteLine($"[BotService] {StaticEncounterConfig.DefaultPath} could not be read for the action list: {ex}");
                }
            }
            // Only an idle bot is offered these (Wild Encounter needs HasWildEncounters, Chained Encounter
            // HasChainedEncounter), so the entry has a bot. It can still be closed between the two reads.
            if (!actions.Any(a => a.Name is "Wild Encounter" or "Chained Encounter") || !slots.Entries.TryGetValue(pos, out var entry))
            {
                return actions;
            }
            Task<PokemonState?>? read = entry.PartyRead;
            if (read is null)
            {
                await slots.Gate.WaitAsync();
                try
                {
                    read = entry.PartyRead ??= ReadPartyAsync(entry.Bot!);
                }
                finally
                {
                    slots.Gate.Release();
                }
            }
            PokemonState? party = await read;
            if (party is null)
            {
                // Not kept, so the next call reads again: a bot is idle at the title screen after a
                // launch. Only this task is dropped, never a later fill.
                await slots.Gate.WaitAsync();
                try
                {
                    if (entry.PartyRead == read)
                    {
                        entry.PartyRead = null;
                    }
                }
                finally
                {
                    slots.Gate.Release();
                }
            }
            // A capability reason wins over the party's for the same choice.
            var disabled = DisabledByCapabilities(entry.Bot!.Profile.Capabilities);
            if (HeadbuttRefusal(party) is { } headbutt)
            {
                disabled.TryAdd("EncounterType", new());
                disabled["EncounterType"].TryAdd(nameof(EncounterType.Headbutt), headbutt);
            }
            if (PickupRefusal(party) is { } pickup)
            {
                disabled.TryAdd("Pickup", new());
                disabled["Pickup"].TryAdd("true", pickup);
            }
            if (disabled.Count == 0)
            {
                return actions;
            }
            // Copies of the two that change, so the registry's own options are never touched.
            return actions.Select(a => a.Name switch
            {
                "Wild Encounter" => a with { DisabledChoices = disabled },
                "Chained Encounter" => a with { LegParameters = a.LegParameters! with { DisabledChoices = disabled } },
                _ => a,
            }).ToList();
        }

        /// <summary>
        /// <see cref="GetPartyAsync"/> for the dashboard, which also forgets the cached party
        /// read. A separate method because GetPartyAsync is also called under the gate, which is
        /// not re-entrant.
        /// </summary>
        public async Task<List<PartySlotInfo>> ReloadPartyAsync(int pos)
        {
            await slots.Gate.WaitAsync();
            try
            {
                if (slots.Entries.TryGetValue(pos, out var entry))
                {
                    entry.PartyRead = null;
                }
            }
            finally
            {
                slots.Gate.Release();
            }
            return await GetPartyAsync(pos);
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
            IBot? bot = botEvent.Bot;
            if(gameState is not null && gameState.State.TryPeek(out var top) && top == EventState.Waiting
                && (eventTask is null || botEvent.Event is MultiStarterHuntEvent))
            {
                return [EventRegistry.Get("Resume").Options];
            }
            else if(eventTask is null && bot is not null)
            {
                // TODO continue game.
                // A hunt's entry has no bot, and is visible idle while it starts and tears down.
                return EventRegistry.All
                    .Where(e => !e.OwnsEmulators && e.Name != "Resume")
                    .Where(e => e.Requires is not { } need || need.Has(bot.Profile.Capabilities))
                    .Select(e => e.Options)
                    .ToList();
            }
            else
            {
                return [];
            }
        }

        private static void RequireCapability(EventRegistration entry, GameProfile profile)
        {
            if (entry.Requires is { } need && !need.Has(profile.Capabilities))
            {
                throw new ArgumentException($"{entry.Name} needs {need.Capability}, which {profile.Flavor} does not have.");
            }
        }

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
                // A plan's entry names its current step, "Plan <name>: <step>", once the first clock read has picked one.
                string name = x.Value.Event is PlanEvent { StepName.Length: > 0 } plan ? $"{x.Value.GetName(x.Key)}: {plan.StepName}" : x.Value.GetName(x.Key);
                return new KeyValuePair<int, EmulatorState>(x.Key, new EmulatorState(name, top, x.Value.Bot?.GameFlavor ?? gameState?.GameFlavor ?? GameFlavor.HGSS, gameState?.SpeedLines));
            }).ToDictionary();
        }

        public async Task<Dictionary<int, EmulatorState>> CancelBotOrEventAsync(int pos)
        {
            await runner.CancelAsync(pos);
            return GetBotDashboardState();
        }

        public EventArgumentOptions[] GetTopLevelOptions()
        {
            return EventRegistry.All.Where(e => e.OwnsEmulators && e.Name != "HGSS Shiny Starter Hunt").Select(e => e.Options).ToArray();
        }

        public async Task RefreshDashboardState()
        {
            await this.hubContext.Clients.All.ReceiveDashboardState(GetBotDashboardState());
        }
    }
}
