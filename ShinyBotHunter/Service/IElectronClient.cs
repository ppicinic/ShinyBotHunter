using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
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
        public string[] PickupGameFiles { get; set; } = [];
    }

    // The dashboard themes (docs/specs/dashboard-alive.md FR-12): the day and night times, the default choice and the per-window overrides.
    public class Appearance
    {
        public string DayStart { get; set; } = "07:00";
        public string NightStart { get; set; } = "19:00";
        public AnimationChoice Animations { get; set; } = new();
        public ThemeChoice Default { get; set; } = new() { Theme = "midnight" };
        public Dictionary<string, ThemeChoice> Windows { get; set; } = new();
    }

    public class ThemeChoice
    {
        public string Theme { get; set; } = null!;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Night { get; set; }
    }

    // How much each kind of theme animation moves (docs/specs/dashboard-animation-cost.md FR-12); a missing member is high.
    public class AnimationChoice
    {
        [BsonRepresentation(BsonType.String)]
        public AnimationLevel Gradients { get; set; } = AnimationLevel.high;
        [BsonRepresentation(BsonType.String)]
        public AnimationLevel Snow { get; set; } = AnimationLevel.high;
        [BsonRepresentation(BsonType.String)]
        public AnimationLevel Stars { get; set; } = AnimationLevel.high;
    }

    // Lowercase because the member names are the values on the wire and in Mongo.
    public enum AnimationLevel { off, low, medium, high }

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
