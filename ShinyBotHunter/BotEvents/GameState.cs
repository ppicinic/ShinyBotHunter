using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents.Target;
using ShinyBotHunter.Games;
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

    public enum TrainType
    {
        None,
        Standard,
        // The pokemon being trained leads (slot 1) so it is sent out and earns participation EXP,
        // then the bot immediately switches to a stronger party member which does the fighting.
        Switch,
    }
    public class GameState
    {
        public ITarget Target { get; set; } = null!;
        public IBot Bot { get; set; } = null!;
        public BattleMode BattleMode { get; set; }
        public BattleMode OriginalBattleMode { get; set; }
        public Stack<EventState> State { get; private set; } = new Stack<EventState>();
        public GameProfile Profile { get; set; } = null!;
        public GameFlavor GameFlavor => Profile.Flavor;
        public string Milestone { get; set; } = null!;

        public PokemonBattleState? BattleState { get; set; } = null;
        public List<int> BattlePositions { get; set; } = [];
        public PersistPokemonData? TargetPokemon { get; set; } = null;
        public Dictionary<string, string> EncounterMacros { get; set; } = new Dictionary<string, string>();
        public Dictionary<(uint, ushort), EvolutionMetadata> PendingEvolutions { get; set; } = new Dictionary<(uint, ushort), EvolutionMetadata>();
        public int StartY { get; set; } = -1;
        public int StartMapId { get; set; } = -1;
        public int StartX { get; set; } = -1;
        public Direction StartFacing { get; set; } = Direction.None;
        public bool MilestoneComplete { get; set; } = false;
        public BotService BotService { get; set; } = null!;
        public List<NodePoint>? NavigationPath { get; set; } = null;
        public int NavigationPathIndex { get; set; } = 0;
        /// <summary>
        /// Extra pathfinding cost for tiles the bot has got stuck on, keyed by bot map id then tile.
        /// Standing next to a signpost and pressing into it opens its message, which locks movement
        /// until dismissed -- the tile is walkable so A* has no reason to avoid it, and the sign is
        /// not on it so marking obstacles doesn't help. Learning the tile and making it expensive is
        /// enough to route around it while leaving it usable when there is no other way past.
        /// Lives with the run: shared across the legs of a chain, gone on restart.
        /// </summary>
        public Dictionary<int, Dictionary<(int X, int Y), int>> TilePenalties { get; } = new Dictionary<int, Dictionary<(int X, int Y), int>>();
        public TrainType TrainType { get; set; } = TrainType.None;
        public int? TargetLevel { get; set; } = null;
        public bool TrainingComplete { get; set; } = false;
        // 1-based party slot of the battler we switch to. Only set when TrainType is Switch.
        public int? SwitchSlot { get; set; } = null;
        // Reset at the start of every battle so we only switch once per encounter.
        public bool SwitchPerformed { get; set; } = false;
        // Set once the run's end condition is met. ReturnToStart reads it to decide whether arriving
        // back at the start tile finishes the event or hands back to the overworld loop.
        public bool CompleteAfterReturn { get; set; } = false;

        public void SwapState(EventState newState)
        {
            State.Pop();
            State.Push(newState);
        }
    }
}
