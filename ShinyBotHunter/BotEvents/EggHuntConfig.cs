using ShinyBotHunter.Pathfinding;
using System.Text.Json;

namespace ShinyBotHunter.BotEvents
{
    /// <summary>A point on the touch screen, in the same 256x192 space the S. macro command uses.</summary>
    public class TouchPoint
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    /// <summary>
    /// Everything about an egg hunt that is a property of the save file rather than of the code:
    /// which macros to play, where the hatching corridor is, and where the party menu buttons sit.
    ///
    /// Loaded from disk on every event start rather than at boot, so the corridor and the touch
    /// coordinates can be corrected against a running game without restarting the server.
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

        // -- Summary-screen touch coordinates and timings. Not used by the active hunt any more -- the
        // shiny sparkle is visible during the hatch animation itself (HatchAnimationMacro), so IsShiny
        // is read straight off party memory right after it plays and opening the summary screen is a
        // redundant confirmation step. Left measured and in place rather than deleted, in case a future
        // change wants that confirmation back (touching the pokedex entry, a different reveal check,
        // etc.) -- see the (currently unused) TouchPoint/PartySlotTouch/SummaryTouch fields below.

        /// <summary>
        /// "Pokemon" on the touch-screen main menu. Matches CommandPreprocessor's shorthand -- which
        /// was declared but never exercised by any macro until a recording of the summary-open sequence
        /// (see OpenPartyMenuWaitFrames etc. below) landed a raw tap a couple pixels from this, so it is
        /// corroborated rather than just declared.
        /// </summary>
        public TouchPoint PokemonMenuTouch { get; set; } = new TouchPoint() { X = 45, Y = 75 };

        /// <summary>Party screen slots 1-6, in order. Measured against a real party of six.</summary>
        public List<TouchPoint> PartySlotTouch { get; set; } = new List<TouchPoint>() {
            new TouchPoint() { X = 60, Y = 25 },
            new TouchPoint() { X = 192, Y = 35 },
            new TouchPoint() { X = 60, Y = 75 },
            new TouchPoint() { X = 192, Y = 85 },
            new TouchPoint() { X = 60, Y = 125 },
            new TouchPoint() { X = 192, Y = 135 },
        };

        /// <summary>"Summary" on the party member's submenu.</summary>
        public TouchPoint SummaryTouch { get; set; } = new TouchPoint() { X = 176, Y = 42 };

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

        // -- Summary-opening sequence, from a real recording of tapping through Pokemon -> slot ->
        // Summary. The three gaps are not interchangeable -- the menu takes longer to open than a
        // party slot takes to react -- so each is its own measured value rather than one shared guess.
        // Unused by the active hunt; see the note above PokemonMenuTouch.

        /// <summary>How long the touch on "Pokemon" is held to open the party menu.</summary>
        public int OpenPartyMenuHoldFrames { get; set; } = 6;

        /// <summary>Wait after opening the party menu, before tapping the hatched pokemon's slot.</summary>
        public int OpenPartyMenuWaitFrames { get; set; } = 107;

        /// <summary>Wait after tapping the party slot, before tapping Summary.</summary>
        public int SlotTapWaitFrames { get; set; } = 44;

        /// <summary>How long the touch on Summary is held.</summary>
        public int SummaryTapHoldFrames { get; set; } = 5;

        /// <summary>Wait after tapping Summary before the shiny star (or its absence) is actually rendered.</summary>
        public int SummaryTapWaitFrames { get; set; } = 156;

        /// <summary>How long the summary stays on screen once it is open, for a human to see it too.</summary>
        public int SummaryHoldMs { get; set; } = 5000;

        /// <summary>Time for the console to come back up after a soft reset, before the continue macro.</summary>
        public int SoftResetDelayMs { get; set; } = 15000;

        /// <summary>Time for the save to finish loading after the continue macro.</summary>
        public int PostContinueDelayMs { get; set; } = 4000;

        /// <summary>Safety margin after a save so the flush lands before anything else happens.</summary>
        public int PostSaveDelayMs { get; set; } = 10000;

        // Remembered so ValidateTouchCoordinates(), called later than Load(), can still name this
        // file in its error message.
        private string sourcePath = DefaultPath;

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
            config.sourcePath = path;
            config.Validate(path);
            return config;
        }

        /// <summary>
        /// Fails at event start rather than mid-hunt. Only checks values the active flow actually
        /// touches -- the summary-screen coordinates above are unused right now, so they are not
        /// checked here; see the note on PokemonMenuTouch.
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

        /// <summary>
        /// Checks the summary-screen coordinates. Not called by Load()/Validate() -- the active hunt
        /// never opens the summary screen, so these are unused; kept callable for whatever eventually
        /// brings that step back rather than deleted alongside the fields it checks.
        /// </summary>
        public void ValidateTouchCoordinates()
        {
            if (PartySlotTouch.Count != 6)
            {
                throw new InvalidOperationException($"{sourcePath}: partySlotTouch needs six entries, one per party slot; found {PartySlotTouch.Count}.");
            }
            for (int i = 0; i < PartySlotTouch.Count; i++)
            {
                if (PartySlotTouch[i].X == 0 && PartySlotTouch[i].Y == 0)
                {
                    throw new InvalidOperationException($"{sourcePath}: partySlotTouch[{i}] is still (0,0). Measure it against the running game.");
                }
            }
            if (SummaryTouch.X == 0 && SummaryTouch.Y == 0)
            {
                throw new InvalidOperationException($"{sourcePath}: summaryTouch is still (0,0). Measure it against the running game.");
            }
        }

        /// <summary>Touch point for a 1-based party slot. Unused by the active hunt; see the note above PokemonMenuTouch.</summary>
        public TouchPoint SlotTouch(int slot) => PartySlotTouch[slot - 1];

        /// <summary>The parsed form of <see cref="StartFacing"/>. Validated at load.</summary>
        public Direction StartFacingDirection =>
            Enum.TryParse<Direction>(StartFacing, ignoreCase: true, out var d) ? d : Direction.Up;

        public bool IsVerticalCorridor => (HatchAxis?.ToLowerInvariant() ?? "y") == "y";
    }
}
