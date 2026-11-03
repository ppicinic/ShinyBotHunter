using ShinyBotHunter.Bot;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Util;
using System.Diagnostics;

namespace ShinyBotHunter.BotEvents
{
    /// <summary>
    /// Soft-reset hunt for a shiny hatched from a gift egg -- the Togepi egg in HGSS.
    ///
    /// An egg's contents are fixed at the moment it is received, so the reset point has to be before
    /// the NPC hands it over. One attempt is: play the receive macro (which ends standing on the
    /// pacing corridor), pace it until the egg hatches, and either save and stop or soft reset and go
    /// again.
    ///
    /// The shiny call is deliberately made after the hatch animation plays, not from the egg. The egg
    /// in the party already carries the final PID, so its shininess is readable long before it hatches
    /// and the loop could be cut to seconds per attempt -- but no player can see that, so the bot does
    /// not act on it either. It is read straight from party memory right after the animation
    /// (see <see cref="HgssEggHatch.HatchAsync"/>) rather than from a summary screen -- the shiny sparkle
    /// already plays as part of that animation, so a player watching would have made the same call
    /// already, with no separate screen needed to confirm it a second time.
    ///
    /// Pacing ends on field script 2021 and the hatch is answered from memory in <see cref="HgssEggHatch"/>.
    /// </summary>
    public class HgssTogepiEggEvent : AbstractBotEvent<PokemonData?>
    {
        private enum EggHuntState
        {
            NotStarted,
            ReceivingEgg,
            Hatching,
            SoftResetting,
            Complete,
        }

        // How long a pacing or hatch wait may see nothing change before it gives up.
        private static readonly TimeSpan HatchStale = TimeSpan.FromSeconds(30);

        private readonly IBot bot;
        private readonly GameFlavor flavor;
        private readonly string phase;
        private readonly PokemonStateReader reader;
        private readonly PathfindingService pathfindingService;
        private readonly PokemonPersistenceService persistenceService;
        private readonly ShinyAlertService shinyAlertService;
        private readonly BotService botService;
        private readonly EggHuntConfig config;
        private readonly HgssPointerService addresses;
        private readonly HgssEggHatch hatch;

        private EggHuntState state = EggHuntState.NotStarted;

        public HgssTogepiEggEvent(IBot bot, GameFlavor flavor, string phase, IServiceProvider provider, BotService botService)
        {
            this.bot = bot;
            this.addresses = (HgssPointerService)bot.Addresses;
            this.flavor = flavor;
            this.phase = phase;
            this.botService = botService;
            this.reader = provider.GetRequiredService<PokemonStateReader>();
            this.pathfindingService = bot.Profile.Pathfinding!;
            this.persistenceService = provider.GetRequiredService<PokemonPersistenceService>();
            this.shinyAlertService = provider.GetRequiredService<ShinyAlertService>();
            // Loaded here rather than at boot so the corridor can be corrected
            // between runs without restarting the server. Throws on anything unmeasured, which is
            // why it happens at construction -- before the dashboard reports the event as started.
            this.config = EggHuntConfig.Load(EggHuntConfig.DefaultPath);
            this.hatch = new HgssEggHatch(bot, addresses, reader, HatchStale);
        }

        public override async Task<PokemonData?> ExecuteWithResultAsync(GameState gameState, CancellationToken cancellationToken)
        {
            PushRunningState(gameState);
            await RefreshDashboardAsync();

            try
            {
                await EnsureStartPositionAsync(cancellationToken);
                if (cancellationToken.IsCancellationRequested) return null;

                while (!cancellationToken.IsCancellationRequested)
                {
                    uint eggPid = await ReceiveEggAsync(cancellationToken);
                    if (cancellationToken.IsCancellationRequested) break;

                    SetState(EggHuntState.Hatching);
                    await hatch.PaceUntilHatchAsync(BuildPaceMacro(), cancellationToken);
                    PokemonData hatched = await hatch.HatchAsync(eggPid, cancellationToken);

                    var persistPokemon = new PersistPokemonData()
                    {
                        PokemonData = hatched,
                        DateTime = DateTime.UtcNow,
                        Game = flavor,
                        Phase = phase,
                        GameFileName = bot.GameFileName,
                    };
                    await persistenceService.AddEncounterAsync(persistPokemon);
                    Console.WriteLine($"[TogepiEgg] Hatched {hatched.Name}, shinyValue={hatched.ShinyValue}, shiny={hatched.IsShiny}");

                    if (hatched.IsShiny)
                    {
                        await SaveAndFinishAsync(persistPokemon);
                        state = EggHuntState.Complete;
                        CompleteState(gameState);
                        await RefreshDashboardAsync();
                        return hatched;
                    }

                    await SoftResetAsync(cancellationToken);
                }
            }
            finally
            {
                if (state != EggHuntState.Complete)
                {
                    // Cancelled or thrown out of. Whatever is holding a direction down should not stay
                    // held once nothing is driving the bot any more.
                    try
                    {
                        bot.ClearMacro();
                    }
                    catch { }
                }
            }

            return null;
        }

        // -- Steps ------------------------------------------------------------------------------

        /// <summary>
        /// Puts the bot on the tile the receive macro expects, and saves there so every soft reset
        /// afterwards lands on it too.
        ///
        /// ReceiveTogepiEgg is a fixed input trace -- it only works from one exact tile and facing, or
        /// its first A press hits the wrong thing. Rather than making the operator know that spot, the
        /// event walks there once and saves, which makes the reset point and the start point the same
        /// tile. Every later attempt then arrives there for free via the soft reset, so this runs once
        /// per event rather than once per attempt.
        ///
        /// Does nothing at all when the bot is already standing there correctly -- no walk, and no
        /// save, so an already-correct save file is never rewritten.
        /// </summary>
        private async Task EnsureStartPositionAsync(CancellationToken cancellationToken)
        {
            await Overworld.WaitForReadyAsync(bot.State, cancellationToken);

            ushort mapId = await bot.Overworld.GetMapIdAsync(cancellationToken);
            (short x, short y) = await bot.Overworld.GetPositionAsync(cancellationToken);
            Direction facing = await bot.Overworld.GetFacingAsync(cancellationToken);

            if (mapId == config.StartMapId && x == config.StartX && y == config.StartY && facing == config.StartFacingDirection)
            {
                Console.WriteLine($"[TogepiEgg] Already at the start position (map={mapId} ({x},{y}) facing={config.StartFacingDirection}).");
                return;
            }

            // Cross-map routing would need the whole navigation graph and a lot more assumptions about
            // what lies between here and there. The save is meant to already be in this area, so being
            // on another map means something is wrong that walking cannot fix.
            if (mapId != config.StartMapId)
            {
                throw new InvalidOperationException(
                    $"Bot is on map {mapId} but the hunt starts on map {config.StartMapId}. Load a save in the right area before starting this hunt.");
            }

            if (x != config.StartX || y != config.StartY)
            {
                Console.WriteLine($"[TogepiEgg] At map={mapId} ({x},{y}); walking to the start tile ({config.StartX},{config.StartY}).");
                string? walk = pathfindingService.Pathfind(mapId, x, y, config.StartX, config.StartY, new Capability(), (byte)facing);
                if (walk is null)
                {
                    throw new InvalidOperationException(
                        $"No route from ({x},{y}) to the start tile ({config.StartX},{config.StartY}) on map {mapId}.");
                }
                bot.RunMacroString(walk);
                await bot.WaitForMacroAsync(cancellationToken);
                await Overworld.WaitForReadyAsync(bot.State, cancellationToken);

                (x, y) = await bot.Overworld.GetPositionAsync(cancellationToken);
                if (x != config.StartX || y != config.StartY)
                {
                    throw new InvalidOperationException(
                        $"Walked to the start tile but ended up at ({x},{y}) instead of ({config.StartX},{config.StartY}).");
                }
                facing = await bot.Overworld.GetFacingAsync(cancellationToken);
            }

            if (facing != config.StartFacingDirection)
            {
                // A short tap turns in place rather than stepping -- the same behaviour SpinEncounterEvent
                // relies on. Only safe because the facing differs; tapping the direction already faced
                // would walk off the tile.
                bot.RunMacroString($"T.{config.StartFacingDirection};W.5");
                await bot.WaitForMacroAsync(cancellationToken);
            }

            // The walk macro reports done the frame the coordinate lands, a frame or two before input is
            // accepted again.
            await Task.Delay(config.StartPositionSettleMs, cancellationToken);

            Console.WriteLine($"[TogepiEgg] At the start position; saving so soft resets return here.");
            bot.RunMacroString(MacroRegistry.Instance.GetMacro(flavor, config.SaveMacro));
            await bot.WaitForMacroAsync(cancellationToken);
            await bot.FlushSave();
            await Task.Delay(config.PostSaveDelayMs, cancellationToken);
        }

        /// <summary>Plays the receive macro and waits for the egg to turn up in the party.</summary>
        /// <returns>The egg's PID, which the hatched pokemon keeps and is identified by later.</returns>
        private async Task<uint> ReceiveEggAsync(CancellationToken cancellationToken)
        {
            SetState(EggHuntState.ReceivingEgg);
            // The macro opens by pressing A immediately, so it has to start from a settled overworld --
            // whether we just walked here or just came back from a soft reset.
            await Overworld.WaitForReadyAsync(bot.State, cancellationToken);
            await Task.Delay(config.StartPositionSettleMs, cancellationToken);

            bot.RunMacroString(MacroRegistry.Instance.GetMacro(flavor, config.ReceiveEggMacro));
            await bot.WaitForMacroAsync(cancellationToken);

            var sw = Stopwatch.StartNew();
            while (!cancellationToken.IsCancellationRequested)
            {
                PokemonData?[]? party = await TryReadPartyAsync();
                if (party is not null)
                {
                    for (int i = 0; i < party.Length; i++)
                    {
                        var mon = party[i]!;
                        if (mon.IsEgg && mon.Species == config.EggSpecies)
                        {
                            // Logged in full rather than just pid: whether ivs is anything other than
                            // 0x00000000 here is the direct answer to whether the game has already
                            // populated IVs (and therefore the isEgg bit this event polls on) before
                            // the egg ever hatches, as opposed to leaving that word zeroed until hatch.
                            Console.WriteLine($"[TogepiEgg] Egg in party slot {i + 1}, pid={mon.PID:X8}, ivs=0x{mon.IVs:X8}.");
                            return mon.PID;
                        }
                    }
                }
                if (sw.ElapsedMilliseconds > config.EggWaitTimeoutMs)
                {
                    throw new InvalidOperationException(
                        $"No species {config.EggSpecies} egg appeared in the party {config.EggWaitTimeoutMs}ms after the '{config.ReceiveEggMacro}' macro finished. " +
                        "The macro is most likely a placeholder, out of sync with the save file, or ended before the NPC handed the egg over.");
                }
                await Task.Delay(Constant.PollIntervalMs, cancellationToken);
            }
            return 0;
        }

        private async Task SaveAndFinishAsync(PersistPokemonData persistPokemon)
        {
            Console.WriteLine($"[TogepiEgg] Shiny {persistPokemon.PokemonData.Name}. Saving.");
            await shinyAlertService.NotifyTargetFoundAsync(persistPokemon, "hatched");

            bot.RunMacroString(MacroRegistry.Instance.GetMacro(flavor, config.SaveMacro));
            await bot.WaitForMacroAsync();
            await persistenceService.AddCaughtPokemonAsync(persistPokemon);
            await Task.Delay(1000);
            await bot.FlushSave();
            await Task.Delay(config.PostSaveDelayMs);
        }

        private async Task SoftResetAsync(CancellationToken cancellationToken)
        {
            SetState(EggHuntState.SoftResetting);
            bot.RunMacroString(MacroRegistry.Instance.GetMacro(flavor, config.SoftResetMacro));
            await bot.WaitForMacroAsync(cancellationToken);
            await Task.Delay(config.SoftResetDelayMs, cancellationToken);

            bot.RunMacroString(MacroRegistry.Instance.GetMacro(flavor, config.ContinueMacro));
            await bot.WaitForMacroAsync(cancellationToken);
            await Task.Delay(config.PostContinueDelayMs, cancellationToken);
            await Overworld.WaitForReadyAsync(bot.State, cancellationToken);
        }

        // -- Helpers ----------------------------------------------------------------------------

        /// <summary>
        /// Builds the repeat macro that walks the corridor end to end. Same shape as
        /// RunEncounterEvent's: the M command holds a direction until the coordinate lands, so one
        /// entry per end is a complete round trip.
        /// </summary>
        private string BuildPaceMacro()
        {
            if (config.IsVerticalCorridor)
            {
                // Up decreases y, matching RunEncounterEvent.
                return $"M.Up.{config.HatchLow}.{addresses.TrainerY};M.Down.{config.HatchHigh}.{addresses.TrainerY}";
            }
            return $"M.Left.{config.HatchLow}.{addresses.TrainerX};M.Right.{config.HatchHigh}.{addresses.TrainerX}";
        }

        /// <summary>
        /// Reads the field party through the emulator's validated-party command
        /// (<see cref="PokemonStateReader.GetPartyState"/>).
        ///
        /// This is the reliable primitive and the reason to prefer it over reading the bytes here: the
        /// emulator re-follows the anchor and re-reads every slot *once per frame* until the whole
        /// party validates, all inside the emulator, with no round trip per attempt. A hand-rolled read
        /// over HTTP can only retry at network cadence, so it gives up during exactly the windows that
        /// matter here -- an overworld reload after the hatch cutscene, where the party is unreadable
        /// for far longer than a couple of frames.
        ///
        /// Returns null when the party genuinely could not be read, so callers can re-poll.
        /// </summary>
        private async Task<PokemonData?[]?> TryReadPartyAsync()
        {
            try
            {
                PokemonState state = await reader.GetPartyState(bot);
                var party = new PokemonData?[state.PartySize];
                for (int i = 0; i < state.PartySize; i++)
                {
                    party[i] = state.PartyPokemon[i];
                }
                return party;
            }
            catch (PartyReadUnavailableException ex)
            {
                // Expected while the party is mid-write or the field is still loading -- the caller
                // polls. Also what an egg would produce if ApiHawk's PokemonValidator rejects egg
                // battle stats; if that turns out to be the case the reason text names the failing
                // check, and the fix is an egg-aware branch in PokemonValidator rather than going back
                // to reading the bytes here.
                Console.WriteLine($"[TogepiEgg] Party not readable yet: {ex.Message}");
                return null;
            }
        }

        private void SetState(EggHuntState next)
        {
            state = next;
            Console.WriteLine($"[TogepiEgg] State: {next}");
        }

        /// <summary>
        /// The dashboard decides which actions a slot offers from the top of this stack, so a running
        /// egg hunt has to leave something there that is neither Complete nor Waiting.
        /// </summary>
        private static void PushRunningState(GameState gameState)
        {
            if (gameState.State.Count == 0)
            {
                gameState.State.Push(EventState.Running);
            }
            else
            {
                gameState.SwapState(EventState.Running);
            }
        }

        private static void CompleteState(GameState gameState)
        {
            if (gameState.State.Count == 0)
            {
                gameState.State.Push(EventState.Complete);
            }
            else
            {
                gameState.SwapState(EventState.Complete);
            }
        }

        private async Task RefreshDashboardAsync()
        {
            await botService.RefreshDashboardState();
        }
    }
}
