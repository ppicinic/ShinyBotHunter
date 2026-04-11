using ShinyBotHunter.Emulators;

namespace ShinyBotHunter.Bot
{
    public interface IBot : ILauncher, ICommandClient, IMemoryClient
    {
    }
}
