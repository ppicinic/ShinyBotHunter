using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents.Target;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.BotEvents
{
    public enum BattleMode
    {
        Flee,
        Battle,
    }
    public class GameState
    {
        public ITarget Target { get; set; } = null!;
        public IBot Bot { get; set; } = null!;
        public BattleMode BattleMode { get; set; }
        public BattleMode OriginalBattleMode { get; set; }
        public Stack<EventState> State { get; private set; } = new Stack<EventState>();
        public GameFlavor GameFlavor { get; set; } = GameFlavor.HGSS;
        public string Milestone { get; set; } = null!;

        public PokemonBattleState? BattleState { get; set; } = null;
        public List<int> BattlePositions { get; set; } = [];
        public PersistPokemonData? TargetPokemon { get; set; } = null;
        public Dictionary<string, string> EncounterMacros { get; set; } = new Dictionary<string, string>();
        public Dictionary<(uint, ushort), EvolutionMetadata> PendingEvolutions { get; set; } = new Dictionary<(uint, ushort), EvolutionMetadata>();
        public int StartY { get; set; } = -1;
        public int StartMapId { get; set; } = -1;
        public int StartX { get; set; } = -1;
        public bool MilestoneComplete { get; set; } = false;
        public BotService BotService { get; set; } = null!;
        public List<NodePoint>? NavigationPath { get; set; } = null;
        public int NavigationPathIndex { get; set; } = 0;

        public void SwapState(EventState newState)
        {
            State.Pop();
            State.Push(newState);
        }
    }
}
