using ShinyBotHunter.Bot;
using ShinyBotHunter.Context;

namespace ShinyBotHunter.BotEvents
{
    public enum EventState
    {
        None,
        NotStarted,
        Running,
        Waiting,
        Complete,
        Encountering,
        BattleStart,
        BattleComplete,
        PokemonDataPending,
        Flee,
        Fight,
        SwitchPokemon,
        DecideAction,
        WaitForMovesEnd,
        EnemyFainted,
        OurPokemonFainted,
        BothPokemonFainted,
        LevelUp,
        EvolutionInit,
        DecideOverworldAction,
        HealAndReturn,
        GoToPokemonCenter,
        HealAtPokemonCenter,
        ReturnToStart,
        Init,
        Resume,
    }
    public interface IBotEvent
    {
        public Task ExecuteAsync(GameState state, CancellationToken cancellationToken);
        public int[] BotPositions { get { return []; } }
        public GameState GameState { get
            {
                return new GameState();
            } 
        }
    }

    public interface IBotEvent<T> : IBotEvent
    {
        public Task<T> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken);
    }
}
