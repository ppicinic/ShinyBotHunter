using System.Text.Json;
using ShinyBotHunter.Emulators.Dolphin;

namespace ShinyBotHunter.BotEvents
{
    /// <summary>
    /// Operator-editable settings for the Wishmaker Jirachi hunt (FR-20), in the same shape as
    /// <see cref="StarterHuntConfig"/>: loaded and validated when the event is built, so a bad value
    /// refuses the start instead of failing a cycle an hour in.
    /// </summary>
    public class WishmakerJirachiConfig
    {
        public const string DefaultPath = "./Configs/Events/WishmakerJirachi.json";

        public string ContinueMacro { get; set; } = "ContinueGame";

        /// <summary>Confirms the save, starting with the cursor already on SAVE.</summary>
        public string SaveMacro { get; set; } = "SaveGame";

        public int SaveMenuActionId { get; set; } = 5;

        /// <summary>The random stretch in the field before saving, drawn uniformly from this range.</summary>
        public int MinPlayMs { get; set; } = 2000;
        public int MaxPlayMs { get; set; } = 20000;

        public int LaunchReadyTimeoutMs { get; set; } = 240000;

        /// <summary>One in-game save, from the save macro until a complete new block is on disk.</summary>
        public int SaveTimeoutMs { get; set; } = 40000;

        /// <summary>The budget for each press loop up to the gift flow; divided by its press interval.</summary>
        public int DiscMenuTimeoutMs { get; set; } = 60000;

        /// <summary>The budget for the presses up to the power prompt, and for the GBA reset window.</summary>
        public int DiscPromptTimeoutMs { get; set; } = 60000;

        public int TransferTimeoutMs { get; set; } = 90000;

        public int DsuPort { get; set; } = 26760;

        public static WishmakerJirachiConfig Load(string path = DefaultPath)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Wishmaker Jirachi config not found at {path}.", path);
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };

            WishmakerJirachiConfig config = JsonSerializer.Deserialize<WishmakerJirachiConfig>(File.ReadAllText(path), options)
                ?? throw new InvalidOperationException($"Wishmaker Jirachi config at {path} deserialized to nothing.");

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
            Require(SaveMacro, nameof(SaveMacro));

            void Positive(int value, string name)
            {
                if (value <= 0)
                    throw new InvalidOperationException($"{path}: {name} must be greater than zero (was {value}).");
            }

            Positive(MinPlayMs, nameof(MinPlayMs));
            Positive(MaxPlayMs, nameof(MaxPlayMs));
            Positive(LaunchReadyTimeoutMs, nameof(LaunchReadyTimeoutMs));
            Positive(SaveTimeoutMs, nameof(SaveTimeoutMs));
            Positive(DiscMenuTimeoutMs, nameof(DiscMenuTimeoutMs));
            Positive(DiscPromptTimeoutMs, nameof(DiscPromptTimeoutMs));
            Positive(TransferTimeoutMs, nameof(TransferTimeoutMs));
            Positive(DsuPort, nameof(DsuPort));

            if (MinPlayMs >= MaxPlayMs)
            {
                throw new InvalidOperationException(
                    $"{path}: MinPlayMs ({MinPlayMs}) must be below MaxPlayMs ({MaxPlayMs}).");
            }

            if (SaveMenuActionId < 0)
            {
                throw new InvalidOperationException($"{path}: SaveMenuActionId cannot be negative.");
            }

            // The press counts are these budgets divided by the disc's press intervals; a budget below
            // its interval derives 0 presses and the disc would fail on its first check.
            if (DiscMenuTimeoutMs < BonusDisc.PlayIntervalMs)
            {
                throw new InvalidOperationException(
                    $"{path}: DiscMenuTimeoutMs ({DiscMenuTimeoutMs}) is below one press interval " +
                    $"({BonusDisc.PlayIntervalMs} ms), so the disc menu would get no presses.");
            }
            if (DiscPromptTimeoutMs < BonusDisc.PromptIntervalMs)
            {
                throw new InvalidOperationException(
                    $"{path}: DiscPromptTimeoutMs ({DiscPromptTimeoutMs}) is below one press interval " +
                    $"({BonusDisc.PromptIntervalMs} ms), so the power prompt would get no presses.");
            }
        }
    }
}
