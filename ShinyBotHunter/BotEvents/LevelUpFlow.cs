using System.Diagnostics;
using ShinyBotHunter.Bot;
using ShinyBotHunter.Games;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.BotEvents
{
    public class LevelUpFlow
    {
        // The stale-timeout table's rows for the exp and move-learn prompts: 15 s between prompts, and a
        // press at a prompt confirmed within 1000 ms, 3 attempts.
        private static readonly TimeSpan PromptStale = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan PromptPressConfirm = TimeSpan.FromMilliseconds(1000);
        private const int PromptPressAttempts = 3;

        // The forget-move screen's slot-and-Forget pair: 1500 ms, 8 attempts (about 15 s with the macro). 1500 ms is twice
        // the 0.7 s observed from Forget to the getter leaving 23, so a pair that landed is not repeated; 8 attempts cover a
        // screen load of over 4x the old macro's 165 frames.
        private static readonly TimeSpan ForgetScreenPressConfirm = TimeSpan.FromMilliseconds(1500);
        private const int ForgetScreenPressAttempts = 8;

        // The learned messages after the forget-move screen (25 → 27 → 29 → 36, about 11 s observed) read as one unchanged
        // BattleScript 276 snapshot; 30 s is about 3x.
        private static readonly TimeSpan LearnedMessagesStale = TimeSpan.FromSeconds(30);

        // The evolution scene states' presses: 1500 ms, 6 attempts (9 s, under the 15 s stale). A press while the
        // message still prints only speeds the text, and "evolved into" prints for over 3 s.
        private static readonly TimeSpan EvolutionPressConfirm = TimeSpan.FromMilliseconds(1500);
        private const int EvolutionPressAttempts = 6;

        // How long a battle Yes/No prompt waits for its question's printer to appear: the getter state turns about 0.16 s
        // before the printer exists (probe run 20261024-042051). Past it the wait falls through, so a question that printed
        // between two polls does not hold the flow.
        private static readonly TimeSpan QuestionPrintStart = TimeSpan.FromSeconds(1);

        // The getter's prompt states (docs/specs/hgss-state-reader.md, state table). The evolution scene's states
        // share numbers with these (11, 13, 21), so every wait for one of them names the battle screens too.
        private const int StatBoxesPrompt = 11;
        private const int StatBoxesTotalsPrompt = 13;
        private const int ForgetAMovePrompt = 21;
        private const int ChooseMoveToForgetPrompt = 23;
        private const int GiveUpLearningPrompt = 34;

        // The evolution scene's waiting states, evo.state (docs/research/pointer-discovery.md, "Post-evolution move learning").
        private const int EvolutionWhatPrompt = 3;
        private const int EvolvedPrompt = 12;
        private const int WantsToLearnPrompt = 15;
        private const int KnowsFourMovesPrompt = 17;
        private const int EvolutionForgetAMovePrompt = 21;
        private const int EvolutionForgetMoveList = 23;
        private const int PoofPrompt = 26;
        private const int ForgotPrompt = 28;
        private const int AndPrompt = 30;
        private const int EvolutionLearnedPrompt = 37;
        private const int StopTeachingPrompt = 35;
        private const int DidNotLearnPrompt = 36;

        // The summary app's states at evo.state 23, sum.proc (pointer-discovery.md, "The forget-move list (yes path)").
        private const int SummaryMoveList = 8;
        private const int SummaryConfirm = 9;

        private readonly PokemonStateReader reader;
        private readonly PokemonMetadataService metadataService;
        private readonly DiscordBotService discordBotService;

        public LevelUpFlow(PokemonStateReader reader, PokemonMetadataService metadataService, DiscordBotService discordBotService)
        {
            this.reader = reader;
            this.metadataService = metadataService;
            this.discordBotService = discordBotService;
        }

        public async Task EvolutionInit(GameState state, CancellationToken ct)
        {
            Console.WriteLine("start evolution");
            List<WrappedEvolutionMetadata> evolutions = new List<WrappedEvolutionMetadata>();
            // Order the evolutions and wrap the necessary data. also validate dead pokemon (not currently needed, but maybe needed in the future.
            foreach(var pokemon in state.BattleState!.PartyPokemon)
            {
                if(pokemon.HP > 0 && state.PendingEvolutions.ContainsKey((pokemon.PID, pokemon.Species)))
                {
                    evolutions.Add(new WrappedEvolutionMetadata()
                    {
                        EvolutionDetails = state.PendingEvolutions[(pokemon.PID, pokemon.Species)],
                        Pokemon = pokemon,
                    });
                }
            }
            foreach(var evo in evolutions)
            {
                await EvolveAsync(state.Bot.State, Tap(state.Bot, ct), ct);

                // Evolution changes species/stats but not level or known moves, so evo.Pokemon
                // (captured before the evolution animation ran) still accurately reflects the
                // mon's level and moveset for the first move check. We deliberately do not read
                // fresh state here: if a move is about to be learned, the "trying to learn a
                // move" menu may already be up, and reads during that menu crash.
                var metadata = metadataService.GetPokemonMetadata(evo.EvolutionDetails.EvolveToId);
                var newMoves = metadata.LevelUpMoves[state.GameFlavor]
                    .Where(x => x.Level == evo.Pokemon.Level || x.Level == 0).ToList();
                newMoves.RemoveAll(m => evo.Pokemon.Moves.Any(k => k.Id == m.Id));
                newMoves.Sort();

                await ProcessEvolutionMoveLearning(state, evo.Pokemon, newMoves, ct);

                // Refresh via the real party read, not the battle-state one -- the enemy is
                // long gone by this point in the flow, so there's nothing for the battle
                // reader's enemy pointer chain to resolve to. Safe here: any move-learn
                // decision has already fully resolved back to a normal screen by this point.
                // Party only: nothing below touches the boxes, and the box dump would drag a 72KB
                // read into the evolution flow.
                var freshParty = await reader.GetPartyState(state.Bot);
                state.BattleState!.PartyPokemon = freshParty.PartyPokemon.Take(freshParty.PartySize).Select(x => x!).ToList();
            }
            await GameStateWait.UntilAsync(state.Bot.State, "the evolution app to close",
                s => s.Screen is GameScreen.FieldFree or GameScreen.FieldTask, TimeSpan.FromSeconds(30), ct);
            // The read inside the loop can come before the scene has written the species and the learned move; once
            // the app has closed, the party holds both. Ids, not names: an empty slot has no move metadata.
            var closedParty = await reader.GetPartyState(state.Bot);
            foreach (var evo in evolutions)
            {
                PokemonData evolved = closedParty.PartyPokemon.Take(closedParty.PartySize).Select(x => x!).First(x => x.PID == evo.Pokemon.PID);
                Console.WriteLine($"[LevelUp] {evolved.Name} moves after the evolution: {string.Join(", ", evolved.Moves.Select(m => m.Id))}");
            }
            Console.WriteLine("end evolution");
            state.PendingEvolutions = new Dictionary<(uint, ushort), EvolutionMetadata>();
            state.SwapState(EventState.EnemyFainted);
        }

        public async Task ProcessLevelUp(GameState state, CancellationToken ct)
        {
            var newState = await reader.GetPokemonBattleState(state.Bot, ct: ct);
            Console.WriteLine($"[LevelUp] fresh read: {(newState is null ? "null" : $"party={newState.PartyPokemon.Count}")}, prev party={state.BattleState!.PartyPokemon.Count}");
            var levelUp = GetLevelUp(state.BattleState!, newState);
            state.BattleState = newState;
            await PassStatBoxesAsync(state.Bot.State, Tap(state.Bot, ct), ct);
            if (levelUp is not null)
            {
                Console.WriteLine($"[LevelUp] {levelUp.Name} reached level {levelUp.Level}");
                var metadata = metadataService.GetPokemonMetadata(levelUp.Species);
                var newMoves = metadata.LevelUpMoves[state.GameFlavor].Where(x => x.Level == levelUp.Level).ToList();
                // A move the mon already knows produces no prompt at all in game, so it must never
                // reach the learn flow -- the macros there would run against dialogue that isn't up.
                newMoves.RemoveAll(m => levelUp.Moves.Any(k => k.Id == m.Id));
                newMoves.Sort();
                Console.WriteLine($"[LevelUp] moves to learn: {(newMoves.Count == 0 ? "none" : string.Join(", ", newMoves.Select(m => m.Name)))}");
                levelUp = await ProcessMoveLearning(state, levelUp, newMoves, ct);
                Console.WriteLine("[LevelUp] move learning done, checking evolutions");
                ProcessEvolutions(levelUp, metadata, state, state.BattleState!);
            }
            state.SwapState(EventState.EnemyFainted);
        }

        private async Task<PokemonData> ProcessMoveLearning(GameState state, PokemonData pokemon, List<PokemonLevelUpMoveMetadata> newMoves, CancellationToken ct)
        {
            // Reads are safe in the in-battle learn-move flow (unlike the evolution flow), so
            // every iteration starts from a fresh read. ApplyLearnedMove additionally records
            // each learn on the PokemonData itself -- memory may not reflect the final learn
            // until its dialogue finishes, and pokemon is the same object referenced by
            // state.BattleState.PartyPokemon, so EvolutionInit's snapshot sees it either way.
            foreach(var newMove in newMoves)
            {
                // Fresh read via the battle-state reader -- during battle the game works on the
                // battle copy of the party; the field party (GetPokemonState) is not synced
                // until the battle fully ends, so reading it here returns pre-battle data.
                var freshState = await reader.GetPokemonBattleState(state.Bot, ct: ct);
                if (freshState is not null)
                {
                    state.BattleState = freshState;
                    pokemon = state.BattleState.PartyPokemon.Where(x => x.PID == pokemon.PID).First();
                }
                // ProcessLevelUp already filtered these, but against a snapshot taken before the
                // level-up dialogue was advanced -- if the game auto-learned this move into a free
                // slot during that dialogue, only the fresh read above knows. Skipping is the whole
                // fix: an already-known move shows no prompt, so the macros below would desync us.
                if (pokemon.Moves.Any(m => m.Id == newMove.Id))
                {
                    Console.WriteLine($"{pokemon.Name} already knows {newMove.Name}, skipping learn flow");
                    continue;
                }
                bool hasFourMoves = true;
                foreach(var knownMove in pokemon.Moves)
                {
                    if(knownMove.Id == 0)
                    {
                        hasFourMoves = false;
                    }
                }
                if (hasFourMoves)
                {
                    // replace move.
                    await GameStateWait.UntilAsync(state.Bot.State, "the forget-a-move prompt", s => s.Screen is GameScreen.BattleExpPrompt or GameScreen.BattleMoveLearnPrompt && s.Prompt == ForgetAMovePrompt, PromptStale, ct);
                    Console.WriteLine($"[LevelUp] asking Discord whether {pokemon.Name} learns {newMove.Name}");
                    int moveId = await discordBotService.SendNewMoveDecisionAndWaitAsync(pokemon, newMove);
                    if(moveId == -1)
                    {
                        await DeclineMoveAsync(state.Bot.State, Tap(state.Bot, ct), ct);
                        Console.WriteLine($"[LevelUp] {pokemon.Name} will not learn {newMove.Name}");
                    }
                    else
                    {
                        await ReplaceMoveAsync(state.Bot.State, Tap(state.Bot, ct), moveId, ct);
                        Console.WriteLine($"[LevelUp] {pokemon.Name} forgot {metadataService.GetPokemonMoveMetadata(pokemon.Moves[moveId].Id).Name} and learned {newMove.Name}");
                        ApplyLearnedMove(pokemon, moveId, newMove);
                    }
                }
                else
                {
                    // The game learns the move at getter 15 and prints it at 36, and neither waits for input in battle, so
                    // nothing is pressed; WaitForLevelUpOrField is the next wait.
                    Console.WriteLine($"{pokemon.Name} learned {newMove.Name} in an open move slot");
                    ApplyLearnedMove(pokemon, FirstOpenMoveSlot(pokemon), newMove);
                }
            }
            // The moves from memory once the learns are done: the replace path has waited out its messages, and a free slot
            // is written at getter 15, before the loop's next read. Ids, not names: an empty slot has no move metadata.
            var learned = await reader.GetPokemonBattleState(state.Bot, ct: ct);
            if (learned is not null)
            {
                PokemonData read = learned.PartyPokemon.First(x => x.PID == pokemon.PID);
                Console.WriteLine($"[LevelUp] {read.Name} moves now: {string.Join(", ", read.Moves.Select(m => m.Id))}");
            }
            return pokemon;
        }

        private async Task<PokemonData> ProcessEvolutionMoveLearning(GameState state, PokemonData pokemon, List<PokemonLevelUpMoveMetadata> newMoves, CancellationToken ct)
        {
            // No party reads in here: we're inside the evolution menus, where party state is
            // not readable. ApplyLearnedMove keeps pokemon.Moves accurate across multiple moves
            // so each Discord decision shows the current moveset.
            foreach(var newMove in newMoves)
            {
                // No fresh read is possible here, so this leans on the same ApplyLearnedMove cache
                // the loop already depends on: an already-known move produces no prompt in game, and
                // running the learn macros against dialogue that isn't up would desync us.
                if (pokemon.Moves.Any(m => m.Id == newMove.Id))
                {
                    Console.WriteLine($"{pokemon.Name} already knows {newMove.Name}, skipping learn flow");
                    continue;
                }
                bool hasFourMoves = true;
                foreach(var knownMove in pokemon.Moves)
                {
                    if(knownMove.Id == 0)
                    {
                        hasFourMoves = false;
                    }
                }
                if (!hasFourMoves)
                {
                    // An open slot means the game just learns the move -- there is no "forget which?"
                    // prompt at all. Asking Discord here answered a question nobody was asked, and the
                    // replace presses below would then run against dialogue that isn't on screen. It
                    // also crashed outright: the prompt builds a button per move slot, and an empty
                    // slot has no move metadata to name it.
                    Console.WriteLine($"{pokemon.Name} learned {newMove.Name} in an open move slot");
                    await PressAtEvolutionStateAsync(state.Bot.State, Tap(state.Bot, ct), "T.A", EvolutionLearnedPrompt, ct);
                    ApplyLearnedMove(pokemon, FirstOpenMoveSlot(pokemon), newMove);
                    continue;
                }
                await ReachEvolutionForgetPromptAsync(state.Bot.State, Tap(state.Bot, ct), ct);
                Console.WriteLine($"[LevelUp] asking Discord whether {pokemon.Name} learns {newMove.Name}");
                int moveId = await discordBotService.SendNewMoveDecisionAndWaitAsync(pokemon, newMove);
                if(moveId == -1)
                {
                    await DeclineEvolutionMoveAsync(state.Bot.State, Tap(state.Bot, ct), ct);
                    Console.WriteLine($"[LevelUp] {pokemon.Name} will not learn {newMove.Name}");
                }
                else
                {
                    await ReplaceEvolutionMoveAsync(state.Bot.State, Tap(state.Bot, ct), moveId, ct);
                    Console.WriteLine($"[LevelUp] {pokemon.Name} forgot {metadataService.GetPokemonMoveMetadata(pokemon.Moves[moveId].Id).Name} and learned {newMove.Name}");
                    ApplyLearnedMove(pokemon, moveId, newMove);
                }
            }
            return pokemon;
        }

        // A at the stat gains, then A at the totals.
        internal static async Task PassStatBoxesAsync(IGameStateReader game, Func<string, Task> tap, CancellationToken ct)
        {
            await PressAtPromptAsync(game, tap, "T.A", StatBoxesPrompt, ct);
            await GameStateWait.UntilAsync(game, $"prompt {StatBoxesTotalsPrompt}", s => s.Screen is GameScreen.BattleExpPrompt or GameScreen.BattleMoveLearnPrompt && s.Prompt == StatBoxesTotalsPrompt, PromptStale, ct);
            await PressAtPromptAsync(game, tap, "T.A", StatBoxesTotalsPrompt, ct);
        }

        // No at "forget a move?", then yes at "give up learning it?".
        internal static async Task DeclineMoveAsync(IGameStateReader game, Func<string, Task> tap, CancellationToken ct)
        {
            await PressAtYesNoAsync(game, tap, "S.DoNotLearn", ForgetAMovePrompt, ct);
            await PressAtYesNoAsync(game, tap, "S.DoNotLearnComplete", GiveUpLearningPrompt, ct);
        }

        // The top button (Yes) at "forget a move?", then the slot and Forget as one press on the forget-move screen, then the
        // learned messages, which need no press. The screen's state cannot be read, so the pair must be safe to repeat:
        // Forget at (32,172) is inside the confirm page's Forget rect (y 152-191, x 0-207) and outside every list-page
        // hitbox, where (104,172) would hit the new move's row (pointer-discovery.md, "The forget-move screen").
        internal static async Task ReplaceMoveAsync(IGameStateReader game, Func<string, Task> tap, int slot, CancellationToken ct)
        {
            await PressAtYesNoAsync(game, tap, "S.NewMove", ForgetAMovePrompt, ct);
            await GameStateWait.UntilAsync(game, $"prompt {ChooseMoveToForgetPrompt}", s => s.Screen is GameScreen.BattleExpPrompt or GameScreen.BattleMoveLearnPrompt && s.Prompt == ChooseMoveToForgetPrompt, PromptStale, ct);
            await PressAtPromptAsync(game, tap, $"S.New{BattleOrchestratorEvent.MoveShorthands[slot]};W.20;S.32.172.5", ChooseMoveToForgetPrompt,
                ForgetScreenPressConfirm, ForgetScreenPressAttempts, ct);
            await GameStateWait.UntilAsync(game, "the learned messages", s => s is not { Screen: GameScreen.BattleScript, Script: 276 }, LearnedMessagesStale, ct);
        }

        // A at "What?", then A at "evolved into".
        internal static async Task EvolveAsync(IGameStateReader game, Func<string, Task> tap, CancellationToken ct)
        {
            await PressAtEvolutionStateAsync(game, tap, "T.A", EvolutionWhatPrompt, ct);
            await PressAtEvolutionStateAsync(game, tap, "T.A", EvolvedPrompt, ct);
        }

        // A at "wants to learn", A at "already knows four moves", then hold at "should a move be deleted?".
        internal static async Task ReachEvolutionForgetPromptAsync(IGameStateReader game, Func<string, Task> tap, CancellationToken ct)
        {
            await PressAtEvolutionStateAsync(game, tap, "T.A", WantsToLearnPrompt, ct);
            await PressAtEvolutionStateAsync(game, tap, "T.A", KnowsFourMovesPrompt, ct);
            await GameStateWait.UntilAsync(game, $"evolution state {EvolutionForgetAMovePrompt}",
                s => s.Screen == GameScreen.Evolution && s.Prompt == EvolutionForgetAMovePrompt, PromptStale, ct);
        }

        // The bottom button (keep old moves) at "should a move be deleted?", A (Yes) at "stop trying to teach?",
        // then A at "did not learn".
        internal static async Task DeclineEvolutionMoveAsync(IGameStateReader game, Func<string, Task> tap, CancellationToken ct)
        {
            await PressAtPromptAsync(game, tap, "S.125.125.5", EvolutionForgetAMovePrompt, ct);
            await GameStateWait.UntilAsync(game, $"evolution state {StopTeachingPrompt}",
                s => s.Screen == GameScreen.Evolution && s.Prompt == StopTeachingPrompt, PromptStale, ct);
            await PressAtPromptAsync(game, tap, "T.A", StopTeachingPrompt, EvolutionPressConfirm, EvolutionPressAttempts, ct);
            await PressAtEvolutionStateAsync(game, tap, "T.A", DidNotLearnPrompt, ct);
        }

        // The top button (forget a move) at "should a move be deleted?", the slot's row and Forget in the summary app,
        // then A at "Poof!", "forgot", "And..." and "learned".
        internal static async Task ReplaceEvolutionMoveAsync(IGameStateReader game, Func<string, Task> tap, int slot, CancellationToken ct)
        {
            await PressAtPromptAsync(game, tap, "S.125.75.5", EvolutionForgetAMovePrompt, ct);
            await GameStateWait.UntilAsync(game, "the forget-move list",
                s => s is { Screen: GameScreen.Evolution, Prompt: EvolutionForgetMoveList, SummaryState: SummaryMoveList }, PromptStale, ct);

            // The row touch moves the app to the confirm in the same frame.
            string row = $"S.Evo{BattleOrchestratorEvent.MoveShorthands[slot]}.5";
            int presses = 0;
            GameSnapshot left = await GameStateWait.PressUntilAsync(game, () => { presses++; return tap(row); },
                $"summary state {SummaryConfirm}", s => s is { Screen: GameScreen.Evolution, Prompt: EvolutionForgetMoveList, SummaryState: SummaryConfirm },
                PromptPressConfirm, PromptPressAttempts, ct);
            Console.WriteLine($"[LevelUp] {row} at summary state {SummaryMoveList} after {presses} press(es): {left}");

            // The app fades out and frees itself (about 0.55 s) before evo.state leaves 23, so a retry lands only on the
            // button or the fade.
            presses = 0;
            left = await GameStateWait.PressUntilAsync(game, () => { presses++; return tap("S.64.168.5"); },
                $"evolution state {EvolutionForgetMoveList} to close", s => s is { Screen: GameScreen.Evolution, Prompt: not EvolutionForgetMoveList },
                EvolutionPressConfirm, EvolutionPressAttempts, ct);
            Console.WriteLine($"[LevelUp] S.64.168.5 at summary state {SummaryConfirm} after {presses} press(es): {left}");

            await PressAtEvolutionStateAsync(game, tap, "T.A", PoofPrompt, ct);
            await PressAtEvolutionStateAsync(game, tap, "T.A", ForgotPrompt, ct);
            await PressAtEvolutionStateAsync(game, tap, "T.A", AndPrompt, ct);
            await PressAtEvolutionStateAsync(game, tap, "T.A", EvolutionLearnedPrompt, ct);
        }

        // Only the named states are pressed at; the evolving animation (state 8) never is, since B there cancels it. A message
        // state presses once, when its printer waits at the arrow, and is confirmed by the slot going NULL or the state
        // leaving; a press while the text draws would be lost. With no printer readable (evo.printer is not slot 0) it falls
        // back to the retried press. Returns once the state has left, after the 30 or 40 frames' countdown that follows the
        // slot going NULL (pointer-discovery.md, "Text printer state (2026-10-24)").
        private static async Task PressAtEvolutionStateAsync(IGameStateReader game, Func<string, Task> tap, string macro, int state, CancellationToken ct)
        {
            GameSnapshot ready = await GameStateWait.UntilAsync(game, $"evolution state {state} at the arrow",
                s => s.Screen == GameScreen.Evolution && s.Prompt == state && (s.TextPrinter is null || s.AtTextArrow()), PromptStale, ct);
            if (ready.TextPrinter is null)
            {
                await PressAtPromptAsync(game, tap, macro, state, EvolutionPressConfirm, EvolutionPressAttempts, ct);
                return;
            }
            int presses = 0;
            GameSnapshot left = await GameStateWait.PressUntilAsync(game, () => { presses++; return tap(macro); },
                $"evolution state {state}'s printer to close", s => s.Prompt != state || s.TextPrinter is null,
                PromptPressConfirm, PromptPressAttempts, ct, settleMs: 0);
            Console.WriteLine($"[LevelUp] {macro} at prompt {state} (printer {ready.TextPrinter}) after {presses} press(es): {left}");
            await GameStateWait.UntilAsync(game, $"evolution state {state} to end", s => s.Prompt != state, PromptStale, ct);
        }

        private static Task PressAtPromptAsync(IGameStateReader game, Func<string, Task> tap, string macro, int prompt, CancellationToken ct)
            => PressAtPromptAsync(game, tap, macro, prompt, PromptPressConfirm, PromptPressAttempts, ct);

        // The battle's Yes/No menus (21, 34) are built only once the question above them has printed: the player
        // controller's Yes/No task (ov12_0225FA44) prints it, waits on TextPrinterCheckActive, then changes the menu. A touch
        // while it draws only speeds the text (lost at both prompts in the probe run 20261024-042051), so the press waits
        // for the printer to be seen and then to clear, and needs no settle; the retry stays as the safety net.
        private static async Task PressAtYesNoAsync(IGameStateReader game, Func<string, Task> tap, string macro, int prompt, CancellationToken ct)
        {
            Stopwatch? atPrompt = null;
            await GameStateWait.UntilAsync(game, $"prompt {prompt}'s question to start printing", s =>
            {
                if (s.Screen != GameScreen.BattleMoveLearnPrompt || s.Prompt != prompt) return false;
                atPrompt ??= Stopwatch.StartNew();
                return s.TextPrinter is not null || atPrompt.Elapsed >= QuestionPrintStart;
            }, PromptStale, ct);
            await GameStateWait.UntilAsync(game, $"prompt {prompt}'s question to print",
                s => s.Screen == GameScreen.BattleMoveLearnPrompt && s.Prompt == prompt && s.TextPrinter is null, PromptStale, ct);
            await PressAtPromptAsync(game, tap, macro, prompt, PromptPressConfirm, PromptPressAttempts, ct, settleMs: 0);
        }

        private static async Task PressAtPromptAsync(
            IGameStateReader game, Func<string, Task> tap, string macro, int prompt, TimeSpan confirm, int attempts, CancellationToken ct,
            int settleMs = GameStateWait.MenuSettleMs)
        {
            int presses = 0;
            GameSnapshot left = await GameStateWait.PressUntilAsync(game, () => { presses++; return tap(macro); },
                $"prompt {prompt} to close", s => s.Prompt != prompt, confirm, attempts, ct, settleMs);
            Console.WriteLine($"[LevelUp] {macro} at prompt {prompt} after {presses} press(es): {left}");
        }

        private static Func<string, Task> Tap(IBot bot, CancellationToken ct) => async macro =>
        {
            bot.RunMacroString(macro);
            await bot.WaitForMacroAsync(ct);
        };

        // We can't read memory to confirm a learn (dialogue still playing / evolution screen
        // up), but we know the decision's outcome exactly, so record it on our in-memory
        // PokemonData directly.
        private static void ApplyLearnedMove(PokemonData pokemon, int slot, PokemonLevelUpMoveMetadata newMove)
        {
            if (slot == 0)
            {
                pokemon.Move1 = (ushort)newMove.Id;
            }
            else if (slot == 1)
            {
                pokemon.Move2 = (ushort)newMove.Id;
            }
            else if (slot == 2)
            {
                pokemon.Move3 = (ushort)newMove.Id;
            }
            else if (slot == 3)
            {
                pokemon.Move4 = (ushort)newMove.Id;
            }
        }

        private static int FirstOpenMoveSlot(PokemonData pokemon)
        {
            var moves = pokemon.Moves;
            for (int i = 0; i < moves.Length; i++)
            {
                if (moves[i].Id == 0)
                {
                    return i;
                }
            }
            return -1;
        }

        private void ProcessEvolutions(PokemonData levelUp, PokemonMetadata metadata, GameState state, PokemonBattleState battleState)
        {
            // The parser only creates this key on pokemon that are somebody's pre-evolution, so a
            // fully-evolved mon legitimately has no entry at all. Absent means "no evolutions".
            if (!metadata.EvolutionDetails.TryGetValue(state.GameFlavor, out var evolutionDetails))
            {
                return;
            }
            foreach(var evoDetails in evolutionDetails)
            {
                if(WillEvolve(levelUp, evoDetails, battleState))
                {
                    if(state.PendingEvolutions.ContainsKey((levelUp.PID, levelUp.Species)))
                    {
                        state.PendingEvolutions[(levelUp.PID, levelUp.Species)] = evoDetails;
                    }
                    else
                    {
                        state.PendingEvolutions.Add((levelUp.PID, levelUp.Species), evoDetails);
                    }
                }
            }
        }

        internal static bool WillEvolve(PokemonData levelUp, EvolutionMetadata evolutionMetadata, PokemonBattleState battleState)
        {
            if(evolutionMetadata.MinLevel is not null && levelUp.Level < evolutionMetadata.MinLevel)
            {
                return false;
            }
            if(evolutionMetadata.HeldItemId is not null && levelUp.HeldItem != evolutionMetadata.HeldItemId)
            {
                return false;
            }
            if(evolutionMetadata.TyrogueValue is not null)
            {
                if(evolutionMetadata.TyrogueValue == -1 && levelUp.Attack < levelUp.Defense)
                {
                    return true;
                }
                else if(evolutionMetadata.TyrogueValue == 0 && levelUp.Attack == levelUp.Defense)
                {
                    return true;
                }
                else if(evolutionMetadata.TyrogueValue == 1 && levelUp.Attack > levelUp.Defense)
                {
                    return true;
                }
            }
            DateTime date = DateTime.Now;
            bool isNight = date.Hour >= 20 || date.Hour < 4;
            if (evolutionMetadata.IsDay is not null && evolutionMetadata.IsDay.Value && isNight)
            {
                return false;
            }
            if(evolutionMetadata.IsNight is not null && evolutionMetadata.IsNight.Value && !isNight)
            {
                return false;
            }
            if(evolutionMetadata.PartySpeciesId is not null && battleState.PartyPokemon.Where(x => x.Species == evolutionMetadata.PartySpeciesId.Value).Count() == 0)
            {
                return false;
            }
            if(evolutionMetadata.KnownMoveId is not null && levelUp.Moves.Where(x => x.Id == evolutionMetadata.KnownMoveId.Value).Count() == 0)
            {
                return false;
            }
            if(evolutionMetadata.Gender is not null && levelUp.Gender != evolutionMetadata.Gender.Value)
            {
                return false;
            }
            if(evolutionMetadata.MinHappiness is not null && levelUp.Happiness < evolutionMetadata.MinHappiness)
            {
                return false;
            }
            if(evolutionMetadata.MinBeauty is not null && levelUp.Beauty < evolutionMetadata.MinBeauty)
            {
                return false;
            }
            return true;
        }

        public async Task WaitForLevelUpOrField(GameState state, CancellationToken ct)
        {
            GameSnapshot next = await GameStateWait.UntilAsync(state.Bot.State, "level-up or field",
                s => s.Screen is GameScreen.BattleExpPrompt or GameScreen.FieldFree or GameScreen.FieldTask
                    || (s.Screen == GameScreen.Evolution && state.PendingEvolutions.Count > 0), PromptStale, ct);
            if (next.Screen == GameScreen.BattleExpPrompt)
            {
                state.SwapState(EventState.LevelUp);
            }
            else if (next.Screen == GameScreen.Evolution)
            {
                state.SwapState(EventState.EvolutionInit);
            }
            else
            {
                state.SwapState(EventState.BattleComplete);
            }
            Console.WriteLine(state.State.Peek().ToString());
        }

        internal static PokemonData? GetLevelUp(PokemonBattleState oldState, PokemonBattleState? newState)
        {
            if(newState is null)
            {
                return null;
            }
            for(int i = 0; i < Math.Min(oldState.PartyPokemon.Count, newState.PartyPokemon.Count); i++)
            {
                if (newState.PartyPokemon[i].Level > oldState.PartyPokemon[i].Level)
                {
                    return newState.PartyPokemon[i];
                }
            }
            return null;
        }
    }
}
