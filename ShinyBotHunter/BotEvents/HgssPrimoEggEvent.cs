using ShinyBotHunter.Bot;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Util;
using System.Diagnostics;

namespace ShinyBotHunter.BotEvents
{
    /// <summary>
    /// Soft-reset hunt for a shiny from Primo's egg in HGSS (docs/specs/primo-egg-hunt.md,
    /// docs/research/primo-slugma-egg.md): the password from the trainer ID, Primo's two easy-chat prompts, the walk
    /// to the pacing line, the shared hatch step, then save on a shiny or soft reset and go again.
    /// </summary>
    public class HgssPrimoEggEvent : AbstractBotEvent<PokemonData?>
    {
        // ScriptEnvironment's special variables from 0x8000; 0x800C, the script's result, is index 12.
        private const int SpecialVarCount = 14;
        private const int ResultVar = 12;

        // The navigator's values for a field script: how long its snapshot may stay the same, and the start press's confirm.
        private static readonly TimeSpan ScriptStale = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan ScriptPressConfirm = TimeSpan.FromSeconds(1);
        private const int ScriptPressAttempts = 3;
        private const string StartScriptMacro = "T.A;W.20";

        // How long the sub-app's overlay may stay the same while the easy-chat app opens (R36).
        private static readonly TimeSpan EasyChatOpenStale = TimeSpan.FromSeconds(10);

        // How long a route leg may stay on one tile; the spike's rule, which passed the door's warp fade.
        private static readonly TimeSpan LegStall = TimeSpan.FromMilliseconds(1500);

        // Safety margin after a save so the flush lands before anything else happens; EggHuntConfig's default.
        private const int PostSaveDelayMs = 10000;

        private readonly IBot bot;
        private readonly string phase;
        private readonly PrimoEggConfig config;
        private readonly HgssPointerService addresses;
        private readonly PokemonStateReader reader;
        private readonly PokemonPersistenceService persistenceService;
        private readonly ShinyAlertService shinyAlertService;
        private readonly BotService botService;
        // One per run, so its press cache lasts across cycles (FR-7); touched only by this event's own task.
        private readonly HgssEasyChatInput easyChat;
        private readonly HgssEggHatch hatch;

        public HgssPrimoEggEvent(IBot bot, string phase, PrimoEggConfig config, IServiceProvider provider, BotService botService)
        {
            this.bot = bot;
            this.phase = phase;
            this.config = config;
            this.botService = botService;
            this.addresses = (HgssPointerService)bot.Addresses;
            this.reader = provider.GetRequiredService<PokemonStateReader>();
            this.persistenceService = provider.GetRequiredService<PokemonPersistenceService>();
            this.shinyAlertService = provider.GetRequiredService<ShinyAlertService>();
            this.easyChat = new HgssEasyChatInput(bot, bot, addresses);
            this.hatch = new HgssEggHatch(bot, addresses, reader, TimeSpan.FromMilliseconds(config.HatchStaleMs));
        }

        public override async Task<PokemonData?> ExecuteWithResultAsync(GameState gameState, CancellationToken cancellationToken)
        {
            PushRunningState(gameState);
            await botService.RefreshDashboardState();

            bool complete = false;
            try
            {
                await Overworld.WaitForReadyAsync(bot.State, cancellationToken);
                ushort trainerId = await ReadTrainerIdAsync(bot, addresses, cancellationToken);
                ushort[] password = PrimoPassword.Encode(trainerId, config.Egg);
                Console.WriteLine($"[PrimoEgg] trainer ID {trainerId}, password {password[0]} {password[1]} / {password[2]} {password[3]}");

                while (!cancellationToken.IsCancellationRequested)
                {
                    uint eggPid = await GetEggAsync(password, cancellationToken);
                    await WalkToLineAsync(cancellationToken);
                    await hatch.PaceUntilHatchAsync(BuildPaceMacro(), cancellationToken);
                    PokemonData hatched = await hatch.HatchAsync(eggPid, cancellationToken);

                    var persistPokemon = new PersistPokemonData()
                    {
                        PokemonData = hatched,
                        DateTime = DateTime.UtcNow,
                        Game = bot.GameFlavor,
                        Phase = phase,
                        GameFileName = bot.GameFileName,
                    };
                    await persistenceService.AddEncounterAsync(persistPokemon);
                    Console.WriteLine($"[PrimoEgg] Hatched {hatched.Name}, shinyValue={hatched.ShinyValue}, shiny={hatched.IsShiny}");

                    if (hatched.IsShiny)
                    {
                        await SaveAndFinishAsync(persistPokemon);
                        complete = true;
                        CompleteState(gameState);
                        await botService.RefreshDashboardState();
                        return hatched;
                    }

                    await SoftResetAsync(cancellationToken);
                }
            }
            finally
            {
                if (!complete)
                {
                    // Cancelled or thrown out of: nothing is driving the bot any more, so nothing stays held.
                    bot.ClearMacro();
                }
            }

            return null;
        }

        // -- The cycle ------------------------------------------------------------------------------

        // Primo's conversation, from the first A to the egg in the party (FR-5). Returns the egg's PID.
        private async Task<uint> GetEggAsync(ushort[] password, CancellationToken ct)
        {
            Func<string, Task> press = async macro =>
            {
                bot.RunMacroString(macro);
                await bot.WaitForMacroAsync(ct);
            };
            await GameStateWait.PressUntilAsync(bot.State, () => press(StartScriptMacro), "Primo's script to start",
                s => s.Screen == GameScreen.FieldTask && s.Script == addresses.PrimoScript, ScriptPressConfirm, ScriptPressAttempts, ct);

            await PromptAsync(1, "Primo's script", password, press, ct);
            ushort[] vars = await PromptAsync(2, "Primo's script after prompt 1", password, press, ct);
            string? refusal = Refusal(vars[ResultVar], config.Egg);
            if (refusal is not null)
                throw new InvalidOperationException($"Primo gave no egg: {refusal}.");
            Console.WriteLine($"[PrimoEgg] outcome {vars[ResultVar]} (party count)");

            await NavigatorEvent.PressThroughScriptAsync(bot.State, addresses, press, "Primo's script after the egg", ScriptStale, ct);

            PokemonState party = await reader.GetPartyState(bot);
            for (int i = 0; i < party.PartySize; i++)
            {
                PokemonData mon = party.PartyPokemon[i]!;
                if (!mon.IsEgg || mon.Species != config.EggSpecies) continue;
                Console.WriteLine($"[PrimoEgg] egg in slot {i + 1}, pid={mon.PID:X8}");
                return mon.PID;
            }
            string species = string.Join(", ", Enumerable.Range(0, party.PartySize).Select(i => party.PartyPokemon[i]!.Species));
            throw new InvalidOperationException($"No species {config.EggSpecies} egg in the party after Primo's script; the party holds species {species}.");
        }

        // Presses through Primo's text to the easy-chat app, enters the prompt's two words, confirms, and stops at the next
        // message. Returns the special variables, once the prompt's two words are checked.
        private async Task<ushort[]> PromptAsync(int prompt, string waitingFor, ushort[] password, Func<string, Task> press, CancellationToken ct)
        {
            await NavigatorEvent.PressThroughScriptAsync(bot.State, addresses, press, waitingFor, ScriptStale, ct, EasyChatOpened);
            await WaitForEasyChatAsync(ct);
            int first = 2 * (prompt - 1);
            await easyChat.EnterAsync(0, password[first], ct);
            await easyChat.EnterAsync(1, password[first + 1], ct);
            await easyChat.ConfirmAsync(ct);
            await NavigatorEvent.PressThroughScriptAsync(bot.State, addresses, press, $"Primo's script after prompt {prompt}", ScriptStale, ct, AtMessage(addresses));
            ushort[] vars = await ReadSpecialVarsAsync(bot, addresses, ct);
            CheckWords(vars, prompt, password);
            Console.WriteLine($"[PrimoEgg] after prompt {prompt}: 0x{0x8000 + first:X4}={vars[first]} 0x{0x8001 + first:X4}={vars[first + 1]}");
            return vars;
        }

        // Polls the sub-app's overlay id until it is the easy-chat app's; throws naming what it saw when that stays the same (R36).
        private async Task WaitForEasyChatAsync(CancellationToken ct)
        {
            string? last = null;
            Stopwatch unchanged = Stopwatch.StartNew();
            while (true)
            {
                ChainBlockResult read = (await bot.ReadChainBlocksAsync(new[]
                {
                    new ChainBlockRequest(addresses.Anchor, new[] { addresses.AppSlotsOffset, addresses.SubAppOffset, addresses.AppOverlayIdOffset }, 4),
                }, ct: ct))[0];
                if (read.Status == ChainBlockStatus.Ok && BitConverter.ToUInt32(read.Bytes, 0) == addresses.EasyChatOverlayId) return;
                string seen = read.Status == ChainBlockStatus.Ok ? $"overlay {BitConverter.ToUInt32(read.Bytes, 0)}" : $"{read.Status} at hop {read.Hop}";
                if (seen != last)
                {
                    last = seen;
                    unchanged.Restart();
                }
                else if (unchanged.Elapsed >= EasyChatOpenStale)
                {
                    throw new InvalidOperationException(
                        $"Waited for the easy-chat app (overlay {addresses.EasyChatOverlayId}) to open; the sub-app stayed {seen} for {EasyChatOpenStale.TotalSeconds:0} s");
                }
                await Task.Delay(Constant.PollIntervalMs, ct);
            }
        }

        // From Primo's tile to the pacing line, one confirmed leg at a time (FR-8).
        private async Task WalkToLineAsync(CancellationToken ct)
        {
            await Overworld.WaitForReadyAsync(bot.State, ct);
            foreach (var leg in config.Legs)
            {
                await WalkLegAsync(bot.Overworld, bot, addresses, leg, LegStall, ct);
            }
        }

        private async Task SaveAndFinishAsync(PersistPokemonData persistPokemon)
        {
            Console.WriteLine($"[PrimoEgg] Shiny {persistPokemon.PokemonData.Name}. Saving.");
            await shinyAlertService.NotifyTargetFoundAsync(persistPokemon, "hatched");

            bot.RunMacroString(MacroRegistry.Instance.GetMacro(bot.GameFlavor, config.SaveMacro));
            await bot.WaitForMacroAsync();
            await persistenceService.AddCaughtPokemonAsync(persistPokemon);
            await bot.FlushSave();
            await Task.Delay(PostSaveDelayMs);
        }

        // Back to the save facing Primo; anywhere else stops the hunt with where the player is (FR-11).
        private async Task SoftResetAsync(CancellationToken ct)
        {
            bot.RunMacroString(MacroRegistry.Instance.GetMacro(bot.GameFlavor, config.SoftResetMacro));
            await bot.WaitForMacroAsync(ct);
            await Task.Delay(config.SoftResetDelayMs, ct);

            bot.RunMacroString(MacroRegistry.Instance.GetMacro(bot.GameFlavor, config.ContinueMacro));
            await bot.WaitForMacroAsync(ct);
            await Task.Delay(config.PostContinueDelayMs, ct);
            await Overworld.WaitForReadyAsync(bot.State, ct);

            string? where = await WrongPlaceAsync(bot.Overworld, config, ct);
            if (where is not null)
            {
                throw new InvalidOperationException(
                    $"After the soft reset the player is on {where}, not facing Primo on map {config.StartMapId}, tile ({config.StartX}, {config.StartY}).");
            }
        }

        // The Togepi hunt's pacing macro over this config's line: the M command holds a direction until the coordinate lands.
        private string BuildPaceMacro()
        {
            if (config.IsVerticalCorridor)
            {
                return $"M.Up.{config.HatchLow}.{addresses.TrainerY};M.Down.{config.HatchHigh}.{addresses.TrainerY}";
            }
            return $"M.Left.{config.HatchLow}.{addresses.TrainerX};M.Right.{config.HatchHigh}.{addresses.TrainerX}";
        }

        // The dashboard decides which actions a slot offers from the top of this stack.
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

        // -- Steps, internal for the tests --------------------------------------------------------

        // The visible trainer ID, [[FieldSystem + 0x0C] + 0x84] (read table R31).
        internal static async Task<ushort> ReadTrainerIdAsync(IMemoryClient memory, HgssPointerService addresses, CancellationToken ct)
        {
            ChainBlockResult read = (await memory.ReadChainBlocksAsync(
                new[] { new ChainBlockRequest(addresses.Anchor, new[] { addresses.SaveDataOffset, addresses.TrainerIdOffset }, 2) }, ct: ct))[0];
            if (read.Status != ChainBlockStatus.Ok)
                throw new InvalidOperationException($"[PrimoEgg] the trainer ID read failed: {read.Status} at hop {read.Hop}");
            return BitConverter.ToUInt16(read.Bytes, 0);
        }

        // The script's special variables 0x8000 to 0x800D, from the running script's environment (read table R33).
        internal static async Task<ushort[]> ReadSpecialVarsAsync(IMemoryClient memory, HgssPointerService addresses, CancellationToken ct)
        {
            ChainBlockResult read = (await memory.ReadChainBlocksAsync(new[]
            {
                new ChainBlockRequest(addresses.Anchor,
                    new[] { addresses.TaskManagerOffset, addresses.ScriptEnvironmentOffset, addresses.ScriptSpecialVarsOffset }, 2 * SpecialVarCount),
            }, ct: ct))[0];
            if (read.Status != ChainBlockStatus.Ok)
                throw new InvalidOperationException($"[PrimoEgg] the script's special variables read failed: {read.Status} at hop {read.Hop}");
            ushort[] vars = new ushort[SpecialVarCount];
            for (int i = 0; i < SpecialVarCount; i++) vars[i] = BitConverter.ToUInt16(read.Bytes, 2 * i);
            return vars;
        }

        // Prompt 1 writes 0x8000 and 0x8001, prompt 2 writes 0x8002 and 0x8003; both must hold the words entered.
        internal static void CheckWords(ushort[] vars, int prompt, ushort[] password)
        {
            int first = 2 * (prompt - 1);
            if (vars[first] == password[first] && vars[first + 1] == password[first + 1]) return;
            throw new InvalidOperationException(
                $"after prompt {prompt}, 0x{0x8000 + first:X4} and 0x{0x8001 + first:X4} read {vars[first]}, {vars[first + 1]}; " +
                $"entered {password[first]}, {password[first + 1]}");
        }

        // 0x800C after the password check (scr_seq_0860_T22PC0101.s): 1 to 5 is the party count after the egg; anything
        // else is why there is no egg. The already-received branch leaves the egg type in 0x800C, so it is the config's egg.
        internal static string? Refusal(ushort result, int egg)
        {
            if (result is >= 1 and <= 5) return null;
            if (result is 255 or 0) return "Primo refused the password";
            if (result == egg) return "this save already received the egg (FLAG_GOT_SLUGMA_EGG_FROM_PRIMO is set)";
            if (result == 6) return "the party is full";
            return $"Primo's script ended with result {result}";
        }

        // Where Primo's text ends: the easy-chat app has replaced the field.
        internal static bool EasyChatOpened(GameSnapshot snapshot) => snapshot.Screen == GameScreen.OtherApp;

        // Where the text after a prompt is stopped: the next message, before any press. The variables are only certain to
        // be written once the script has moved on, and on the egg-given path 0x800C holds the party count only until
        // CloseMsg after message 18, the first message after prompt 2.
        internal static Func<GameSnapshot, bool> AtMessage(HgssPointerService addresses) => s => s.Native == addresses.PrintingNative;

        // Null on the config's start tile and facing, otherwise where the player is.
        internal static async Task<string?> WrongPlaceAsync(IOverworldReader overworld, PrimoEggConfig config, CancellationToken ct)
        {
            ushort map = await overworld.GetMapIdAsync(ct);
            (short x, short y) = await overworld.GetPositionAsync(ct);
            Direction facing = await overworld.GetFacingAsync(ct);
            if (map == config.StartMapId && x == config.StartX && y == config.StartY && facing == config.StartFacingDirection) return null;
            return $"map {map}, tile ({x}, {y}), facing {facing.ToString().ToLowerInvariant()}";
        }

        // FR-2's sentence, from the config.
        internal static string StandFacingPrimo(PrimoEggConfig config) =>
            $"Stand facing Primo in the Violet City Pokémon Center: map {config.StartMapId}, tile ({config.StartX}, {config.StartY}), " +
            $"facing {config.StartFacingDirection.ToString().ToLowerInvariant()}.";

        // One route leg as an M command, the pace macro's move: hold the direction until the coordinate reads the value.
        internal static string LegMacro((char Axis, short Value) leg, short x, short y, HgssPointerService addresses)
        {
            if (leg.Axis == 'x') return $"M.{(leg.Value < x ? "Left" : "Right")}.{leg.Value}.{addresses.TrainerX}";
            return $"M.{(leg.Value < y ? "Up" : "Down")}.{leg.Value}.{addresses.TrainerY}";
        }

        // Runs one leg and confirms it by position. Throws, after clearing the macro, when the map and tile have not
        // changed for stall.
        internal static async Task WalkLegAsync(
            IOverworldReader overworld, ICommandClient commands, HgssPointerService addresses, (char Axis, short Value) leg, TimeSpan stall,
            CancellationToken ct)
        {
            ushort map = await overworld.GetMapIdAsync(ct);
            (short x, short y) = await overworld.GetPositionAsync(ct);
            commands.RunMacroString(LegMacro(leg, x, y, addresses));
            Stopwatch still = Stopwatch.StartNew();
            while ((leg.Axis == 'x' ? x : y) != leg.Value)
            {
                if (still.Elapsed >= stall)
                {
                    commands.ClearMacro();
                    throw new InvalidOperationException($"Route leg {leg.Axis}:{leg.Value} stopped at map {map}, tile ({x}, {y})");
                }
                await Task.Delay(Constant.PollIntervalMs, ct);
                ushort nextMap = await overworld.GetMapIdAsync(ct);
                (short nextX, short nextY) = await overworld.GetPositionAsync(ct);
                if ((nextMap, nextX, nextY) != (map, x, y)) still.Restart();
                (map, x, y) = (nextMap, nextX, nextY);
            }
            await commands.WaitForMacroAsync(ct);
            Console.WriteLine($"[PrimoEgg] leg {leg.Axis}:{leg.Value} reached at map {map} ({x}, {y})");
        }
    }
}
