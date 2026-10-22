using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Util;

namespace ShinyBotHunter.BotEvents
{
    public class LevelUpFlow
    {
        // Settle, advance the opening text, sit through the evolution animation, dismiss the result.
        //
        // The long wait has to outlast the animation, which varies by species -- it was about a
        // second short for the slowest ones, so the final tap landed while the animation was still
        // playing, got swallowed, and left the result screen up with nothing to dismiss it. Padded by
        // 120 frames. Overshooting is safe: the result screen waits for a button, so a late tap still
        // lands, while an early one is simply lost.
        private const string EvolutionMacro = "W.45;T.A;W.1245;T.A;W.15";

        // Settles through the level-up fanfare, then pages past the stat panel with two A presses.
        //
        // The leading wait carries ~1s of deliberate margin. It is measured from whenever this macro
        // starts, and that moment moved earlier when the battle-state read got faster: ProcessLevelUp's
        // own read shortened, and WaitForLevelUpOrField now polls on a much finer grain, so the level
        // field is seen updating before the panel accepts input. The margin is safe to overshoot --
        // the stat panel waits for a button, so waiting too long only costs time, while waiting too
        // little drops the press and desyncs every macro after it.
        private const string LevelUpAdvanceMacro = "W.325;T.A;W.5;T.A";

        // Advances dialogue from right after the level-up fanfare to the point where the
        // move-learn decision (or auto-learn text) is ready to act on.
        private const string LevelUpPrePromptMacro = "W.340";

        // Macros driving the "do you want to learn a new move?" flow, post level-up.
        private const string DoNotLearnMacro = "S.DoNotLearn;W.225;S.DoNotLearnComplete;W.5";
        private const string LearnMoveMacroTemplate = "S.NewMove;W.165;S.New{0};W.20;S.Forget;W.5";
        private const string FreeSlotMacro = "T.A;W.30;T.A;W.15";

        // Clones of the above two for the post-evolution move-learn prompt. Kept as separate
        // constants (rather than reused) because the evolution flow's screen sequence does not
        // necessarily match the post-level-up one -- tune these independently.
        private const string EvolutionLearnMoveInit = "T.A;W.150;T.A;W.150;";
        private const string EvolutionLearnMoveMacroTemplate = $"{EvolutionLearnMoveInit}S.125.75.5;W.240;S.Evo{{0}}.5;W.15;S.60.165.5;W.320;T.A;W.150;T.A;W.90;T.A;W.150;T.A;W.15";
        private const string EvolutionDoNotLearnMacro = $"{EvolutionLearnMoveInit}S.125.125.5;W.240;S.125.75.5;W.180;T.A;W.15";

        // The post-evolution free-slot path: with an open move slot the game learns the move outright
        // and only shows "<Pokemon> learned <Move>!", so this just settles and advances that text.
        // Deliberately not built on EvolutionLearnMoveInit -- there is no prompt to walk into here.
        // The level-up equivalent is FreeSlotMacro.
        private const string EvolutionFreeSlotMacro = "W.160;T.A;W.15";

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
                state.Bot.RunMacroString(EvolutionMacro);
                await state.Bot.WaitForMacroAsync(ct);

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
            state.Bot.RunMacroString(LevelUpAdvanceMacro);
            await state.Bot.WaitForMacroAsync(ct);
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
                    state.Bot.RunMacroString(LevelUpPrePromptMacro);
                    await state.Bot.WaitForMacroAsync(ct);
                    int moveId = await discordBotService.SendNewMoveDecisionAndWaitAsync(pokemon, newMove);
                    if(moveId == -1)
                    {
                        state.Bot.RunMacroString($"W.5;{DoNotLearnMacro}");
                        await state.Bot.WaitForMacroAsync(ct);
                    }
                    else
                    {
                        string moveStr = BattleOrchestratorEvent.MoveShorthands[moveId];
                        state.Bot.RunMacroString($"W.5;{string.Format(LearnMoveMacroTemplate, moveStr)}");
                        await state.Bot.WaitForMacroAsync(ct);
                        ApplyLearnedMove(pokemon, moveId, newMove);
                    }
                }
                else
                {
                    // The mon has an open move slot, so the game auto-learns the move and just
                    // needs the "Pokemon learned Move!" text advanced.
                    Console.WriteLine($"{pokemon.Name} learned {newMove.Name} in an open move slot");
                    state.Bot.RunMacroString($"{LevelUpPrePromptMacro};{FreeSlotMacro}");
                    await state.Bot.WaitForMacroAsync(ct);
                    ApplyLearnedMove(pokemon, FirstOpenMoveSlot(pokemon), newMove);
                }
            }
            return pokemon;
        }

        private async Task<PokemonData> ProcessEvolutionMoveLearning(GameState state, PokemonData pokemon, List<PokemonLevelUpMoveMetadata> newMoves, CancellationToken ct)
        {
            // No memory reads in here: we're inside the evolution menus, where party state is
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
                    // replace macros below would then run against dialogue that isn't on screen. It
                    // also crashed outright: the prompt builds a button per move slot, and an empty
                    // slot has no move metadata to name it.
                    Console.WriteLine($"{pokemon.Name} learned {newMove.Name} in an open move slot");
                    state.Bot.RunMacroString(EvolutionFreeSlotMacro);
                    await state.Bot.WaitForMacroAsync(ct);
                    ApplyLearnedMove(pokemon, FirstOpenMoveSlot(pokemon), newMove);
                    continue;
                }
                int moveId = await discordBotService.SendNewMoveDecisionAndWaitAsync(pokemon, newMove);
                if(moveId == -1)
                {
                    state.Bot.RunMacroString($"W.5;{EvolutionDoNotLearnMacro}");
                    await state.Bot.WaitForMacroAsync(ct);
                }
                else
                {
                    string moveStr = BattleOrchestratorEvent.MoveShorthands[moveId];
                    state.Bot.RunMacroString($"W.5;{string.Format(EvolutionLearnMoveMacroTemplate, moveStr)}");
                    await state.Bot.WaitForMacroAsync(ct);
                    ApplyLearnedMove(pokemon, moveId, newMove);
                }
            }
            return pokemon;
        }

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
            bool endWait = false;
            

            Console.WriteLine("hit");
            while (!endWait)
            {
                await Task.Delay(Constant.PollIntervalMs, ct);
                try
                {
                    if(state.PendingEvolutions.Count() > 0 && await state.Bot.GetBattleState() == 14)
                    {
                        state.SwapState(EventState.EvolutionInit);
                        endWait = true;
                    }
                    else if (!BattleOrchestratorEvent.IsBattle(await state.Bot.GetBattleValue(ct)))
                    {
                        state.SwapState(EventState.BattleComplete);
                        endWait = true;
                    }
                    else if (await reader.GetPokemonBattleState(state.Bot, ct: ct) is var battleState
                                && battleState is not null
                                && battleState.ActivePokemon.HP == 0)
                    {
                        state.SwapState(EventState.BothPokemonFainted);
                        endWait = true;
                    }
                    else if (GetLevelUp(state.BattleState!, battleState) is var levelUp && levelUp is not null)
                    {
                        state.SwapState(EventState.LevelUp);
                        endWait = true;
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    Console.WriteLine(ex.ToString());
                    throw;
                }
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
