using Microsoft.Extensions.Options;
using ShinyBotHunter.Bot;
using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Service
{
    public record MacroRecordResult(string Yaml, int FrameCount, int StepCount);

    public record MacroRecordStatus(bool Recording, int RunCount, int FrameCount);

    /// <summary>
    /// Dev-only macro recorder. Puts an emulator into a mode where ApiHawk stops driving the joypad
    /// and instead captures the human's per-frame input, then encodes the trace as macro YAML.
    ///
    /// Gated by <see cref="ApplicationSettings.EnableMacroRecording"/>; the controller enforces the
    /// flag as well, so a stale dashboard cannot reach these calls.
    /// </summary>
    public class MacroRecordingService
    {
        private readonly BotService botService;
        private readonly ApplicationSettings settings;

        public MacroRecordingService(BotService botService, IOptions<ApplicationSettings> appSettings)
        {
            this.botService = botService;
            this.settings = appSettings.Value;
        }

        public bool Enabled => settings.EnableMacroRecording;

        public async Task StartAsync(int pos, CancellationToken ct = default)
        {
            IBot bot = ResolveBot(pos);

            // Recording and a running event would fight over the joypad, and the recording would
            // capture the bot's own macro input rather than the human's.
            if (botService.HasRunningEvent(pos))
                throw new InvalidOperationException($"Emulator {pos} is running an event. Cancel it before recording.");

            await bot.StartRecordingAsync(ct);
        }

        public async Task<MacroRecordResult> StopAsync(int pos, string macroName, CancellationToken ct = default)
        {
            IBot bot = ResolveBot(pos);

            string raw = await bot.StopRecordingAsync(ct);
            List<RecordedInputRun> runs = RecordedMacroEncoder.ParseRuns(raw);

            RecordedMacroEncoder encoder = new(settings.MacroRecordTapMaxFrames, settings.MacroRecordTouchSnapRadius);
            // The flavor comes from the emulator being recorded, so recording on a GBA emulator emits
            // `flavor: RS` rather than silently tagging every macro as HGSS.
            string yaml = encoder.Encode(runs, macroName, bot.GameFlavor);

            int frameCount = runs.Sum(r => r.Frames);
            int stepCount = yaml.Split('\n').Count(l => l.StartsWith("  - "));
            return new MacroRecordResult(yaml, frameCount, stepCount);
        }

        public async Task<MacroRecordStatus> GetStatusAsync(int pos, CancellationToken ct = default)
        {
            IBot bot = ResolveBot(pos);

            // Wire format: "recording|<runs>|<frames>"
            string raw = (await bot.GetRecordingStatusAsync(ct)).Trim();
            string[] parts = raw.Split('|');
            if (parts.Length < 3) return new MacroRecordStatus(false, 0, 0);

            int.TryParse(parts[1], out int runs);
            int.TryParse(parts[2], out int frames);
            return new MacroRecordStatus(parts[0] == "recording", runs, frames);
        }

        private IBot ResolveBot(int pos)
        {
            // The legacy Lua command server has no /record/* routes; without this the HTTP client
            // would burn its full retry budget on 404s before failing with a confusing message.
            if (settings.BizHawkMode != "ApiHawk")
                throw new InvalidOperationException(
                    $"Macro recording requires BizHawkMode 'ApiHawk' (currently '{settings.BizHawkMode}').");

            return botService.GetBotAt(pos)
                ?? throw new InvalidOperationException($"No emulator running at position {pos}.");
        }
    }
}
