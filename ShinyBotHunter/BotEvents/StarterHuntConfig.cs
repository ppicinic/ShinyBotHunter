using System.Text.Json;

namespace ShinyBotHunter.BotEvents
{
    /// <summary>
    /// Operator-editable settings for the Ruby/Sapphire starter hunt.
    ///
    /// Follows the same shape as <see cref="EggHuntConfig"/>: loaded from disk and validated when the
    /// event is constructed, so a bad value surfaces as a refused start rather than as an emulator
    /// mashing buttons against the wrong screen an hour into a hunt.
    /// </summary>
    public class StarterHuntConfig
    {
        public const string DefaultPath = "./Configs/Events/RsStarterHunt.json";

        public string ContinueMacro { get; set; } = "ContinueGameWithRandom";
        public string CreateSaveMacro { get; set; } = "CreateNewSave";
        public string SoftResetMacro { get; set; } = "SoftReset";
        public string BattleAdvanceMacro { get; set; } = "MashB";

        /// <summary>Confirms the save, starting with the cursor already on SAVE.</summary>
        public string SaveConfirmMacro { get; set; } = "SaveGame";

        /// <summary>Macro to run for each starter, keyed by species name.</summary>
        public Dictionary<string, string> StarterMacros { get; set; } = new();

        public int PartyRetryFrames { get; set; } = 120;
        public int BattleWaitTimeoutMs { get; set; } = 30000;
        public int BattlePollIntervalMs { get; set; } = 250;
        public int LaunchSettleMs { get; set; } = 10000;
        public int SoftResetSettleMs { get; set; } = 4000;
        public int SaveSettleMs { get; set; } = 10000;

        /// <summary>
        /// How long to keep trying to read the starter after the battle begins. The battle flag turns
        /// on before the pokemon is out of its ball, so the first reads legitimately fail; this is the
        /// budget for the game to settle, not an expected wait.
        /// </summary>
        public int StarterReadTimeoutMs { get; set; } = 20000;

        /// <summary>How long to wait between read attempts while the game settles.</summary>
        public int StarterReadPollIntervalMs { get; set; } = 500;

        /// <summary>
        /// How long to wait for the starter to appear on the battlefield before moving on anyway.
        /// A budget, not an expected wait: the run continues either way, since the pokemon has
        /// already been read and verified by then.
        /// </summary>
        public int StarterOnFieldTimeoutMs { get; set; } = 20000;

        /// <summary>
        /// Settle time once the starter is on the field. The species appears slightly before the
        /// sprite has finished arriving, so a moment here is what makes a run watchable. Set to 0
        /// once the hunt is trusted and the seconds matter.
        /// </summary>
        public int StarterOnFieldSettleMs { get; set; } = 1000;

        public int SaveMenuActionId { get; set; } = 5;

        public bool LogLegalityForEveryEncounter { get; set; } = true;

        public static StarterHuntConfig Load(string path = DefaultPath)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Starter hunt config not found at {path}.", path);
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };

            StarterHuntConfig config = JsonSerializer.Deserialize<StarterHuntConfig>(File.ReadAllText(path), options)
                ?? throw new InvalidOperationException($"Starter hunt config at {path} deserialized to nothing.");

            config.Validate(path);
            return config;
        }

        public void Validate(string path = DefaultPath)
        {
            void Require(string value, string name)
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new InvalidOperationException($"{path}: {name} must name a macro.");
            }

            Require(ContinueMacro, nameof(ContinueMacro));
            Require(CreateSaveMacro, nameof(CreateSaveMacro));
            Require(SoftResetMacro, nameof(SoftResetMacro));
            Require(BattleAdvanceMacro, nameof(BattleAdvanceMacro));
            Require(SaveConfirmMacro, nameof(SaveConfirmMacro));

            if (StarterMacros.Count == 0)
            {
                throw new InvalidOperationException($"{path}: StarterMacros is empty, so no starter can be chosen.");
            }
            foreach (var (starter, macro) in StarterMacros)
            {
                if (string.IsNullOrWhiteSpace(macro))
                    throw new InvalidOperationException($"{path}: StarterMacros['{starter}'] must name a macro.");
            }

            void Positive(int value, string name)
            {
                if (value <= 0)
                    throw new InvalidOperationException($"{path}: {name} must be greater than zero (was {value}).");
            }

            Positive(PartyRetryFrames, nameof(PartyRetryFrames));
            Positive(BattleWaitTimeoutMs, nameof(BattleWaitTimeoutMs));
            Positive(BattlePollIntervalMs, nameof(BattlePollIntervalMs));
            Positive(LaunchSettleMs, nameof(LaunchSettleMs));
            Positive(SoftResetSettleMs, nameof(SoftResetSettleMs));
            Positive(SaveSettleMs, nameof(SaveSettleMs));
            Positive(StarterReadTimeoutMs, nameof(StarterReadTimeoutMs));
            Positive(StarterReadPollIntervalMs, nameof(StarterReadPollIntervalMs));
            Positive(StarterOnFieldTimeoutMs, nameof(StarterOnFieldTimeoutMs));
            if (StarterOnFieldSettleMs < 0)
            {
                throw new InvalidOperationException($"{path}: StarterOnFieldSettleMs cannot be negative.");
            }
            if (StarterReadPollIntervalMs > StarterReadTimeoutMs)
            {
                throw new InvalidOperationException(
                    $"{path}: StarterReadPollIntervalMs ({StarterReadPollIntervalMs}) exceeds " +
                    $"StarterReadTimeoutMs ({StarterReadTimeoutMs}), so the starter would be read once at most.");
            }

            if (BattlePollIntervalMs > BattleWaitTimeoutMs)
            {
                throw new InvalidOperationException(
                    $"{path}: BattlePollIntervalMs ({BattlePollIntervalMs}) exceeds BattleWaitTimeoutMs " +
                    $"({BattleWaitTimeoutMs}), so the battle would never be polled for.");
            }

            if (SaveMenuActionId < 0)
            {
                throw new InvalidOperationException($"{path}: SaveMenuActionId cannot be negative.");
            }
        }

        /// <summary>Macro for a starter, with a message naming what is configured if it is missing.</summary>
        public string MacroForStarter(string starter)
        {
            if (!StarterMacros.TryGetValue(starter, out string? macro))
            {
                throw new InvalidOperationException(
                    $"No macro configured for starter '{starter}'. Configured: {string.Join(", ", StarterMacros.Keys)}.");
            }
            return macro;
        }
    }
}
