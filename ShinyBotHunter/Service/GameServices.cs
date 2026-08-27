using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service.BattlePokemonParser;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Service
{
    /// <summary>
    /// Looks up the per-game services for a flavor.
    ///
    /// These are registered in keyed DI, and the flavor is only known once there is a bot running a
    /// particular game -- which is later than the readers and events that need them are constructed.
    /// Resolving in a constructor therefore meant naming a flavor literally, which is what pinned the
    /// whole codebase to HGSS. Going through here instead lets a caller resolve at the point it
    /// actually knows what it is talking to.
    /// </summary>
    public class GameServices
    {
        private readonly IServiceProvider provider;

        public GameServices(IServiceProvider provider)
        {
            this.provider = provider;
        }

        /// <summary>Memory map: anchors, offsets and structure sizes.</summary>
        public IPointerService Pointers(GameFlavor flavor) => Resolve<IPointerService>(flavor);

        /// <summary>Decoder for the game's stored pokemon structure.</summary>
        public IPokemonParser Parser(GameFlavor flavor) => Resolve<IPokemonParser>(flavor);

        /// <summary>Decoder for the game's in-battle pokemon structure, which differs from the stored one.</summary>
        public IBattlePokemonParser BattleParser(GameFlavor flavor) => Resolve<IBattlePokemonParser>(flavor);

        private T Resolve<T>(GameFlavor flavor) where T : notnull
        {
            T? service = provider.GetKeyedService<T>(flavor);
            if (service is null)
            {
                // Reached when a flavor is playable but its game logic is not implemented yet, so say
                // which piece is missing rather than surfacing a bare DI error.
                throw new InvalidOperationException(
                    $"No {typeof(T).Name} is registered for {flavor}. That game's memory layout and " +
                    "parsers have not been implemented, so it cannot be read from.");
            }
            return service;
        }
    }
}
