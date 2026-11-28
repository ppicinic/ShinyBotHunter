using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.BattlePokemonParser;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Games
{
    /// <summary>
    /// Names the parts one flavor uses. It composes and does no work; the only code is the
    /// constructor's check that every declared capability has its part present.
    /// </summary>
    public sealed record GameProfile
    {
        public GameFlavor Flavor { get; }

        public GameCapabilities Capabilities { get; }

        /// <summary>
        /// Address table for a ROM's game code and revision. Throws NotSupportedException for a revision without one. A null
        /// game code gives the RAM-only table, with no per-game part.
        /// </summary>
        public Func<string?, int, IPointerService> Addresses { get; }

        public IPokemonParser Parser { get; }

        /// <summary>Null when the flavor has no battle reads.</summary>
        public IBattlePokemonParser? BattleParser { get; }

        /// <summary>Null when the flavor has no overworld reads.</summary>
        public Func<IMemoryClient, IPointerService, IOverworldReader>? CreateOverworldReader { get; }

        public MapService? Maps { get; }

        public GraphService? Graph { get; }

        public PathfindingService? Pathfinding { get; }

        /// <summary>Null when the flavor has no state reads.</summary>
        public Func<IMemoryClient, IPointerService, IGameStateReader>? CreateStateReader { get; }

        /// <summary>The battle a wild encounter hands to, made once per orchestrator. Null when the flavor has no wild encounters.</summary>
        public Func<IServiceProvider, IBotEvent<GameState>>? CreateBattleEvent { get; }

        /// <summary>Saves the game after a catch; done when the save is. Null when the flavor has no wild encounters.</summary>
        public Func<IServiceProvider, IBot, Task>? SaveGame { get; }

        /// <summary>How long a seek lets the field stay locked before it hands the lock to the battle event, which reads
        /// what locked it. Null when the seek waits for the battle screen alone.</summary>
        public TimeSpan? SeekLockHandOff { get; }

        /// <summary>What walks to a Pokémon Center, heals and walks back to the start tile, made once per orchestrator at its
        /// first navigation state. Null when the flavor has no overworld navigation.</summary>
        public Func<IServiceProvider, IBotEvent<GameState>>? CreateNavigatorEvent { get; }

        /// <summary>Why a ROM's address table cannot train, or null when it can. Null when every table of the flavor can.</summary>
        public Func<IPointerService, string?>? TrainingRefusal { get; }

        /// <summary>The species shares and survive share of a milestone: (milestone, time of day or for headbutt the tree kind,
        /// Repel level or null, lead ability, lead level, lead is egg). Null when the flavor has no milestones.</summary>
        public Func<Milestone, SlotCondition, int?, byte, int, bool, (IReadOnlyDictionary<int, double> Shares, double Survive)>? Rates { get; }

        /// <summary>Each version's generated milestones, from the flavor's milestone file. Null when the flavor has none.</summary>
        public IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>>? Milestones { get; }

        /// <summary>The map data's tile kind of a land milestone's encounter tiles, which its anchor must stand on (FR-24).
        /// Null when the flavor has no milestones.</summary>
        public Func<Milestone, byte>? LandTileKind { get; }

        /// <param name="revisions">The revisions <paramref name="addresses"/> has a table for. Only the box check reads it.</param>
        public GameProfile(
            GameFlavor flavor,
            GameCapabilities capabilities,
            IReadOnlyList<int> revisions,
            Func<string?, int, IPointerService> addresses,
            IPokemonParser parser,
            IBattlePokemonParser? battleParser,
            Func<IMemoryClient, IPointerService, IOverworldReader>? createOverworldReader = null,
            MapService? maps = null,
            GraphService? graph = null,
            PathfindingService? pathfinding = null,
            Func<IMemoryClient, IPointerService, IGameStateReader>? createStateReader = null,
            Func<IServiceProvider, IBotEvent<GameState>>? createBattleEvent = null,
            Func<IServiceProvider, IBot, Task>? saveGame = null,
            TimeSpan? seekLockHandOff = null,
            Func<IServiceProvider, IBotEvent<GameState>>? createNavigatorEvent = null,
            Func<IPointerService, string?>? trainingRefusal = null,
            Func<Milestone, SlotCondition, int?, byte, int, bool, (IReadOnlyDictionary<int, double> Shares, double Survive)>? rates = null,
            IReadOnlyDictionary<RomVersion, IReadOnlyList<Milestone>>? milestones = null,
            Func<Milestone, byte>? landTileKind = null)
        {
            if (capabilities.HasOverworldNavigation && createOverworldReader is null)
            {
                throw new ArgumentException($"{flavor} declares HasOverworldNavigation but has no overworld reader.");
            }
            if (capabilities.HasOverworldNavigation && maps is null)
            {
                throw new ArgumentException($"{flavor} declares HasOverworldNavigation but has no maps.");
            }
            // The navigator part is what reads the graph or the maps; RS's plans over the maps alone (RouteSearch).
            if (capabilities.HasOverworldNavigation && createNavigatorEvent is null)
            {
                throw new ArgumentException($"{flavor} declares HasOverworldNavigation but has no navigator event.");
            }
            // The static start walks to the spot with Pathfinding itself, outside any navigator part.
            if (capabilities.HasStaticEncounter && graph is null)
            {
                throw new ArgumentException($"{flavor} declares HasStaticEncounter but has no graph.");
            }
            if (capabilities.HasStaticEncounter && pathfinding is null)
            {
                throw new ArgumentException($"{flavor} declares HasStaticEncounter but has no pathfinding.");
            }
            if (capabilities.HasWildEncounters && createStateReader is null)
            {
                throw new ArgumentException($"{flavor} declares HasWildEncounters but has no state reader.");
            }
            // The orchestrator's Init reads the start tile, and its loop runs the profile's battle and save.
            if (capabilities.HasWildEncounters && createOverworldReader is null)
            {
                throw new ArgumentException($"{flavor} declares HasWildEncounters but has no overworld reader.");
            }
            if (capabilities.HasWildEncounters && createBattleEvent is null)
            {
                throw new ArgumentException($"{flavor} declares HasWildEncounters but has no battle event.");
            }
            if (capabilities.HasWildEncounters && saveGame is null)
            {
                throw new ArgumentException($"{flavor} declares HasWildEncounters but has no save.");
            }
            // No battle parser rule: training's decisions read the party record, which carries HP and PP through the battle
            // (docs/research/rs-navigation-and-training.md, R8).
            if (capabilities.HasTraining && !capabilities.HasOverworldNavigation)
            {
                throw new ArgumentException($"{flavor} declares HasTraining but not HasOverworldNavigation.");
            }
            // A chained leg may train, and the chained start greys no choice by capability, so a training leg must find the
            // navigator part that HasTraining brings.
            if (capabilities.HasChainedEncounter && !capabilities.HasTraining)
            {
                throw new ArgumentException($"{flavor} declares HasChainedEncounter but not HasTraining.");
            }
            if (capabilities.CanReadBoxes)
            {
                foreach (int revision in revisions)
                {
                    // A null game code means the RAM-only table, with no per-game part; the box layout is in RAM.
                    if (addresses(null, revision).BoxLayout is null)
                    {
                        throw new ArgumentException($"{flavor} declares CanReadBoxes but revision {revision} has no box layout.");
                    }
                }
            }
            Flavor = flavor;
            Capabilities = capabilities;
            Addresses = addresses;
            Parser = parser;
            BattleParser = battleParser;
            CreateOverworldReader = createOverworldReader;
            Maps = maps;
            Graph = graph;
            Pathfinding = pathfinding;
            CreateStateReader = createStateReader;
            CreateBattleEvent = createBattleEvent;
            SaveGame = saveGame;
            SeekLockHandOff = seekLockHandOff;
            CreateNavigatorEvent = createNavigatorEvent;
            TrainingRefusal = trainingRefusal;
            Rates = rates;
            Milestones = milestones;
            LandTileKind = landTileKind;
        }
    }
}
