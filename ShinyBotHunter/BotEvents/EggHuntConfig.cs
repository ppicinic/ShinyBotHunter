using ShinyBotHunter.Pathfinding;
using System.Text.Json;

namespace ShinyBotHunter.BotEvents
{
    /// <summary>
    /// Everything about an egg hunt that is a property of the save file rather than of the code:
    /// which macros to play and where the hatching corridor is.
    ///
    /// Loaded from disk on every event start rather than at boot, so the corridor can be corrected
    /// against a running game without restarting the server.
    /// </summary>
    public class EggHuntConfig
    {
        public const string DefaultPath = "./Configs/Events/TogepiEgg.json";

        /// <summary>
        /// Macro that walks to the NPC, clears the dialogue through to holding the egg, and ends
        /// standing on the pacing corridor. There is deliberately no separate walk-to-corridor step --
        /// ReceiveTogepiEgg already leaves the bot in the right spot.
        /// </summary>
        public string ReceiveEggMacro { get; set; } = "ReceiveTogepiEgg";

        /// <summary>
        /// Plays once the egg flag clears, to progress the hatch cry/animation/nickname prompt through
        /// to the overworld. A fixed, hand-recorded sequence rather than something polled for -- there
        /// is no reliable signal in party memory for "the animation is done" the way there is for "it
        /// started" (see the class doc on <see cref="HgssTogepiEggEvent"/>), so this has to be timed
        /// against a real hatch rather than detected. The shiny sparkle plays during this animation, so
        /// IsShiny is read straight off the party mon right after it finishes -- no summary screen.
        /// </summary>
        public string HatchAnimationMacro { get; set; } = "HatchEggAnimation";

        public string SaveMacro { get; set; } = "SaveGame";

        /// <summary>
        /// Randomized rather than plain ContinueGame -- a soft reset otherwise lands on the title
        /// screen at the same frame every time, and pressing A back into the file on identical fixed
        /// waits would advance the RNG by the same amount each attempt, defeating the point of
        /// resetting at all. The randomized waits (W.R[min,max] in the macro) spread that out so each
        /// attempt starts from a different seed.
        /// </summary>
        public string ContinueMacro { get; set; } = "ContinueGameWithRandom";

        public string SoftResetMacro { get; set; } = "SoftReset";

        /// <summary>National dex number of the species in the egg. Togepi is 175.</summary>
        public int EggSpecies { get; set; } = 175;

        // -- Start position. ReceiveTogepiEgg is a fixed input trace, so it only works from one exact
        // tile and facing; anywhere else and its first A press hits the wrong thing. The event walks
        // here itself before every attempt rather than making the operator know the spot.

        /// <summary>Raw game map id the receive macro starts from (the value GetMapId returns).</summary>
        public int StartMapId { get; set; } = 157;

        public int StartX { get; set; } = 6;
        public int StartY { get; set; } = 9;

        /// <summary>Facing the receive macro starts from: Up, Down, Left or Right.</summary>
        public string StartFacing { get; set; } = "Up";

        /// <summary>Which axis the bot paces along: "x" or "y".</summary>
        public string HatchAxis { get; set; } = "y";

        /// <summary>
        /// The two ends of the corridor, as absolute tile coordinates on the axis above. The walk
        /// macro is what puts the bot on the corridor, so absolute endpoints are deterministic in a
        /// way "current position plus a range" is not.
        /// </summary>
        public int HatchLow { get; set; }
        public int HatchHigh { get; set; }

        // -- Timings (milliseconds unless the name says frames) -----------------------------------

        /// <summary>
        /// Settle time after arriving at the start tile, before the receive macro is played. The walk
        /// macro reports done the frame the destination coordinate lands, which is a frame or two
        /// ahead of the character actually finishing its step animation and accepting input again --
        /// starting the macro on that frame drops its first press.
        /// </summary>
        public int StartPositionSettleMs { get; set; } = 1000;

        /// <summary>How long to wait for the egg to appear in the party after the receive macro ends.</summary>
        public int EggWaitTimeoutMs { get; set; } = 60000;

        /// <summary>How long to pace before giving up on the egg ever hatching.</summary>
        public int HatchTimeoutMs { get; set; } = 30 * 60 * 1000;

        /// <summary>Time for the console to come back up after a soft reset, before the continue macro.</summary>
        public int SoftResetDelayMs { get; set; } = 15000;

        /// <summary>Time for the save to finish loading after the continue macro.</summary>
        public int PostContinueDelayMs { get; set; } = 4000;

        /// <summary>Safety margin after a save so the flush lands before anything else happens.</summary>
        public int PostSaveDelayMs { get; set; } = 10000;

        public static EggHuntConfig Load(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Egg hunt config not found at '{path}'. It holds the macro names and the hatching corridor, neither of which has a sensible default.", path);
            }
            var config = JsonSerializer.Deserialize<EggHuntConfig>(File.ReadAllText(path), new JsonSerializerOptions()
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            if (config is null)
            {
                throw new InvalidOperationException($"Egg hunt config at '{path}' is empty.");
            }
            config.Validate(path);
            return config;
        }

        /// <summary>
        /// Fails at event start rather than mid-hunt. Only checks values the active flow actually touches.
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
            if (HatchLow == HatchHigh)
            {
                throw new InvalidOperationException($"{path}: hatchLow and hatchHigh are both {HatchLow}, so the bot would never take a step.");
            }
            if (HatchLow > HatchHigh)
            {
                throw new InvalidOperationException($"{path}: hatchLow ({HatchLow}) must be less than hatchHigh ({HatchHigh}).");
            }
        }

        /// <summary>The parsed form of <see cref="StartFacing"/>. Validated at load.</summary>
        public Direction StartFacingDirection =>
            Enum.TryParse<Direction>(StartFacing, ignoreCase: true, out var d) ? d : Direction.Up;

        public bool IsVerticalCorridor => (HatchAxis?.ToLowerInvariant() ?? "y") == "y";
    }
}
