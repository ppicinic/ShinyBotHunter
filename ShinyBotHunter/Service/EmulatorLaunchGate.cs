namespace ShinyBotHunter.Service
{
    /// <summary>
    /// Serializes emulator launches: the next EmuHawk starts only once the previous one's ApiHawk
    /// server has answered a request.
    ///
    /// Launching several within a second of each other made their startups race -- BizHawk instances
    /// booting simultaneously contend for the machine and each other's shared install, and a launch
    /// that lands in that contention can miss its readiness window. Gating on confirmed connection
    /// rather than a fixed stagger means the pace adapts: a fast boot releases the gate early, a slow
    /// one holds the queue instead of letting the next launch pile on top of it.
    ///
    /// Only the launch-to-first-response stretch is held. What runs after -- save creation, hunt
    /// cycles -- is concurrent as before.
    /// </summary>
    public class EmulatorLaunchGate
    {
        private readonly SemaphoreSlim gate = new(1, 1);

        public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
        {
            await gate.WaitAsync(cancellationToken);
            return new Releaser(gate);
        }

        private sealed class Releaser(SemaphoreSlim gate) : IDisposable
        {
            private int released;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref released, 1) == 0)
                {
                    gate.Release();
                }
            }
        }
    }
}
