using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShinyBotHunter.BotEvents
{
    // Loaded from disk on every Run, so a bad file is a 400 and a fix needs no restart.
    public class StaticEncounterConfig
    {
        public const string DefaultPath = "./Configs/Events/StaticEncounter.json";

        public string SoftResetMacro { get; set; } = "SoftReset";
        public string ContinueMacro { get; set; } = "ContinueGameWithRandom";
        public int SoftResetDelayMs { get; set; } = 15000;
        public int PostContinueDelayMs { get; set; } = 4000;
        public int CutsceneStaleMs { get; set; } = 200000;
        public List<Entry> Encounters { get; set; } = [];

        public class Entry
        {
            public string Name { get; set; } = "";
            public int Species { get; set; }
            public int MapId { get; set; }
            public int[] Spot { get; set; } = [];
            public int[] PokemonTouch { get; set; } = [];
            public int[] SlotTouch { get; set; } = [];
            public int[] SummaryTouch { get; set; } = [];
        }

        public static StaticEncounterConfig Load(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Static encounter config not found at '{path}'. It holds the encounters, none of which has a sensible default.", path);
            }
            StaticEncounterConfig? config;
            try
            {
                config = JsonSerializer.Deserialize<StaticEncounterConfig>(File.ReadAllText(path), new JsonSerializerOptions()
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                });
            }
            catch (JsonException ex)
            {
                // The controller maps InvalidOperationException to a 400; it does not map JsonException.
                throw new InvalidOperationException($"{path}: {ex.Message}", ex);
            }
            if (config is null)
            {
                throw new InvalidOperationException($"Static encounter config at '{path}' is empty.");
            }
            config.Validate(path);
            return config;
        }

        // Fails at Run rather than mid-hunt. Only checks values the flow uses.
        public void Validate(string path)
        {
            if (Encounters.Count == 0)
            {
                throw new InvalidOperationException($"{path}: encounters must hold at least one encounter.");
            }
            var names = new HashSet<string>();
            for (int i = 0; i < Encounters.Count; i++)
            {
                Entry entry = Encounters[i];
                if (string.IsNullOrWhiteSpace(entry.Name))
                {
                    throw new InvalidOperationException($"{path}: name of encounter {i + 1} is empty.");
                }
                if (!names.Add(entry.Name))
                {
                    throw new InvalidOperationException($"{path}: name '{entry.Name}' appears more than once.");
                }
                if (entry.Species is < 1 or > 493)
                {
                    throw new InvalidOperationException($"{path}: species of '{entry.Name}' must be 1 to 493, got {entry.Species}.");
                }
                var points = new (string Key, int[] Point)[]
                {
                    ("spot", entry.Spot), ("pokemonTouch", entry.PokemonTouch), ("slotTouch", entry.SlotTouch), ("summaryTouch", entry.SummaryTouch),
                };
                foreach (var (key, point) in points)
                {
                    if (point.Length != 2)
                    {
                        throw new InvalidOperationException($"{path}: {key} of '{entry.Name}' must be two numbers, x and y, got {point.Length}.");
                    }
                }
            }
            if (CutsceneStaleMs <= 0)
            {
                throw new InvalidOperationException($"{path}: cutsceneStaleMs must be above 0, got {CutsceneStaleMs}.");
            }
            if (SoftResetDelayMs < 0)
            {
                throw new InvalidOperationException($"{path}: softResetDelayMs must not be negative, got {SoftResetDelayMs}.");
            }
            if (PostContinueDelayMs < 0)
            {
                throw new InvalidOperationException($"{path}: postContinueDelayMs must not be negative, got {PostContinueDelayMs}.");
            }
        }

        public Entry Find(string? name)
            => Encounters.FirstOrDefault(e => e.Name == name)
                ?? throw new ArgumentException($"'{name}' is not a static encounter. Choose one of: {string.Join(", ", Encounters.Select(e => e.Name))}.");
    }
}
