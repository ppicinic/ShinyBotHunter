using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Context;
using System.Collections.Concurrent;

namespace ShinyBotHunter.Service
{
    public class EmulatorSlotRegistry(int maxEmulators)
    {
        // Lock rule: writers take the gate, readers never do. A writer is any code that changes
        // Entries or a field of a BotAndOrEvent: BotService.ExecuteNewBotOrSpecialEvent,
        // BotService.ExecuteEvent, BotService.CancelBotOrEventAsync and the teardown in
        // BotService.RunEventAsync. Each holds the gate across its whole check-then-act sequence.
        // Readers (BotService.GetBotDashboardState, GetAvailableActions, GetBotAt, HasRunningEvent,
        // GetPartyAsync) stay lock-free because running events call them from inside their loops,
        // and an event waiting on the gate while a canceller holds it and awaits that event would
        // deadlock. The dictionary is concurrent so their enumeration is safe against a writer's add
        // or remove, and they read each entry field once into a local. A writer must never call
        // another writer: the gate is not re-entrant.
        public SemaphoreSlim Gate { get; } = new(1, 1);

        // Written only under Gate; concurrent so the lock-free readers can enumerate it.
        public ConcurrentDictionary<int, BotAndOrEvent> Entries { get; } = new();

        /// <summary>
        /// The occupied display positions, sorted, and the lowest free one. The caller holds
        /// <see cref="Gate"/>.
        /// </summary>
        public (List<int> Occupied, int Next) AllocatePosition()
        {
            var positions = Entries.Values.Aggregate(new List<int>(), (acc, x) => { acc.AddRange(x.GetBotPositions()); return acc; });
            positions.Sort();
            if (positions.Count() >= maxEmulators)
            {
                throw new Exception("No open space to start any new emulators");
            }
            int newPos = positions.Count() == 0 ? 0 : -1;
            for (int i = 0; i < positions.Count() && newPos == -1; i++)
            {
                if (positions[i] != i)
                {
                    newPos = i;
                }
            }
            if(newPos == -1)
            {
                newPos = positions.Count();
            }
            return (positions, newPos);
        }
    }

    public class BotAndOrEvent
    {
        public BotAndOrEvent() { }
        public BotAndOrEvent(IBot bot) 
        {
            Bot = bot;
        }
        public int? BotPosition { get; set; }
        public IBot? Bot { get; set; }

        /// <summary>
        /// How this bot's emulator was built: which ROM file, which console, which flavor. Kept so an
        /// event started later can copy a ROM or resolve a macro without re-deriving any of it.
        /// </summary>
        public GameContext? Context { get; set; }
        public IBotEvent? Event { get; set; }
        public Task? EventTask { get; set; }
        public GameState? GameState { get; set; }
        public CancellationTokenSource? CancellationTokenSource { get; set; }
        public string? EventName { get; set; }
        public string GetName(int pos)
        {
            return EventName is not null ? EventName : $"Bot {pos}";
        }

        public int[] GetBotPositions()
        {
            if (Event is not null)
            {
                if(Event.BotPositions is not null && Event.BotPositions.Length > 0)
                {
                    return Event.BotPositions;
                }
            }
            if(BotPosition is not null)
            {
                return [BotPosition.Value];
            }
            return [];
        }
    }
}
