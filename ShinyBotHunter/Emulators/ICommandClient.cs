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
    }
}
