using ShinyBotHunter.BotEvents;

namespace ShinyBotHunter.Service
{
    public class BotEventRunner(EmulatorSlotRegistry slots, Func<Task> refreshDashboard)
    {
        /// <summary>
        /// Cancels the running event at <paramref name="pos"/>, drops a parked one, or closes an idle
        /// bot. A running event is only signalled under the gate and awaited outside it, because its
        /// own teardown in <see cref="RunEventAsync"/> takes the gate; this returns once that teardown
        /// and its dashboard refresh have run.
        /// </summary>
        public async Task CancelAsync(int pos)
        {
            Task? running = null;
            await slots.Gate.WaitAsync();
            try
            {
                var entry = slots.Entries[pos];
                if (entry.EventTask is not null)
                {
                    entry.CancellationTokenSource!.Cancel();
                    running = entry.EventTask;
                }
                else if (entry.Event is not null)
                {
                    entry.Event = null;
                    entry.GameState = null;
                }
                else
                {
                    // Dispose never calls back into BotService, so awaiting it under the gate is safe.
                    await entry.Bot!.DisposeAsync();
                    slots.Entries.TryRemove(pos, out _);
                }
            }
            finally
            {
                slots.Gate.Release();
            }
            if (running is not null)
            {
                await running;
            }
        }

        /// <summary>
        /// Starts <paramref name="ev"/> on <paramref name="entry"/>. Only called under the gate, so
        /// <see cref="RunEventAsync"/> always reaches its gate wait asynchronously and cannot clear
        /// EventTask before this assigns it.
        /// </summary>
        public void StartEvent(int key, BotAndOrEvent entry, IBotEvent ev, GameState state, string name, bool removeWhenDone)
        {
            var cts = new CancellationTokenSource();
            entry.Event = ev;
            entry.EventName = name;
            entry.CancellationTokenSource = cts;
            entry.EventTask = RunEventAsync(key, entry, ev.ExecuteAsync(state, cts.Token), cts, state, name, removeWhenDone);
        }

        /// <summary>
        /// Owns the whole teardown of an event, whether it was cancelled or finished on its own, so
        /// the order is decided in one place. Never faults: an event crash is logged here, and the
        /// dashboard refresh is caught because nothing is guaranteed to await a natural finish.
        /// </summary>
        private async Task RunEventAsync(int key, BotAndOrEvent entry, Task eventTask, CancellationTokenSource cts, GameState state, string name, bool removeWhenDone)
        {
            try
            {
                await eventTask;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                // Without this, an unhandled exception in the event loop is indistinguishable from a
                // clean finish: the bot silently goes idle, or a hunt vanishes, with no trace of why.
                Console.WriteLine($"[BotService] Event '{name}' crashed. State: {(state.State.TryPeek(out var crashedAt) ? crashedAt : EventState.None)}");
                Console.WriteLine(ex.ToString());
            }

            bool cancelled = cts.IsCancellationRequested;
            if (!cancelled)
            {
                await cts.CancelAsync();
            }

            await slots.Gate.WaitAsync();
            try
            {
                if (cancelled)
                {
                    entry.Event = null;
                    entry.GameState = null;
                }
                else if (!(state.State.TryPeek(out var top) && top == EventState.Waiting))
                {
                    entry.Event = null;
                }
                entry.EventTask = null;
                entry.CancellationTokenSource = null;
                if (removeWhenDone)
                {
                    // The key captured at start, not a position read back from the event.
                    slots.Entries.TryRemove(key, out _);
                }
            }
            finally
            {
                slots.Gate.Release();
            }

            try
            {
                await refreshDashboard();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BotService] Dashboard refresh after event '{name}' failed: {ex}");
            }
        }
    }
}
