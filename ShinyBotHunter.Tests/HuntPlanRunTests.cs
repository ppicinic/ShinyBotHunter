using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.BotEvents.Target;
using ShinyBotHunter.Controllers;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Games.Rs;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    // The saved plan in a Wild Encounter (FR-32 to FR-36, FR-37a, FR-40, FR-42): SoulSilver's Route 32 grass by day and Route 30
    // grass by night, from the committed milestone file. In the collection because MapService opens maps.bin without read sharing.
    [Collection("macro-registry")]
    public class HuntPlanRunTests
    {
        private static readonly string DataDirectory = Path.Combine(RepoPaths.AppProject, "Data", "HGSS");
        private static readonly HgssPointerService Table = new();
        // Where the field system points; any RAM value works.
        private const uint FieldSystem = 0x02300000;
        private const string PlanName = "Night on 30";
        private const string Route32 = "36-land", Route30 = "34-land";

        private static Milestone Find(RomVersion version, string id) => MilestoneFile.Load(DataDirectory)[version].Single(m => m.Id == id);

        private static HuntPlan NightOn30() => new(GameFlavor.HGSS, PlanName, Route32, [new PlanRule([SlotCondition.Night], [], Route30)]);

        // Each stop as Start resolves it, with its own target.
        private static HuntPlanRun Run()
        {
            var stops = new Dictionary<string, (EventLeg Leg, MilestoneAnchor Anchor, string Name)>();
            foreach (string id in new[] { Route32, Route30 })
            {
                Milestone milestone = Find(RomVersion.SoulSilver, id);
                Anchor anchor = milestone.Anchor!;
                EventLeg leg = new(new ShinyTarget(), id, new EncounterParams(EncounterType.Run, anchor.YRange, anchor.XRange), TrainType.None, null, null);
                stops[id] = (leg, new MilestoneAnchor(id, anchor, Direction.None), milestone.Name);
            }
            return new HuntPlanRun(NightOn30(), stops);
        }

        // Arrived on Route 32 with no move owed, as after the first commit.
        private static HuntPlanRun OnRoute32()
        {
            HuntPlanRun run = Run();
            (run.Arrived, run.Pending) = (true, null);
            return run;
        }

        private static void Stand(FakeMemoryClient memory, int map, int x, int y)
        {
            memory.Place(Table.Anchor, BitConverter.GetBytes(FieldSystem));
            memory.Place((uint)(FieldSystem + Table.MapIdOffset), BitConverter.GetBytes((ushort)map));
            memory.Place((uint)(FieldSystem + Table.FacingOffset), [(byte)Direction.Down]);
            memory.Place(Table.TrainerX, BitConverter.GetBytes((uint)x << 16));
            memory.Place(Table.TrainerY, BitConverter.GetBytes((uint)y << 16));
        }

        // A bot standing on the tile, its overworld reader the real one over placed memory.
        private static FakeBot StandingAt(int map, int x, int y)
        {
            FakeMemoryClient memory = new();
            Stand(memory, map, x, y);
            return new FakeBot(Task.CompletedTask) { Overworld = new HgssOverworldReader(memory, Table) };
        }

        // Thursday at the hour, hunting the milestone with the run's own target; no overworld reader unless the test gives one.
        private static GameState StateAt(FakeBot bot, HuntPlanRun run, string milestone, int hour)
        {
            GameState state = new()
            {
                Bot = bot,
                Profile = bot.Profile,
                Milestone = milestone,
                Target = run.Stops[milestone].Leg.Target,
                RatesSeen = new RateInputs(4, hour, null, null, 0, 10, false),
            };
            state.State.Push(EventState.DecideOverworldAction);
            return state;
        }

        private sealed record Calls(List<string> Follows, List<string> Updates);

        private static Task<bool> StepAsync(HuntPlanRun run, GameState state, Calls calls) => run.StepAsync(state,
            m => { calls.Follows.Add(m); return Task.CompletedTask; }, s => { calls.Updates.Add(s.Milestone); return Task.CompletedTask; }, CancellationToken.None);

        // A party block for the decision: level, HP, ability, and 1 for an egg.
        private sealed class RateFieldsParser : IPokemonParser
        {
            public PokemonData? Parse(byte[] raw) => new PokemonData { Level = raw[0], HP = raw[1], Ability = raw[2], IVs = raw[3] == 1 ? 1u << 30 : 0 };
        }

        // HGSS with only what the decision uses: Thursday 21:00 on the live clock, one healthy lead, and no overworld reader.
        private static (FakeBot Bot, FakeMemoryClient Memory) HgssDecisionBot()
        {
            FakeMemoryClient memory = new();
            memory.Place(Table.LiveClock + 0x0C, BitConverter.GetBytes(4u));
            memory.Place(Table.LiveClock + 0x10, BitConverter.GetBytes(21u));
            GameProfile hgss = new(GameFlavor.HGSS,
                new GameCapabilities(HasOverworldNavigation: false, HasWildEncounters: false, HasTraining: false, HasEggs: false,
                    CanReadBoxes: true, HasSoftResetStarterHunt: false, HasShinyStarterHunt: false, HasWishmakerJirachi: false),
                [0], (_, _) => Table, new RateFieldsParser(), battleParser: null);
            FakeBot bot = new(Task.CompletedTask, hgss) { PartyBlocks = [[12, 20, 51, 0]], State = new HgssFieldSystemStateReader(memory, Table) };
            return (bot, memory);
        }

        private static GameState DecisionState(FakeBot bot)
        {
            GameState state = new() { Bot = bot, Profile = bot.Profile, Target = new ShinyTarget(), Milestone = Route32 };
            state.State.Push(EventState.DecideOverworldAction);
            return state;
        }

        private static Task DecideAsync(GameState state, Func<GameState, Task<bool>>? followPlan = null) => OrchestratorEvent.DecideOverworldAction(state,
            new PokemonStateReader(), _ => throw new NotImplementedException(), null!, CancellationToken.None, followPlan);

        // FR-42 and its binding note: with no plan the decision is today's, one chain read beside the party read and no
        // overworld read (the bot has no reader, so one would throw).
        [Fact]
        public async Task Decide_NoPlan_IsTodaysDecision()
        {
            (FakeBot bot, FakeMemoryClient memory) = HgssDecisionBot();
            GameState state = DecisionState(bot);

            await DecideAsync(state);

            Assert.Single(memory.ChainReads);
            Assert.Equal(1, bot.PartyReads);
            Assert.Equal(new RateInputs(4, 21, null, null, 51, 12, false), state.RatesSeen);
            Assert.Equal([EventState.Encountering], state.State);
        }

        // The step runs after the clock read; true leaves the stack as the step left it, false goes on to the encounter.
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Decide_WithPlan_StepSeesTheClockAndCanTakeTheStack(bool took)
        {
            (FakeBot bot, _) = HgssDecisionBot();
            GameState state = DecisionState(bot);
            RateInputs? seen = null;

            await DecideAsync(state, s =>
            {
                seen = s.RatesSeen;
                if (took)
                {
                    s.SwapState(EventState.ReturnToStart);
                }
                return Task.FromResult(took);
            });

            Assert.Equal(new RateInputs(4, 21, null, null, 51, 12, false), seen);
            Assert.Equal([took ? EventState.ReturnToStart : EventState.Encountering], state.State);
        }

        [Fact]
        public async Task Step_SameMilestone_NoMove_ReadsNothing()
        {
            HuntPlanRun run = OnRoute32();
            GameState state = StateAt(new FakeBot(Task.CompletedTask), run, Route32, 12);
            Calls calls = new([], []);

            Assert.False(await StepAsync(run, state, calls));

            Assert.Empty(calls.Follows);
            Assert.Empty(calls.Updates);
            Assert.Equal([EventState.DecideOverworldAction], state.State);
        }

        // Binding answer 6: Start cannot read the clock, so the first decision makes the pick the milestone before any walk.
        [Fact]
        public async Task Step_FirstDecision_PickBecomesTheMilestone_ThenWalks()
        {
            HuntPlanRun run = Run();
            GameState state = StateAt(StandingAt(36, 450, 330), run, Route32, 21);
            Calls calls = new([], []);

            Assert.True(await StepAsync(run, state, calls));

            Assert.Equal(Route30, state.Milestone);
            Assert.Same(run.Stops[Route30].Leg.Target, state.Target);
            Assert.Equal([Route30], calls.Follows);
            Assert.Equal([Route30], calls.Updates);
            Assert.Equal([EventState.ReturnToStart], state.State);
            Assert.Equal((34, 547, 304), (state.StartMapId, state.StartX, state.StartY));
            Assert.Equal(BattleMode.Flee, state.BattleMode);
            Assert.Equal(Route30, run.Pending);
        }

        // FR-33 and FR-33a: no move while the rule does not match, and a walk at the first decision after it does.
        [Fact]
        public async Task Step_RuleStartsMatching_WalksToTheAnchor()
        {
            HuntPlanRun run = OnRoute32();
            FakeBot bot = new(Task.CompletedTask);
            GameState state = StateAt(bot, run, Route32, 19);
            Calls calls = new([], []);

            Assert.False(await StepAsync(run, state, calls));
            FakeMemoryClient memory = new();
            Stand(memory, 36, 458, 339);
            bot.Overworld = new HgssOverworldReader(memory, Table);
            state.RatesSeen = state.RatesSeen! with { Hour = 20 };
            Assert.True(await StepAsync(run, state, calls));

            Assert.Equal([EventState.ReturnToStart], state.State);
            Assert.Equal(Route30, run.Pending);
            Assert.Equal(Route32, state.Milestone);
            Assert.Empty(calls.Follows);
        }

        // FR-36: an encounter on the walk is logged under the milestone being left.
        [Fact]
        public async Task Step_EncounterOnTheWalk_LoggedUnderTheMilestoneLeft()
        {
            HuntPlanRun run = OnRoute32();
            GameState state = StateAt(StandingAt(36, 458, 339), run, Route32, 21);
            await StepAsync(run, state, new Calls([], []));
            state.State.Push(EventState.BattleStart);
            List<PersistPokemonData> logged = [];

            await OrchestratorEvent.LogEncounterAndAskTargetAsync(state, new PokemonData { Level = 5, PID = 0x12345678 },
                p => { logged.Add(p); return Task.CompletedTask; }, _ => Task.CompletedTask);

            Assert.Equal(Route32, Assert.Single(logged).Phase);
        }

        // FR-36 and FR-40: on the new anchor the hunt commits, follows the card once, and drops the Run line paced on Route 32.
        [Fact]
        public async Task Step_OnTheNewAnchor_Commits()
        {
            HuntPlanRun run = OnRoute32();
            run.Pending = Route30;
            GameState state = StateAt(StandingAt(34, 547, 304), run, Route32, 21);
            state.EncounterMacros["Run"] = "the Route 32 line";
            Calls calls = new([], []);

            Assert.False(await StepAsync(run, state, calls));

            Assert.Equal(Route30, state.Milestone);
            Assert.Same(run.Stops[Route30].Leg.Target, state.Target);
            Assert.Null(run.Pending);
            Assert.DoesNotContain("Run", state.EncounterMacros.Keys);
            Assert.Equal([Route30], calls.Follows);
            Assert.Equal([Route30], calls.Updates);
            Assert.Equal([EventState.DecideOverworldAction], state.State);
        }

        // The navigator gave up on the route and still reported arrival: the hunt parks with where the player stands.
        [Fact]
        public async Task Step_WalkEndedOffTheAnchor_ParksWithTheSentence()
        {
            HuntPlanRun run = OnRoute32();
            (run.Pending, run.WalkEnded) = (Route30, true);
            GameState state = StateAt(StandingAt(34, 540, 300), run, Route32, 21);
            TextWriter console = Console.Out;
            StringWriter log = new();
            Console.SetOut(log);
            try
            {
                Assert.True(await StepAsync(run, state, new Calls([], [])));
            }
            finally
            {
                Console.SetOut(console);
            }

            Assert.Equal([EventState.Waiting], state.State);
            Assert.Contains("[Orchestrator] Route 30 · grass: the walk ended at map 34 (540, 300), not on the anchor (547, 304).", log.ToString());
            Assert.False(run.WalkEnded);
        }

        // A Resume after a park mid-walk (the Repel message's interim path) walks again, planned from where the player stands.
        [Fact]
        public async Task Step_ResumedMidWalk_WalksAgain()
        {
            HuntPlanRun run = OnRoute32();
            run.Pending = Route30;
            GameState state = StateAt(StandingAt(34, 560, 310), run, Route32, 21);
            state.NavigationPath = [];
            state.NavigationPathIndex = 3;

            Assert.True(await StepAsync(run, state, new Calls([], [])));

            Assert.Equal([EventState.ReturnToStart], state.State);
            Assert.Equal((34, 547, 304), (state.StartMapId, state.StartX, state.StartY));
            Assert.Null(state.NavigationPath);
            Assert.Equal(0, state.NavigationPathIndex);
        }

        // FR-33a: the rule stopped matching before the walk arrived, so the hunt walks back to the milestone it never left.
        [Fact]
        public async Task Step_RuleStopsMatchingMidWalk_TurnsBack()
        {
            HuntPlanRun run = OnRoute32();
            run.Pending = Route30;
            GameState state = StateAt(StandingAt(34, 560, 310), run, Route32, 10);

            Assert.True(await StepAsync(run, state, new Calls([], [])));

            Assert.Equal(Route32, run.Pending);
            Assert.Equal(Route32, state.Milestone);
            Assert.Equal((36, 458, 339), (state.StartMapId, state.StartX, state.StartY));
            Assert.Equal([EventState.ReturnToStart], state.State);
        }

        // FR-34: a complete milestone's rule is skipped, and with every milestone complete the decision ends the hunt.
        [Fact]
        public async Task Step_CompleteSkipped_AllCompleteEndsTheHunt()
        {
            HuntPlanRun skipping = OnRoute32();
            skipping.Complete = new HashSet<string> { Route30 };
            GameState night = StateAt(new FakeBot(Task.CompletedTask), skipping, Route32, 21);

            Assert.False(await StepAsync(skipping, night, new Calls([], [])));
            Assert.Equal(Route32, night.Milestone);
            Assert.Equal([EventState.DecideOverworldAction], night.State);

            HuntPlanRun done = OnRoute32();
            done.Complete = new HashSet<string> { Route30, Route32 };
            (FakeBot bot, _) = HgssDecisionBot();
            GameState state = DecisionState(bot);

            await DecideAsync(state, s => StepAsync(done, s, new Calls([], [])));

            Assert.True(state.MilestoneComplete);
            Assert.Equal([EventState.Complete], state.State);
        }

        private static PathfindingService Pathfinding()
            => new(new MapService(DataDirectory), new GraphService(DataDirectory), Table.TrainerX, Table.TrainerY, Table.Anchor, Table.MapIdOffset);

        private static (string Name, MilestoneAnchor? Anchor) Stop(string id)
        {
            Milestone milestone = Find(RomVersion.SoulSilver, id);
            return (milestone.Name, new GameFile().AnchorFor(id, milestone));
        }

        private static readonly (int Map, int X, int Y, Direction Facing) OnRoute32sAnchor = (36, 458, 339, Direction.Down);

        // FR-35 and FR-19: Sprout Tower is one of the 19 land milestones with no generated anchor.
        [Fact]
        public async Task Reach_AnchorlessMilestone_Refused()
        {
            string? refusal = await HuntPlanRun.ReachRefusalAsync(PlanName, Pathfinding(), OnRoute32sAnchor, [Stop(Route32), Stop("155-land")]);

            Assert.Equal("Night on 30: Sprout Tower · cave has no anchor.", refusal);
        }

        // FR-35: Whirl Islands is behind Surf, which the navigator's walk does not use.
        [Fact]
        public async Task Reach_UnreachableAnchor_Refused()
        {
            string? refusal = await HuntPlanRun.ReachRefusalAsync(PlanName, Pathfinding(), OnRoute32sAnchor, [Stop(Route32), Stop("121-land")]);

            Assert.Equal("Night on 30: Whirl Islands 1F, B1F and B2F · cave's anchor (52, 46) cannot be reached from here without an HM.", refusal);
        }

        [Fact]
        public async Task Reach_Route32AndRoute30_Pass()
        {
            Assert.Null(await HuntPlanRun.ReachRefusalAsync(PlanName, Pathfinding(), OnRoute32sAnchor, [Stop(Route32), Stop(Route30)]));
        }

        // FR-37a: one saved plan resolves on a HeartGold file and on a SoulSilver file alike.
        [Theory]
        [InlineData(RomVersion.HeartGold)]
        [InlineData(RomVersion.SoulSilver)]
        public async Task Plan_OneSavedPlan_TwoGameFiles(RomVersion version)
        {
            HuntPlan plan = NightOn30();
            var milestones = MilestoneFile.Load(DataDirectory);
            var stops = plan.Milestones.Select(id =>
            {
                Milestone? milestone = PhaseTarget.FindMilestone(milestones, version, id);
                return (milestone!.Name, new GameFile { Id = $"{version}.nds" }.AnchorFor(id, milestone));
            }).ToList();

            Assert.All(stops, s => Assert.NotNull(s.Item2));
            Assert.Null(await HuntPlanRun.ReachRefusalAsync(plan.Name, Pathfinding(), OnRoute32sAnchor, stops));
        }

        // A plan needs the in-game clock, which RS has none of: refused before any read, with no Mongo in the provider.
        [Fact]
        public async Task StartWithAPlan_OnRs_Is400WithTheClockSentence()
        {
            GameProfile rs = RsProfile.Create(new PokemonMetadataService(null!), new MapService(new Dictionary<int, Map>()));
            var settings = Options.Create(new ApplicationSettings { MaxEmulators = new() { ["DS"] = 2 } });
            var factory = new FakeBotFactory(settings, Task.CompletedTask, rs);
            var provider = new ServiceCollection().AddSingleton<PokemonStateReader>().BuildServiceProvider();
            var service = new BotService(settings, factory, provider, null!, new RomInfoService(settings, NullLogger<RomInfoService>.Instance), new GameProfiles(rs));
            await service.ExecuteNewBotOrSpecialEvent(new NewEventArguments { EventName = "Launch Bot", GameName = "fake.nds" });
            var args = new NewEventArguments
            {
                EventName = "Wild Encounter", PhaseName = "x", TargetType = "ShinyTarget", EncounterType = EncounterType.Run, HuntPlan = PlanName,
            };

            var result = await new BotController(service).StartAction(0, args);

            var content = Assert.IsType<ContentResult>(result.Result);
            Assert.Equal(StatusCodes.Status400BadRequest, content.StatusCode);
            Assert.Equal("Hunt plans need the in-game clock; RS has none.", content.Content);
            Assert.Equal(0, factory.Bots[0].PartyReads);
        }
    }
}
