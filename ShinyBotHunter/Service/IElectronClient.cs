using ShinyBotHunter.Pokemon;
using static ShinyBotHunter.Service.PhaseInfoService;

namespace ShinyBotHunter.Service
{

    public class DashboardConfig
    {
        public DashboardConfig(GamePhase[] gamePhases)
        {
            GamePhases = gamePhases;
        }

        public GamePhase[] GamePhases { get; private set; }
        public int RecentEncounterCount { get; set; }
    }

    // One game file's Pickup tally: the GET body's entries and the push payload.
    public record PickupItemsResponse(string GameFileName, int Total, List<PickupItemCount> Items);
    public record PickupItemCount(int Id, string Name, int Count);

    public interface IElectronClient
    {
        Task RecieveEncounters(GameEncounterResponse pokemon);
        Task ReceiveDashboardConfig(DashboardConfig config);
        Task ReceiveDashboardState(Dictionary<int, EmulatorState> state);
        Task ReceivePickupItems(PickupItemsResponse items);
    }
}
