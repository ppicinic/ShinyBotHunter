using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Context;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.BotEvents.Target;
using ShinyBotHunter.Games;
using ShinyBotHunter.Macro;
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
                throw new ArgumentException($"There is not bot at position: {pos}");
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
            return await entry.Create(this, args, pos);
        }

        internal async Task<Dictionary<int, EmulatorState>> StartWildEncounter(NewEventArguments args, int pos)
        {
            // A fresh read, not the cached answer, which may predate a party change.
            if (args.EncounterType == EncounterType.Headbutt && await HeadbuttRefusalAsync(slots.Entries[pos].Bot!) is { } reason)
            {
                throw new ArgumentException(reason);
            }
            var leg = ValidateAndBuildLeg(args.ToLeg(), slots.Entries[pos].Bot!.GameFileName, slots.Entries[pos].Bot!.GameFlavor);

            var wildPokemonEvent = new OrchestratorEvent(provider, leg.EncounterParams);
            slots.Entries[pos].GameState = NewGameStateForLeg(pos, leg);
            slots.Entries[pos].GameState!.State.Push(EventState.Init);
            runner.StartEvent(pos, slots.Entries[pos], wildPokemonEvent, slots.Entries[pos].GameState!, leg.PhaseName, removeWhenDone: false);

            return GetBotDashboardState();
        }

        internal async Task<Dictionary<int, EmulatorState>> StartChainedEncounter(NewEventArguments args, int pos)
        {
            if (args.Legs is null || args.Legs.Count == 0)
            {
                throw new ArgumentException("At least one leg must be supplied for Chained Encounter.");
            }
            // A fresh read, not the cached answer, which may predate a party change.
            if (args.Legs.Any(l => l.EncounterType == EncounterType.Headbutt) && await HeadbuttRefusalAsync(slots.Entries[pos].Bot!) is { } reason)
            {
                throw new ArgumentException(reason);
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
                switchSlot);
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

        // MOVE_HEADBUTT, include/constants/moves.h:33
        private const ushort MoveHeadbutt = 29;

        private const string PartyUnreadable = "The party could not be read; try again once the game is in the field.";

        /// <summary>
        /// Null when a party member knows Headbutt, otherwise the reason the dashboard shows. Never
        /// faults: a failed read is logged and becomes <see cref="PartyUnreadable"/>.
        /// </summary>
        private async Task<string?> HeadbuttRefusalAsync(IBot bot)
        {
            PokemonState party;
            try
            {
                party = await provider.GetRequiredService<PokemonStateReader>().GetPartyState(bot);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BotService] Could not read the party to check for Headbutt: {ex}");
                return PartyUnreadable;
            }
            // A game in the field always has a party member; none means the title screen.
            if (party.PartySize == 0)
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

        /// <summary>
        /// <see cref="GetAvailableActions"/> with Headbutt marked disabled on the encounter actions
        /// when no party member knows it. The party is read once per idle period, or again on the next
        /// call while the read fails: the read is started under the gate and awaited outside it.
        /// </summary>
        public async Task<List<EventArgumentOptions>> GetBotActionsAsync(int pos)
        {
            var actions = GetAvailableActions(pos);
            // Only an idle bot with HasWildEncounters is offered these, so the entry has a bot. It
            // can still be closed between the two reads.
            if (!actions.Any(a => a.Name is "Wild Encounter" or "Chained Encounter") || !slots.Entries.TryGetValue(pos, out var entry))
            {
                return actions;
            }
            Task<string?>? refusal = entry.HeadbuttRefusal;
            if (refusal is null)
            {
                await slots.Gate.WaitAsync();
                try
                {
                    refusal = entry.HeadbuttRefusal ??= HeadbuttRefusalAsync(entry.Bot!);
                }
                finally
                {
                    slots.Gate.Release();
                }
            }
            string? reason = await refusal;
            if (reason == PartyUnreadable)
            {
                // Not kept, so the next call reads again: a bot is idle at the title screen after a
                // launch. Only this task is dropped, never a later fill.
                await slots.Gate.WaitAsync();
                try
                {
                    if (entry.HeadbuttRefusal == refusal)
                    {
                        entry.HeadbuttRefusal = null;
                    }
                }
                finally
                {
                    slots.Gate.Release();
                }
            }
            if (reason is null)
            {
                return actions;
            }
            var disabled = new Dictionary<string, Dictionary<string, string>>
            {
                ["EncounterType"] = new() { [nameof(EncounterType.Headbutt)] = reason },
            };
            // Copies of the two that change, so the registry's own options are never touched.
            return actions.Select(a => a.Name switch
            {
                "Wild Encounter" => a with { DisabledChoices = disabled },
                "Chained Encounter" => a with { LegParameters = a.LegParameters! with { DisabledChoices = disabled } },
                _ => a,
            }).ToList();
        }

        /// <summary>
        /// <see cref="GetPartyAsync"/> for the dashboard, which also forgets the cached Headbutt
        /// answer. A separate method because GetPartyAsync is also called under the gate, which is
        /// not re-entrant.
        /// </summary>
        public async Task<List<PartySlotInfo>> ReloadPartyAsync(int pos)
        {
            await slots.Gate.WaitAsync();
            try
            {
                if (slots.Entries.TryGetValue(pos, out var entry))
                {
                    entry.HeadbuttRefusal = null;
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
                return new KeyValuePair<int, EmulatorState>(x.Key, new EmulatorState(x.Value.GetName(x.Key), top, x.Value.Bot?.GameFlavor ?? gameState?.GameFlavor ?? GameFlavor.HGSS));
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
