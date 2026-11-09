using System.Text.Json;

namespace ShinyBotHunter.BotEvents
{
    /// <summary>
    /// The Pickup trip's measured values (docs/research/pickup.md): the party menu's touch targets and the
    /// stale-timeouts. Loaded at the decision, so a value can be corrected against a running game without a build.
    /// </summary>
    public class PickupConfig
    {
        public const string DefaultPath = "./Configs/Events/Pickup.json";

        /// <summary>Touch targets as (x, y) on the DS bottom screen.</summary>
        public int[] PokemonTouch { get; set; } = [];
        public int[][] SlotTouches { get; set; } = [];
        public int[] ItemTouch { get; set; } = [];
        public int[] TakeTouch { get; set; } = [];
        public int[] CancelTouch { get; set; } = [];

        public int StartMenuStaleMs { get; set; }
        public int PartyMenuStaleMs { get; set; }
        public int StepStaleMs { get; set; }
        public int FieldFreeStaleMs { get; set; }

        public static PickupConfig Load(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Pickup config not found at '{path}'. It holds the party menu's touch targets, none of which has a sensible default.", path);
            }
            var config = JsonSerializer.Deserialize<PickupConfig>(File.ReadAllText(path), new JsonSerializerOptions()
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            if (config is null)
            {
                throw new InvalidOperationException($"Pickup config at '{path}' is empty.");
            }
            config.Validate(path);
            return config;
        }

        /// <summary>
        /// Fails at the decision rather than mid-trip. Only checks values the trip actually uses.
        /// </summary>
        public void Validate(string path)
        {
            if (SlotTouches.Length != 6)
            {
                throw new InvalidOperationException($"{path}: slotTouches must hold six touches, got {SlotTouches.Length}.");
            }
            var touches = new (string Key, int[] Touch)[]
            {
                ("pokemonTouch", PokemonTouch), ("itemTouch", ItemTouch), ("takeTouch", TakeTouch), ("cancelTouch", CancelTouch),
            }.Concat(SlotTouches.Select(t => ("slotTouches", t)));
            foreach (var (key, touch) in touches)
            {
                if (touch.Length != 2)
                {
                    throw new InvalidOperationException($"{path}: {key} must be two ints (x, y), got {touch.Length}.");
                }
            }
            var stales = new (string Key, int Ms)[]
            {
                ("startMenuStaleMs", StartMenuStaleMs), ("partyMenuStaleMs", PartyMenuStaleMs), ("stepStaleMs", StepStaleMs), ("fieldFreeStaleMs", FieldFreeStaleMs),
            };
            foreach (var (key, ms) in stales)
            {
                if (ms <= 0)
                {
                    throw new InvalidOperationException($"{path}: {key} must be above 0, got {ms}.");
                }
            }
        }
    }
}
