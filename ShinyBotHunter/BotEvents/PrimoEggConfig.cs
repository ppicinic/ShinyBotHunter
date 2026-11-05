using ShinyBotHunter.Games;
using System.Text.Json;

namespace ShinyBotHunter.BotEvents
{
    /// <summary>
    /// Everything about the Primo egg hunt that is a property of the save and the map rather than of the code:
    /// which egg, where the player stands facing Primo, the tile the walk to the line ends on, the line, and the macros.
    ///
    /// Loaded from disk on every event start rather than at boot, so it can be corrected against a running game
    /// without restarting the server.
    /// </summary>
    public class PrimoEggConfig
    {
        public const string DefaultPath = "./Configs/Events/PrimoEgg.json";

        /// <summary>National dex number of the species in the egg. Slugma is 218.</summary>
        public int EggSpecies { get; set; } = 218;

        /// <summary>The egg the password asks for: 8 Mareep, 9 Wooper, 10 Slugma.</summary>
        public int Egg { get; set; } = 10;

        /// <summary>Raw game map id of the tile facing Primo (the value GetMapId returns).</summary>
        public int StartMapId { get; set; } = 158;

        public int StartX { get; set; } = 12;
        public int StartY { get; set; } = 12;

        /// <summary>Facing at the start tile: Up, Down, Left or Right.</summary>
        public string StartFacing { get; set; } = "Up";

        /// <summary>The tile the navigator's walk from the start tile ends on, where pacing starts; raw game map id.</summary>
        public int LineMapId { get; set; } = 73;
        public int LineX { get; set; } = 469;
        public int LineY { get; set; } = 258;

        /// <summary>Which axis the bot paces along: "x" or "y".</summary>
        public string HatchAxis { get; set; } = "x";

        /// <summary>The two ends of the pacing line, as absolute tile coordinates on the axis above.</summary>
        public int HatchLow { get; set; }
        public int HatchHigh { get; set; }

        public string SoftResetMacro { get; set; } = "SoftReset";

        /// <summary>Randomized rather than plain ContinueGame, so each attempt starts from a different seed.</summary>
        public string ContinueMacro { get; set; } = "ContinueGameWithRandom";

        public string SaveMacro { get; set; } = "SaveGame";

        /// <summary>Time for the console to come back up after a soft reset, before the continue macro.</summary>
        public int SoftResetDelayMs { get; set; } = 15000;

        /// <summary>Time for the save to finish loading after the continue macro.</summary>
        public int PostContinueDelayMs { get; set; } = 4000;

        /// <summary>How long the pacing counter or a hatch wait may see nothing change before it gives up.</summary>
        public int HatchStaleMs { get; set; } = 30000;

        public static PrimoEggConfig Load(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Primo egg config not found at '{path}'. It holds the start tile, the line's start tile and the line, none of which has a sensible default.", path);
            }
            var config = JsonSerializer.Deserialize<PrimoEggConfig>(File.ReadAllText(path), new JsonSerializerOptions()
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            if (config is null)
            {
                throw new InvalidOperationException($"Primo egg config at '{path}' is empty.");
            }
            config.Validate(path);
            return config;
        }

        /// <summary>
        /// Fails at event start rather than mid-hunt. Only checks values the flow actually uses.
        /// </summary>
        public void Validate(string path)
        {
            if (!Enum.TryParse<Direction>(StartFacing, ignoreCase: true, out var facing) || facing == Direction.None)
            {
                throw new InvalidOperationException($"{path}: startFacing must be Up, Down, Left or Right, got '{StartFacing}'.");
            }
            string axis = HatchAxis?.ToLowerInvariant() ?? "";
            if (axis != "x" && axis != "y")
            {
                throw new InvalidOperationException($"{path}: hatchAxis must be 'x' or 'y', got '{HatchAxis}'.");
            }
            if (HatchLow >= HatchHigh)
            {
                throw new InvalidOperationException($"{path}: hatchLow ({HatchLow}) must be less than hatchHigh ({HatchHigh}).");
            }
            if (Egg is < 8 or > 10)
            {
                throw new InvalidOperationException($"{path}: egg must be 8 (Mareep), 9 (Wooper) or 10 (Slugma), got {Egg}.");
            }
            if (HatchStaleMs <= 0)
            {
                throw new InvalidOperationException($"{path}: hatchStaleMs must be above 0, got {HatchStaleMs}.");
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

        /// <summary>The parsed form of <see cref="StartFacing"/>. Validated at load.</summary>
        public Direction StartFacingDirection =>
            Enum.TryParse<Direction>(StartFacing, ignoreCase: true, out var d) ? d : Direction.Up;

        public bool IsVerticalCorridor => HatchAxis.ToLowerInvariant() == "y";
    }
}
