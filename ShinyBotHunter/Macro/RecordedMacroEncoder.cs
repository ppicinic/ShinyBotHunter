using System.Text;

namespace ShinyBotHunter.Macro
{
    /// <summary>
    /// One run of consecutive emulator frames that shared the same input state, as captured by the
    /// ApiHawk macro recorder.
    /// </summary>
    public sealed record RecordedInputRun(int Frames, IReadOnlyList<string> Buttons, bool Touch, int X, int Y)
    {
        public bool IsIdle => Buttons.Count == 0 && !Touch;
    }

    /// <summary>
    /// Turns a recorded frame trace into a macro YAML document.
    ///
    /// Pure — no I/O, no emulator, no clock — so the encoding rules can be pinned down by unit tests
    /// rather than by replaying macros and squinting at the game.
    /// </summary>
    public class RecordedMacroEncoder
    {
        /// <summary>
        /// The first frame entry of a macro is applied for one frame fewer than its written count.
        /// <see cref="ApiHawk"/>'s stack stepper decrements the top entry *before* the joypad is
        /// applied, so an entry written as N is applied on N-1 frames when it is pushed onto an empty
        /// stack, and on exactly N frames thereafter (once a preceding entry's pop hands it a "free"
        /// first frame). Adding one to the leading step makes a recording round-trip to the same
        /// duration it was captured at.
        /// </summary>
        private const int FirstStepFrameCompensation = 1;

        private readonly int tapMaxFrames;
        private readonly int touchSnapRadius;

        public RecordedMacroEncoder(int tapMaxFrames = 5, int touchSnapRadius = 6)
        {
            this.tapMaxFrames = tapMaxFrames;
            this.touchSnapRadius = touchSnapRadius;
        }

        /// <summary>
        /// Parses the recorder wire format: one run per line, <c>frames|btn1,btn2|x|y</c>, with the
        /// x/y fields empty when the run is not a touch. Blank and malformed lines are skipped.
        /// </summary>
        public static List<RecordedInputRun> ParseRuns(string text)
        {
            List<RecordedInputRun> runs = new();
            if (string.IsNullOrWhiteSpace(text)) return runs;

            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim('\r', ' ');
                if (line.Length == 0) continue;

                string[] parts = line.Split('|');
                if (parts.Length < 4) continue;
                if (!int.TryParse(parts[0], out int frames) || frames <= 0) continue;

                List<string> buttons = parts[1]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(b => b.Trim())
                    .Where(b => b.Length > 0)
                    .ToList();

                bool touch = int.TryParse(parts[2], out int x) & int.TryParse(parts[3], out int y);
                runs.Add(new RecordedInputRun(frames, buttons, touch, touch ? x : 0, touch ? y : 0));
            }

            return runs;
        }

        /// <summary>Renders a recorded trace as a macro YAML document ready to paste into Macro/Configs.</summary>
        public string Encode(IReadOnlyList<RecordedInputRun> runs, string macroName, GameFlavor flavor)
        {
            List<RecordedInputRun> trimmed = Trim(runs);
            int capturedFrames = runs.Sum(r => r.Frames);

            StringBuilder sb = new();
            sb.Append("# Recorded macro — ").Append(capturedFrames).Append(" frames captured, ")
              .Append(trimmed.Count).Append(" steps after trimming leading/trailing idle.").Append('\n');
            sb.Append("# Timings are a literal frame trace. Consider replacing fixed waits with W.R[min,max]\n");
            sb.Append("# where the game's timing actually varies.\n");
            sb.Append("name: ").Append(SanitizeName(macroName)).Append('\n');
            sb.Append("flavor: ").Append(flavor).Append('\n');

            if (trimmed.Count == 0)
            {
                sb.Append("# No input was captured — nothing but idle frames.\n");
                sb.Append("steps: []\n");
                return sb.ToString();
            }

            sb.Append("steps:\n");
            for (int i = 0; i < trimmed.Count; i++)
            {
                int frames = trimmed[i].Frames + (i == 0 ? FirstStepFrameCompensation : 0);
                (List<string> actions, string comment) = EncodeRun(trimmed[i], frames);

                string body = actions.Count == 1
                    ? actions[0]
                    : "[" + string.Join(", ", actions.Select(a => "\"" + a + "\"")) + "]";

                sb.Append("  - ").Append(body);
                if (comment.Length > 0) sb.Append("  # ").Append(comment);
                sb.Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>Drops leading and trailing idle runs; gaps between real input are preserved.</summary>
        private static List<RecordedInputRun> Trim(IReadOnlyList<RecordedInputRun> runs)
        {
            int start = 0;
            int end = runs.Count - 1;
            while (start <= end && runs[start].IsIdle) start++;
            while (end >= start && runs[end].IsIdle) end--;

            List<RecordedInputRun> result = new();
            for (int i = start; i <= end; i++) result.Add(runs[i]);
            return result;
        }

        private (List<string> Actions, string Comment) EncodeRun(RecordedInputRun run, int frames)
        {
            if (run.IsIdle) return (new List<string> { $"W.{frames}" }, "");

            List<string> actions = new();
            string comment = "";

            if (run.Touch)
            {
                if (CommandPreprocessor.TryFindShorthand(run.X, run.Y, touchSnapRadius, out string name, out string[] alternates))
                {
                    // The bare form lets CommandPreprocessor apply its own default tap length; only
                    // spell out a count when the touch was genuinely held.
                    actions.Add(frames <= tapMaxFrames ? $"S.{name}" : $"S.{name}.{frames}");

                    // Always show the raw pixel so a wrong snap is visible on review rather than
                    // silently baked into the macro.
                    comment = $"recorded ({run.X},{run.Y})";
                    if (alternates.Length > 0)
                        comment += $"; same coords as {string.Join(", ", alternates)}";
                }
                else
                {
                    actions.Add($"S.{run.X}.{run.Y}.{frames}");
                }
            }

            foreach (string button in run.Buttons)
                actions.Add(frames <= tapMaxFrames ? $"T.{button}" : $"H.{button}.{frames}");

            return (actions, comment);
        }

        /// <summary>
        /// Macro names are dictionary keys and are referenced from C# by literal, so keep them to a
        /// plain identifier rather than trusting whatever was typed into the dashboard.
        /// </summary>
        private static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "RecordedMacro";

            StringBuilder sb = new();
            foreach (char c in name.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
            }

            if (sb.Length == 0) return "RecordedMacro";
            if (char.IsDigit(sb[0])) sb.Insert(0, '_');
            return sb.ToString();
        }
    }
}
