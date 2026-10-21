namespace ShinyBotHunter.Persistence
{
    public static class BackgroundLoop
    {
        // Runs a channel-draining loop until it returns normally, which happens when DisposeAsync completes its channel.
        // A throw is logged with the full exception and the loop is started again; the item being processed is dropped.
        public static async Task RunSupervisedAsync(Func<Task> loop, ILogger logger, string name)
        {
            while (true)
            {
                try
                {
                    await loop();
                    return;
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Background loop {Loop} failed; restarting", name);
                }
            }
        }
    }
}
