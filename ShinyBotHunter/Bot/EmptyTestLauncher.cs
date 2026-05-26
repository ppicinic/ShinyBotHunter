using ShinyBotHunter.Emulators;

namespace ShinyBotHunter.Bot
{
    public class EmptyTestLauncher : ILauncher
    {
        
        public EmptyTestLauncher(string gameFileName)
        {
            GameFileName = gameFileName;
        }
        public string GameFileName { get; private set; }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public Task LaunchAsync()
        {
            return Task.CompletedTask;
        }

        public Task RepositionAsync(int x, int y)
        {
            return Task.CompletedTask;
        }

        public Task RepositionAsync(int placement)
        {
            return Task.CompletedTask;
        }
    }
}
