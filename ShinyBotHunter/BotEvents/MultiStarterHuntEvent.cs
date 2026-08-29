using ShinyBotHunter.Bot;
using ShinyBotHunter.Context;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.BotEvents
{
    /// <summary>
    /// Hunts a queue of Ruby/Sapphire starters across several emulators.
    ///
    /// Each starter gets a wave of emulators racing on freshly allocated ROM copies. When one finds a
    /// keeper, that emulator is parked mid-battle for the player, the rest of its wave is torn down,
    /// and the next starter's wave begins on new copies -- so a hunt can be several starters deep
    /// with several shinies parked before the player has touched any of them.
    ///
    /// Resume settles one parked shiny per press, oldest first: the battle has been played out by
    /// hand, so it saves, records, retires that copy and closes that emulator. The dashboard's
    /// Waiting/Resume surface stays up whenever at least one shiny is parked.
    ///
    /// The rule underneath all of it: a copy whose save is a shiny -- parked or already saved -- is
    /// never allocated to a hunt again. Retired copies are persisted; parked ones are excluded for as
    /// long as they are parked. Reusing one would eventually overwrite the save that is the entire
    /// point of the exercise. The HGSS hunt enforces this with its GameIds map; this is the same
    /// intent carried over.
    /// </summary>
    public class MultiStarterHuntEvent : AbstractBotEvent<PokemonData?>
    {
        private readonly BotFactory botFactory;
        private readonly StarterHuntStateStore stateStore;
        private readonly IServiceProvider provider;
        private readonly StarterHuntConfig config;
        private readonly int emulatorsPerWave;
        private readonly IReadOnlyList<StarterHuntEntry> queue;
        private readonly string milestone;
        private readonly int emulatorBudget;

        private sealed record ParkedShiny(
            string Starter, IBot Bot, StarterHuntEvent Hunt, GameState HuntState, string CopyHandle, int Position, bool Forced);

        private readonly object gate = new();
        private readonly List<ParkedShiny> parked = new();
        private readonly HashSet<int> positionsInUse = new();
        private GameState? sharedState;
        private TaskCompletionSource resumeHappened = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override EventState EventState => EventState.Complete;

        public int[] BotPositions
        {
            get { lock (gate) { return positionsInUse.Count == 0 ? new[] { 0 } : positionsInUse.ToArray(); } }
        }

        public PokemonData? Result { get; private set; }

        public MultiStarterHuntEvent(IReadOnlyList<StarterHuntEntry> queue, string milestone,
            int emulatorsPerWave, int emulatorBudget, StarterHuntConfig config, IServiceProvider provider)
        {
            this.queue = queue;
            this.milestone = milestone;
            this.emulatorsPerWave = emulatorsPerWave;
            this.emulatorBudget = emulatorBudget;
            this.config = config;
            this.provider = provider;
            this.botFactory = provider.GetRequiredService<BotFactory>();
            this.stateStore = provider.GetRequiredService<StarterHuntStateStore>();

            config.Validate();
            foreach (StarterHuntEntry entry in queue)
            {
                config.MacroForStarter(entry.Starter);
            }
        }

        public override async Task<PokemonData?> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            // Re-entered by the dashboard's Resume. The main run below is still going (or waiting on
            // its parked shinies), so this call only settles one of them and returns.
            if (state.State.Count > 0 && state.State.Peek() == EventState.Resume)
            {
                await ResumeOldestAsync();
                return Result;
            }

            sharedState = state;
            StarterHuntGameState progress = await stateStore.LoadOrCreateAsync(GameFlavor.RS, milestone);
            ReportAndClearStalePending(progress);
            await stateStore.SaveAsync(progress);

            foreach (StarterHuntEntry entry in queue)
            {
                if (StarterHuntStateStore.IsDone(progress, entry.Starter))
                {
                    Console.WriteLine($"[StarterHunt] {entry.Starter} is already caught for '{milestone}'; skipping.");
                    continue;
                }

                await HuntOneStarterAsync(entry, cancellationToken);
                if (cancellationToken.IsCancellationRequested) break;
            }

            // Every starter is found or skipped; what remains is the player playing out the parked
            // battles. The event stays alive until the last Resume so those emulators stay owned.
            while (!cancellationToken.IsCancellationRequested)
            {
                Task waiter;
                lock (gate)
                {
                    if (parked.Count == 0) break;
                    waiter = resumeHappened.Task;
                }
                Console.WriteLine($"[StarterHunt] {CountParked()} shiny battle(s) waiting to be played out and resumed.");
                await Task.WhenAny(waiter, Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => { }));
            }

            SetDashboardState(EventState.Complete);
            Console.WriteLine("[StarterHunt] Queue finished.");
            return Result;
        }

        /// <summary>Runs waves for one queue entry until an emulator parks with a keeper.</summary>
        private async Task HuntOneStarterAsync(StarterHuntEntry entry, CancellationToken cancellationToken)
        {
            // Parked emulators keep holding slots from the budget, so a deep queue narrows the waves
            // rather than exceeding what the machine was told it may run.
            int waveSize;
            while ((waveSize = Math.Min(emulatorsPerWave, emulatorBudget - CountParked())) < 1
                   && !cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine("[StarterHunt] All emulator slots are held by parked shinies; resume one to continue.");
                await WaitForAResumeAsync(cancellationToken);
            }
            if (cancellationToken.IsCancellationRequested) return;

            // Never allocate a copy that is retired (its save is a caught shiny) or parked (its save
            // will be). This is the property the whole registry exists for.
            IReadOnlyList<string> retired = await stateStore.GetRetiredCopiesAsync(GameFlavor.RS);
            List<string> unavailable;
            lock (gate) { unavailable = retired.Concat(parked.Select(p => p.CopyHandle)).ToList(); }
            List<int> copyIds = StarterHuntStateStore.AllocateCopyIds(entry.RomHandle, waveSize, unavailable);

            Console.WriteLine(
                $"[StarterHunt] Hunting {entry.Starter} on {waveSize} emulator(s) of {entry.RomHandle}, copies " +
                $"[{string.Join(", ", copyIds.Select(id => entry.RomHandle + id))}].");

            using var wave = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var runs = copyIds.Select(id => RunOneAsync(entry, id, wave.Token)).ToList();

            // The first find ends the wave -- one starter, then the next. But a second emulator can
            // have found in the same instant, before the cancellation reached it, and in a real hunt
            // that is a genuine shiny: it parks too rather than being discarded or, worse, leaked as
            // a paused emulator nothing tracks. One find per STARTER is enforced by cancelling the
            // wave, not by throwing away concurrent winners.
            bool waveCancelled = false;
            while (runs.Count > 0)
            {
                Task<ParkedShiny?> finished = await Task.WhenAny(runs);
                runs.Remove(finished);

                ParkedShiny? winner = null;
                try { winner = await finished; }
                catch (OperationCanceledException) { continue; }
                catch (Exception ex)
                {
                    Console.WriteLine($"[StarterHunt] An emulator stopped with an error: {ex.Message}");
                    continue;
                }
                if (winner is null) continue;

                lock (gate) { parked.Add(winner); }
                Result = winner.HuntState.TargetPokemon?.PokemonData;
                SetDashboardState(EventState.Waiting);
                Console.WriteLine(
                    $"[StarterHunt] Shiny {entry.Starter} parked on {winner.CopyHandle} (position {winner.Position}). " +
                    $"{CountParked()} waiting.");

                if (!waveCancelled)
                {
                    waveCancelled = true;
                    Console.WriteLine($"[StarterHunt] Stopping the {entry.Starter} wave and moving on.");
                    await wave.CancelAsync();
                }
            }
        }

        /// <summary>One emulator's hunt on its own freshly allocated copy.</summary>
        private async Task<ParkedShiny?> RunOneAsync(StarterHuntEntry entry, int copyId, CancellationToken cancellationToken)
        {
            int position = ClaimPosition();
            GameContext context = botFactory.BuildGameContext(entry.RomHandle, copyId, position);
            IBot bot = botFactory.CreateBot(context);

            var hunt = new StarterHuntEvent(bot, context, entry, config, provider);
            GameState huntState = new()
            {
                GameFlavor = GameFlavor.RS,
                Bot = bot,
                Milestone = milestone,
                Target = new Target.ShinyTarget(),
            };

            try
            {
                PokemonData? shiny = await hunt.ExecuteWithResultAsync(huntState, cancellationToken);
                if (shiny is null)
                {
                    await DisposeQuietly(bot);
                    ReleasePosition(position);
                    return null;
                }

                // Persisted before the player touches it: the wait can outlive the process, and the
                // record is what reports the loss if it does. But the record is bookkeeping and the
                // shiny is the point -- a failure here is logged and swallowed, because letting it
                // throw handed the parked emulator to the cleanup path, which closed a live shiny
                // over a serialization error.
                try
                {
                    StarterHuntGameState progress = await stateStore.LoadOrCreateAsync(GameFlavor.RS, milestone);
                    progress.PendingShinies.Add(new PendingShiny
                    {
                        Starter = entry.Starter,
                        RomHandle = context.GameName,
                        RecordingName = entry.RecordingName,
                        PID = shiny.PID,
                        Species = shiny.Species,
                        FoundUtc = DateTime.UtcNow,
                        PokemonData = shiny,
                    });
                    await stateStore.SaveAsync(progress);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"[StarterHunt] Could not record the pending shiny on {context.GameName} ({ex.Message}). " +
                        "The emulator stays parked; a restart before Resume will not know about this one.");
                }

                return new ParkedShiny(entry.Starter, bot, hunt, huntState, context.GameName, position, hunt.ResultWasForced);
            }
            catch
            {
                // Cancelled or crashed, the emulator goes with its run. Cleaning up only on
                // cancellation is how every cancelled wave stranded its losers: a run that died on
                // any other exception left EmuHawk open and frozen mid-battle, starter on screen,
                // looking exactly like a find nothing ever collected.
                await DisposeQuietly(bot);
                ReleasePosition(position);
                throw;
            }
        }

        /// <summary>
        /// Settles the oldest parked shiny: the player has played the battle out, so save, record,
        /// retire the copy and close that emulator. One per press; the Waiting surface stays up while
        /// more are parked.
        /// </summary>
        private async Task ResumeOldestAsync()
        {
            ParkedShiny? oldest;
            lock (gate) { oldest = parked.FirstOrDefault(); }
            if (oldest is null)
            {
                Console.WriteLine("[StarterHunt] Nothing is parked; nothing to resume.");
                return;
            }

            Console.WriteLine($"[StarterHunt] Resuming {oldest.Starter} on {oldest.CopyHandle}: saving.");
            oldest.HuntState.State.Push(EventState.Resume);
            await oldest.Hunt.ExecuteWithResultAsync(oldest.HuntState, CancellationToken.None);

            // Forced finds retire and complete like real ones. These are the paths the test exists
            // to prove -- retirement is what keeps the next wave off this copy, completion is what
            // makes a rerun skip this starter -- and a test that skips them tests nothing. The test
            // state lands in the same PhaseStates records as a real hunt's, so clear those (and the
            // test copies) before hunting for real.
            await stateStore.RetireCopyAsync(GameFlavor.RS, oldest.CopyHandle);
            PokemonData? caught = oldest.HuntState.TargetPokemon?.PokemonData;
            await stateStore.MarkStarterCompletedAsync(GameFlavor.RS, milestone, new CompletedFind
            {
                Starter = oldest.Starter,
                CopyHandle = oldest.CopyHandle,
                RecordingName = oldest.HuntState.TargetPokemon?.GameFileName,
                PID = caught?.PID ?? 0,
                Species = caught?.Species ?? 0,
                CaughtUtc = DateTime.UtcNow,
            });
            if (oldest.Forced)
            {
                Console.WriteLine(
                    $"[StarterHunt] [TEST] Retired {oldest.CopyHandle} and completed {oldest.Starter} as a real " +
                    "find would; only the caught-pokemon record is skipped.");
            }

            StarterHuntGameState progress = await stateStore.LoadOrCreateAsync(GameFlavor.RS, milestone);
            progress.PendingShinies.RemoveAll(p => string.Equals(p.RomHandle, oldest.CopyHandle, StringComparison.OrdinalIgnoreCase));
            await stateStore.SaveAsync(progress);

            await DisposeQuietly(oldest.Bot);
            int remaining;
            lock (gate)
            {
                parked.Remove(oldest);
                positionsInUse.Remove(oldest.Position);
                remaining = parked.Count;
                resumeHappened.TrySetResult();
                resumeHappened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            Console.WriteLine($"[StarterHunt] {oldest.Starter} saved on {oldest.CopyHandle}. {remaining} still parked.");
            SetDashboardState(remaining > 0 ? EventState.Waiting : EventState.Running);
        }

        private void ReportAndClearStalePending(StarterHuntGameState progress)
        {
            foreach (PendingShiny lost in progress.PendingShinies)
            {
                Console.WriteLine(
                    $"[StarterHunt] Discarding a shiny {lost.Starter} (PID {lost.PID:X8}) on {lost.RomHandle} " +
                    "left unsaved by an earlier run; its emulator is gone.");
            }
            progress.PendingShinies.Clear();
        }

        private async Task WaitForAResumeAsync(CancellationToken cancellationToken)
        {
            Task waiter;
            lock (gate) { waiter = resumeHappened.Task; }
            await Task.WhenAny(waiter, Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => { }));
        }

        private int CountParked() { lock (gate) { return parked.Count; } }

        private int ClaimPosition()
        {
            lock (gate)
            {
                int position = 0;
                while (positionsInUse.Contains(position)) position++;
                positionsInUse.Add(position);
                return position;
            }
        }

        private void ReleasePosition(int position) { lock (gate) { positionsInUse.Remove(position); } }

        /// <summary>
        /// What the dashboard sees. Waiting is what surfaces the Resume button, so it is set exactly
        /// while at least one shiny is parked.
        /// </summary>
        private void SetDashboardState(EventState newState)
        {
            GameState? shared = sharedState;
            if (shared is null) return;
            lock (gate)
            {
                if (shared.State.Count > 0) shared.State.Pop();
                shared.State.Push(newState);
            }
        }

        private static async Task DisposeQuietly(IBot bot)
        {
            try { await bot.DisposeAsync(); } catch { }
        }
    }
}
