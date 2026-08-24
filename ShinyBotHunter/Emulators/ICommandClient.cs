namespace ShinyBotHunter.Emulators
{
    public record MacroStruct(string macro, bool repeat = false);
    public interface ICommandClient : IAsyncDisposable
    {
        public void Connect();
        public void Tap(string button);
        public void Hold(string button);

        public void HoldFrames(string button, int frames);

        public void Release();

        public void Release(string button);

        // ── Macros ────────────────────────────────────────────────────────────────

        public void RunMacroString(string macro);

        public void RunRepeatMacro(string macro);
        public void RunMacros(params MacroStruct[] macro);

        public void ClearMacro();

        public Task WaitForMacroAsync(CancellationToken ct = default);
        Task FlushSave(CancellationToken ct = default);

        // -- Macro recording (dev only) --------------------------------------------
        // While recording, the emulator stops applying bot input entirely so a human can play
        // and have every frame captured. Only supported by the ApiHawk command server.

        /// <summary>Begin capturing per-frame input. Clears any running macro.</summary>
        Task StartRecordingAsync(CancellationToken ct = default);

        /// <summary>Stop capturing and return the raw run-length-encoded frame text.</summary>
        Task<string> StopRecordingAsync(CancellationToken ct = default);

        /// <summary>Raw status line: <c>recording|runs|frames</c>.</summary>
        Task<string> GetRecordingStatusAsync(CancellationToken ct = default);
    }
}
