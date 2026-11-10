using System.Text.Json.Serialization;
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
        public Appearance Appearance { get; set; } = new();
    }

    // The dashboard themes (docs/specs/dashboard-alive.md FR-12): the day and night times, the default choice and the per-window overrides.
    public class Appearance
    {
        public string DayStart { get; set; } = "07:00";
        public string NightStart { get; set; } = "19:00";
        public bool Animations { get; set; } = true;
        public ThemeChoice Default { get; set; } = new() { Theme = "midnight" };
        public Dictionary<string, ThemeChoice> Windows { get; set; } = new();
    }

    public class ThemeChoice
    {
        public string Theme { get; set; } = null!;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Night { get; set; }
    }

    // One game file's Pickup tally: the GET body's entries and the push payload.
    public record PickupItemsResponse(string GameFileName, string Milestone, List<PickupItemCount> Items);
    public record PickupItemCount(int Id, string Name, int Count);

    public interface IElectronClient
    {
        Task RecieveEncounters(GameEncounterResponse pokemon);
        Task ReceiveDashboardConfig(DashboardConfig config);
        Task ReceiveDashboardState(Dictionary<int, EmulatorState> state);
        Task ReceivePickupItems(PickupItemsResponse items);
    }
}
