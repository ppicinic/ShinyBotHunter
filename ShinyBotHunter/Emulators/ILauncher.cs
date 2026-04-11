namespace ShinyBotHunter.Emulators
{
    public interface ILauncher : IAsyncDisposable
    {
        public Task LaunchAsync();
        public Task RepositionAsync(int x, int y);
        public Task RepositionAsync(int placement);
    }
}
