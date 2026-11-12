using ShinyBotHunter.Bot;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Util;
using System.Diagnostics;

namespace ShinyBotHunter.BotEvents
{
    // The static encounter hunt (docs/specs/sinjoh-static.md, docs/research/sinjoh-static.md): save once at Run, then each
    // attempt replays the walk Run made to the circle. A step that goes wrong stops the hunt with its sentence.
    public class HgssStaticEncounterEvent : AbstractBotEvent<bool>
    {
        internal const string DidNotRespond = "The circle did not respond. This save has already received this Pokémon, or this Arceus has been used.";

        // How long the snapshot may stay the same during the walk: it stays FieldFree throughout, which takes 5.4 to 6.9 s.
        private static readonly TimeSpan WalkStale = TimeSpan.FromSeconds(15);

        // How long the snapshot may stay the same after a menu answer, and in a field script (the navigator's value).
        private static readonly TimeSpan MenuStale = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ScriptStale = TimeSpan.FromSeconds(20);

        // The field reads 6-frame holds; one-frame taps were missed in the spike (FR-14).
        private const string HoldA = "H.A.6;W.20";
        private const string HoldB = "H.B.6;W.20";

        // Safety margin after a save so the flush lands before anything else happens; the Primo hunt's value.
        private static readonly TimeSpan PostSaveDelay = TimeSpan.FromSeconds(10);

        private readonly IBot bot;
        private readonly StaticEncounterConfig config;
        private readonly StaticEncounterConfig.Entry encounter;
        private readonly string walkMacro;
        private readonly (short X, short Y, Direction Facing) start;
        private readonly PokemonStateReader reader;
        private readonly PokemonPersistenceService persistenceService;
        private readonly ShinyAlertService shinyAlertService;
        private readonly BotService botService;

        public HgssStaticEncounterEvent(IBot bot, StaticEncounterConfig config, StaticEncounterConfig.Entry encounter, string walkMacro,
            short x, short y, Direction facing, IServiceProvider provider, BotService botService)
        {
            this.bot = bot;
            this.config = config;
            this.encounter = encounter;
            this.walkMacro = walkMacro;
            this.start = (x, y, facing);
            this.botService = botService;
            this.reader = provider.GetRequiredService<PokemonStateReader>();
            this.persistenceService = provider.GetRequiredService<PokemonPersistenceService>();
            this.shinyAlertService = provider.GetRequiredService<ShinyAlertService>();
        }

        public override async Task<bool> ExecuteWithResultAsync(GameState gameState, CancellationToken cancellationToken)
        {
            PushRunningState(gameState);
            await botService.RefreshDashboardState();

            bool complete = false;
            try
            {
                complete = await HuntAsync(gameState, bot, config, encounter, walkMacro, start,
                    () => botService.StaticEncounterRefusalAsync(bot, encounter, cancellationToken),
                    () => reader.GetPartyState(bot),
                    persistenceService.AddEncounterAsync,
                    p => shinyAlertService.NotifyTargetFoundAsync(p, "received"),
                    persistenceService.AddCaughtPokemonAsync,
                    PostSaveDelay, cancellationToken) is null;
                return complete;
            }
            finally
            {
                if (!complete)
                {
                    // Stopped, cancelled or thrown out of: nothing is driving the bot any more, so nothing stays held.
                    bot.ClearMacro();
                }
            }
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

        // Clears the macro, logs the sentence and leaves the slot idle, so Run is offered again and checks again. Not Waiting:
        // Resume would run the save at Run again from wherever the stop left the game.
        private static string Stop(GameState state, IBot bot, string sentence)
        {
            bot.ClearMacro();
            Console.WriteLine($"[Static] stopped: {sentence}");
            state.SwapState(EventState.None);
            return sentence;
        }

        // -- Steps, internal for the tests --------------------------------------------------------

        // The run: the save at Run, then the attempts. Null when the hunt completed, otherwise the sentence it stopped with.
        // The delegates are the boundaries a test cannot run (AGENTS rule 2): Run's live checks, the emulator's party read,
        // the encounter log, the alert and the caught record.
        internal static async Task<string?> HuntAsync(GameState state, IBot bot, StaticEncounterConfig config, StaticEncounterConfig.Entry encounter,
            string walkMacro, (short X, short Y, Direction Facing) start, Func<Task<string?>> checks, Func<Task<PokemonState>> readParty,
            Func<PersistPokemonData, Task> addEncounter, Func<PersistPokemonData, Task> alert, Func<PersistPokemonData, Task> addCaught,
            TimeSpan postSave, CancellationToken ct)
        {
            var addresses = (HgssPointerService)bot.Addresses;
            Func<string, Task> press = async macro =>
            {
                bot.RunMacroString(macro);
                await bot.WaitForMacroAsync(ct);
            };
            // The one place the macro is printed (FR-12).
            Console.WriteLine($"[Static] {encounter.Name}: species {encounter.Species}, circle ({encounter.Spot[0]}, {encounter.Spot[1]}) on map {encounter.MapId}; " +
                $"walk from ({start.X}, {start.Y}) facing {start.Facing.ToString().ToLowerInvariant()}: {walkMacro}");
            try
            {
                // Once, after Run's checks, so every reset returns to the checked game (FR-3).
                bot.RunMacroString(MacroRegistry.Instance.GetMacro(bot.GameFlavor, "SaveGame"));
                await bot.WaitForMacroAsync(ct);
                await bot.FlushSave(ct);
                await Task.Delay(postSave, ct);
                await Overworld.WaitForReadyAsync(bot.State, ct);
                Console.WriteLine("[Static] saved at Run");

                int attempt = 0;
                while (true)
                {
                    attempt++;
                    Console.WriteLine($"[Static] attempt {attempt}");
                    if (await WalkAsync(bot, encounter, walkMacro, WalkStale, ct) is { } walkStop)
                    {
                        return Stop(state, bot, walkStop);
                    }
                    if (await PromptAsync(bot.State, addresses, press, ScriptStale, MenuStale, ct) is { } promptStop)
                    {
                        return Stop(state, bot, promptStop);
                    }
                    if (await CutsceneAsync(bot.State, bot, addresses, press, TimeSpan.FromMilliseconds(config.CutsceneStaleMs), MenuStale, ct) is { } cutsceneStop)
                    {
                        return Stop(state, bot, cutsceneStop);
                    }
                    Stopwatch after = Stopwatch.StartNew();
                    await NavigatorEvent.PressThroughScriptAsync(bot.State, addresses, press, "the scripts after the hand-over", ScriptStale, ct,
                        pressA: HoldA, pressB: HoldB);
                    Console.WriteLine($"[Static] after the hand-over {after.ElapsedMilliseconds} ms, field free on map {await bot.Overworld.GetMapIdAsync(ct)}");

                    var (mon, summaryStop) = await SummaryAsync(bot.State, addresses, press, encounter, readParty, MenuStale, ct);
                    if (summaryStop is not null)
                    {
                        return Stop(state, bot, summaryStop);
                    }
                    var persist = new PersistPokemonData
                    {
                        PokemonData = mon!,
                        DateTime = DateTime.UtcNow,
                        Game = bot.GameFlavor,
                        Phase = state.Milestone,
                        GameFileName = bot.GameFileName,
                    };
                    await addEncounter(persist);
                    Console.WriteLine($"[Static] attempt {attempt}: {encounter.Name} species {mon!.Species}, pid {mon.PID:X8}, shinyValue {mon.ShinyValue}, shiny {mon.IsShiny}");

                    if (mon.IsShiny)
                    {
                        await FinishAsync(bot, addresses, press, persist, alert, addCaught, MenuStale, postSave, ct);
                        CompleteState(state);
                        Console.WriteLine($"[Static] Shiny {encounter.Name}. Saved.");
                        return null;
                    }
                    if (await ResetAsync(bot, config, start, checks, ct) is { } resetStop)
                    {
                        return Stop(state, bot, resetStop);
                    }
                }
            }
            catch (GameStateTimeoutException ex)
            {
                // Its message already names what was awaited and the state that stayed.
                return Stop(state, bot, ex.Message);
            }
        }

        // Replays Run's walk, raced against the circle's script starting, which is the arrival (FR-5). The gate is the free
        // field, seen after the save or the reset. The event never searches: the macro is the parameter (FR-4).
        internal static async Task<string?> WalkAsync(IBot bot, StaticEncounterConfig.Entry encounter, string walkMacro, TimeSpan stale, CancellationToken ct)
        {
            Stopwatch walk = Stopwatch.StartNew();
            bot.RunMacroString(walkMacro);
            GameSnapshot started;
            try
            {
                started = await GameStateWait.UntilAsync(bot.State, "the circle to start its script",
                    s => s.Screen == GameScreen.FieldTask && s.Script is not null, stale, ct);
            }
            catch (GameStateTimeoutException)
            {
                ushort map = await bot.Overworld.GetMapIdAsync(ct);
                (short x, short y) = await bot.Overworld.GetPositionAsync(ct);
                if (map == encounter.MapId && x == encounter.Spot[0] && y == encounter.Spot[1])
                {
                    return DidNotRespond;
                }
                Direction facing = await bot.Overworld.GetFacingAsync(ct);
                return $"The walk to {encounter.Name} ended on map {map}, tile ({x}, {y}), facing {facing.ToString().ToLowerInvariant()}; " +
                    $"the circle is at ({encounter.Spot[0]}, {encounter.Spot[1]}).";
            }
            // The step that started the script was the macro's last, so nothing of it is left to wait for.
            bot.ClearMacro();
            Console.WriteLine($"[Static] walk {walk.ElapsedMilliseconds} ms, script {started.Script}");
            return null;
        }

        // The circle's Yes/No (FR-6). The presser presses nothing at the menu, because until is checked first; its only presses
        // are A at a text arrow, for the refusal's text, each confirmed by the printer leaving the arrow. The Yes is one press,
        // not repeated: a dropped hold is the stale stop naming the menu.
        internal static async Task<string?> PromptAsync(IGameStateReader game, HgssPointerService addresses, Func<string, Task> press,
            TimeSpan scriptStale, TimeSpan menuStale, CancellationToken ct)
        {
            GameSnapshot ended = await NavigatorEvent.PressThroughScriptAsync(game, addresses, press, "the circle script", scriptStale, ct,
                s => s.Native == addresses.MenuChoiceNative || s.Screen == GameScreen.FieldFree, HoldA, HoldB);
            if (ended.Screen == GameScreen.FieldFree)
            {
                return DidNotRespond;
            }
            await press(GameStateWait.MenuWait + HoldA);
            await GameStateWait.UntilAsync(game, "the circle menu to take the Yes", s => s.Native != addresses.MenuChoiceNative, menuStale, ct);
            Console.WriteLine("[Static] Yes");
            return null;
        }

        // The party count, [FieldSystem] - 0x23F44: the count only, never a slot, so nothing of the Pokémon is read here.
        internal static async Task<int> ReadPartyCountAsync(IMemoryClient memory, HgssPointerService addresses, CancellationToken ct)
        {
            ChainBlockResult read = (await memory.ReadChainBlocksAsync(
                new[] { new ChainBlockRequest(addresses.Anchor, new[] { addresses.PartySize }, 1) }, ct: ct))[0];
            if (read.Status != ChainBlockStatus.Ok)
                throw new InvalidOperationException($"[Static] the party count read failed: {read.Status} at hop {read.Hop}");
            return read.Bytes[0];
        }

        // From the Yes to the nickname question, then its No (FR-7, FR-8). The presser's only press here is A at the printer's
        // arrow, confirmed by the printer leaving it. About 127 s of the cutscene shows no native; that is pressed through by
        // nothing and is a stall only if the whole snapshot stays the same for cutsceneStale. The No is one press, not repeated.
        internal static async Task<string?> CutsceneAsync(IGameStateReader game, IMemoryClient memory, HgssPointerService addresses, Func<string, Task> press,
            TimeSpan cutsceneStale, TimeSpan menuStale, CancellationToken ct)
        {
            Stopwatch cutscene = Stopwatch.StartNew();
            await NavigatorEvent.PressThroughScriptAsync(game, addresses, press, "the cutscene to reach the nickname question", cutsceneStale, ct,
                s => s.Native == addresses.MenuChoiceNative, HoldA, HoldB);
            int count = await ReadPartyCountAsync(memory, addresses, ct);
            if (count != 2)
            {
                return $"At the nickname question the party holds {count}, not 2.";
            }
            Console.WriteLine($"[Static] cutscene {cutscene.ElapsedMilliseconds} ms (Yes to party count 2)");
            await press(GameStateWait.MenuWait + HoldB);
            await GameStateWait.UntilAsync(game, "the nickname menu to take the No", s => s.Native != addresses.MenuChoiceNative, menuStale, ct);
            Console.WriteLine("[Static] No to the nickname");
            return null;
        }

        // X, POKéMON, slot 2, SUMMARY, from the free field the scripts after the hand-over ended on (FR-9). Each press is
        // gated on the state the one before it confirmed and is sent once; the party menu and the summary are told apart by
        // the app's init function (FR-13). Slot 2 is read only once the summary is on screen, as a player would see it.
        internal static async Task<(PokemonData? Mon, string? Stop)> SummaryAsync(IGameStateReader game, HgssPointerService addresses, Func<string, Task> press,
            StaticEncounterConfig.Entry encounter, Func<Task<PokemonState>> readParty, TimeSpan stale, CancellationToken ct)
        {
            Stopwatch route = Stopwatch.StartNew();
            await press("H.X.6");
            await GameStateWait.UntilAsync(game, "the start menu after X", s => s.Screen == GameScreen.FieldTask, stale, ct);

            await press(Touch(encounter.PokemonTouch));
            await GameStateWait.UntilAsync(game, "the party list after POKéMON",
                s => s.Screen == GameScreen.OtherApp && s.AppInit == addresses.PartyMenuInit && s.AppState == addresses.PartyMenuList, stale, ct);

            await press(Touch(encounter.SlotTouch));
            await GameStateWait.UntilAsync(game, "slot 2's context menu",
                s => s.AppInit == addresses.PartyMenuInit && s.AppState == addresses.PartyMenuContextMenu && s.PartyMonIndex == 1, stale, ct);

            await press(Touch(encounter.SummaryTouch));
            await GameStateWait.UntilAsync(game, "slot 2's summary after SUMMARY", s => s.AppInit == addresses.SummaryInit, stale, ct);
            Console.WriteLine($"[Static] summary on screen {route.ElapsedMilliseconds} ms");

            PokemonData mon = (await readParty()).PartyPokemon[1]!;
            if (mon.Species != encounter.Species)
            {
                return (null, $"Party slot 2 is species {mon.Species}, not {encounter.Name}'s species {encounter.Species}.");
            }
            return (mon, null);
        }

        // After the menu input-ready gap; a 3-frame touch (FR-14).
        private static string Touch(int[] xy) => $"{GameStateWait.MenuWait}S.{xy[0]}.{xy[1]}.3";

        // Back to the save from any screen, the summary included, then Run's checks and the tile and facing the walk was made
        // from, so the next attempt replays the walk from where it fits (FR-11). Null when the game is back where it was saved.
        internal static async Task<string?> ResetAsync(IBot bot, StaticEncounterConfig config, (short X, short Y, Direction Facing) start,
            Func<Task<string?>> checks, CancellationToken ct)
        {
            Stopwatch reset = Stopwatch.StartNew();
            bot.RunMacroString(MacroRegistry.Instance.GetMacro(bot.GameFlavor, config.SoftResetMacro));
            await bot.WaitForMacroAsync(ct);
            await Task.Delay(config.SoftResetDelayMs, ct);
            bot.RunMacroString(MacroRegistry.Instance.GetMacro(bot.GameFlavor, config.ContinueMacro));
            await bot.WaitForMacroAsync(ct);
            await Task.Delay(config.PostContinueDelayMs, ct);
            await Overworld.WaitForReadyAsync(bot.State, ct);
            Console.WriteLine($"[Static] reset {reset.ElapsedMilliseconds} ms to the free field");

            if (await checks() is { } refusal)
            {
                return refusal;
            }
            (short x, short y) = await bot.Overworld.GetPositionAsync(ct);
            Direction facing = await bot.Overworld.GetFacingAsync(ct);
            if (x != start.X || y != start.Y || facing != start.Facing)
            {
                return $"After the soft reset the player is at tile ({x}, {y}) facing {facing.ToString().ToLowerInvariant()}, " +
                    $"not ({start.X}, {start.Y}) facing {start.Facing.ToString().ToLowerInvariant()}, where the walk was made from.";
            }
            return null;
        }

        // The shiny (FR-11): the alert first, then B to the party list and B to the field, each confirmed, because the save
        // macro starts from the field; then the save, the caught record and the flush, uncancellable as the Primo hunt's are.
        internal static async Task FinishAsync(IBot bot, HgssPointerService addresses, Func<string, Task> press, PersistPokemonData persist,
            Func<PersistPokemonData, Task> alert, Func<PersistPokemonData, Task> addCaught, TimeSpan stale, TimeSpan postSave, CancellationToken ct)
        {
            await alert(persist);

            await press(GameStateWait.MenuWait + "H.B.6");
            await GameStateWait.UntilAsync(bot.State, "the party list after B from the summary",
                s => s.AppInit == addresses.PartyMenuInit && s.AppState == addresses.PartyMenuList, stale, ct);
            await press(GameStateWait.MenuWait + "H.B.6");
            await GameStateWait.UntilAsync(bot.State, "the field free after B from the party list", s => s.Screen == GameScreen.FieldFree, stale, ct);

            bot.RunMacroString(MacroRegistry.Instance.GetMacro(bot.GameFlavor, "SaveGame"));
            await bot.WaitForMacroAsync(CancellationToken.None);
            await addCaught(persist);
            await bot.FlushSave(CancellationToken.None);
            await Task.Delay(postSave, CancellationToken.None);
        }
    }
}
