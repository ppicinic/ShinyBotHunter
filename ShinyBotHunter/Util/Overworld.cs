using ShinyBotHunter.Games;
using System.Diagnostics;

namespace ShinyBotHunter.Util
{
    public static class Overworld
    {
        // Upper bound on how long we wait for the overworld coordinates to populate before giving up
        // and proceeding anyway. Long enough for a battle fade-out, short enough not to stall a run
        // if the heuristic ever fails to spot a valid position.
        public const int ReadyTimeoutMs = 3000;

        /// <summary>
        /// Waits until the trainer coordinates look like a loaded overworld.
        ///
        /// Coming out of a battle or a warp, the position reads (0,0) or negative for a moment while
        /// the field fades back in. Anything that acts on the trainer's position or facing has to wait
        /// this out, or it works from a bogus origin -- a route pathfound from nowhere, or a spin
        /// started against a facing value that isn't real yet.
        /// </summary>
        /// <returns>True once the coordinates look valid, false if the timeout elapsed first.</returns>
        public static async Task<bool> WaitForReadyAsync(IOverworldReader overworld, CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            while (!cancellationToken.IsCancellationRequested && sw.ElapsedMilliseconds < ReadyTimeoutMs)
            {
                var (x, y) = await overworld.GetPositionAsync(cancellationToken);
                // Neither coord can be negative. An edge tile (one == 0) is valid, but both-zero
                // and any negative are the not-loaded values (e.g. (0,0), (0,-1)).
                if (x >= 0 && y >= 0 && (x > 0 || y > 0))
                {
                    return true;
                }
                await Task.Delay(Constant.PollIntervalMs, cancellationToken);
            }
            return false;
        }
    }
}
