using ShinyBotHunter.Emulators;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Scripts.Maps;
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

        /// <summary>Address table for a ROM revision. Throws NotSupportedException for a revision without one.</summary>
        public Func<int, IPointerService> Addresses { get; }

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

        /// <param name="revisions">The revisions <paramref name="addresses"/> has a table for. Only the box check reads it.</param>
        public GameProfile(
            GameFlavor flavor,
            GameCapabilities capabilities,
            IReadOnlyList<int> revisions,
            Func<int, IPointerService> addresses,
            IPokemonParser parser,
            IBattlePokemonParser? battleParser,
            Func<IMemoryClient, IPointerService, IOverworldReader>? createOverworldReader = null,
            MapService? maps = null,
            GraphService? graph = null,
            PathfindingService? pathfinding = null,
            Func<IMemoryClient, IPointerService, IGameStateReader>? createStateReader = null)
        {
            if (capabilities.HasOverworldNavigation && createOverworldReader is null)
            {
                throw new ArgumentException($"{flavor} declares HasOverworldNavigation but has no overworld reader.");
            }
            if (capabilities.HasOverworldNavigation && maps is null)
            {
                throw new ArgumentException($"{flavor} declares HasOverworldNavigation but has no maps.");
            }
            if (capabilities.HasOverworldNavigation && graph is null)
            {
                throw new ArgumentException($"{flavor} declares HasOverworldNavigation but has no graph.");
            }
            if (capabilities.HasOverworldNavigation && pathfinding is null)
            {
                throw new ArgumentException($"{flavor} declares HasOverworldNavigation but has no pathfinding.");
            }
            if (capabilities.HasWildEncounters && battleParser is null)
            {
                throw new ArgumentException($"{flavor} declares HasWildEncounters but has no battle parser.");
            }
            if (capabilities.HasWildEncounters && createStateReader is null)
            {
                throw new ArgumentException($"{flavor} declares HasWildEncounters but has no state reader.");
            }
            if (capabilities.HasWildEncounters && !capabilities.HasOverworldNavigation)
            {
                throw new ArgumentException($"{flavor} declares HasWildEncounters but not HasOverworldNavigation.");
            }
            if (capabilities.CanReadBoxes)
            {
                foreach (int revision in revisions)
                {
                    if (addresses(revision).BoxLayout is null)
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
        }
    }
}
