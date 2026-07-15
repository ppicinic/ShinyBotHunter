using Discord;
using Microsoft.Extensions.Options;
using Mono.TextTemplating;
using ShinyBotHunter.Bot;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.BattlePokemonParser;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Services;
using ShinyBotHunter.Util;
using System.Diagnostics.Eventing.Reader;
using YamlDotNet.Core;
using static ShinyBotHunter.BotEvents.WildPokemonEncounterEvent;

namespace ShinyBotHunter.BotEvents
{
    public class BattleOrchestratorEvent : AbstractBotEvent<GameState>
    {
        public override EventState EventState {
            get => EventState.NotStarted;
        }
        private static readonly Dictionary<int, string> MoveShorthands = new Dictionary<int, string>()
        {
            { 0, "MoveOne" },
            { 1, "MoveTwo" },
            { 2, "MoveThree" },
            { 3, "MoveFour" },
        };

        private const string EvolutionMacro = "W.45;T.A;W.1125;T.A;W.15";

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

        private HgssPokemonParser parser;
        private PokemonPersistenceService persistenceService;
        private PokemonMetadataService metadataService;
        private ApplicationSettings appSettings;
        private DiscordBotService discordBotService;
        private PokemonStateReader reader;
        private SmogonDamageService damageService;
        private IPointerService pointerService;
        public BattleOrchestratorEvent(HgssPokemonParser parser, PokemonPersistenceService persistenceService, IOptions<ApplicationSettings> appSettings, DiscordBotService discordBotService, PokemonStateReader reader,
                                        PokemonMetadataService metadataService, SmogonDamageService damageService, IServiceProvider provider)
        {
            this.parser = parser;
            this.persistenceService = persistenceService;
            this.appSettings = appSettings.Value;
            this.discordBotService = discordBotService;
            this.reader = reader;
            this.metadataService = metadataService;
            this.damageService = damageService;
            this.pointerService = provider.GetRequiredKeyedService<IPointerService>(GameFlavor.HGSS);
        }

        public override async Task<GameState> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            while (state.State.Peek() != EventState.BattleComplete && state.State.Peek() != EventState.Waiting && !cancellationToken.IsCancellationRequested)
            {
                EventState topState = state.State.Peek();
                if (topState == EventState.BattleStart)
                {
                    state.State.Pop();
                    state.State.Push(EventState.PokemonDataPending);
                }
                else if (topState == EventState.PokemonDataPending)
                {
                    await PokemonDataPending(state);
                }
                else if (topState == EventState.DecideAction)
                {
                    DecideAction(state);
                }
                else if (topState == EventState.Fight)
                {
                    await SelectMove(state);
                }
                else if (topState == EventState.WaitForMovesEnd)
                {
                    await WaitForMoveEnd(state);
                }
                else if (topState == EventState.OurPokemonFainted || topState == EventState.BothPokemonFainted)
                {
                    // TODO Implement losing our pokemon - for now we just defer to user to regain control
                    state.State.Pop();
                    state.State.Push(EventState.Waiting);
                }
                else if (topState == EventState.EnemyFainted)
                {
                    await WaitForLevelUpOrField(state);
                }
                else if (topState == EventState.LevelUp)
                {
                    await ProcessLevelUp(state);
                }
                else if (topState == EventState.EvolutionInit)
                {
                    await EvolutionInit(state);
                }
                else if(topState == EventState.Flee)
                {
                    await Flee(state);
                }
            }
            state.BattleState = null;
            return state;
        }

        private async Task Flee(GameState state)
        {
            state.Bot.RunMacroString("W.10;S.Run.3;W.5;");
            await state.Bot.WaitForMacroAsync();
            state.SwapState(EventState.WaitForMovesEnd);
        }

        private async Task EvolutionInit(GameState state)
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
                await state.Bot.WaitForMacroAsync();

                // Evolution changes species/stats but not level or known moves, so evo.Pokemon
                // (captured before the evolution animation ran) still accurately reflects the
                // mon's level and moveset for the first move check. Reading fresh battle state
                // here would fail/time out if a "trying to learn a move" prompt is already up,
                // since that screen doesn't parse as a normal battle state.
                var metadata = metadataService.GetPokemonMetadata(evo.EvolutionDetails.EvolveToId);
                var newMoves = metadata.LevelUpMoves[GameFlavor.HGSS]
                    .Where(x => x.Level == evo.Pokemon.Level || x.Level == 0).ToList();
                newMoves.RemoveAll(m => evo.Pokemon.Moves.Any(k => k.Id == m.Id));
                newMoves.Sort();

                await ProcessEvolutionMoveLearning(state, evo.Pokemon, newMoves);

                // Refresh via the real party read, not the battle-state one -- the enemy is
                // long gone by this point in the flow, so there's nothing for the battle
                // reader's enemy pointer chain to resolve to.
                var freshParty = await reader.GetPokemonState(state.Bot);
                state.BattleState!.PartyPokemon = freshParty.PartyPokemon.Take(freshParty.PartySize).Select(x => x!).ToList();
            }
            Console.WriteLine("end evolution");
            state.PendingEvolutions = new Dictionary<(uint, ushort), EvolutionMetadata>();
            state.SwapState(EventState.EnemyFainted);
        }

        private async Task ProcessLevelUp(GameState state)
        {
            var newState = await reader.GetPokemonBattleState(state.Bot);
            var levelUp = GetLevelUp(state.BattleState!, newState);
            state.BattleState = newState;
            state.Bot.RunMacroString("W.265;T.A;W.5;T.A");
            await state.Bot.WaitForMacroAsync();
            if (levelUp is not null)
            {
                var metadata = metadataService.GetPokemonMetadata(levelUp.Species);
                var newMoves = metadata.LevelUpMoves[GameFlavor.HGSS].Where(x => x.Level == levelUp.Level).ToList();
                newMoves.Sort();
                levelUp = await ProcessMoveLearning(state, levelUp, newMoves);
                ProcessEvolutions(levelUp, metadata, state, state.BattleState!);
            }
            state.SwapState(EventState.EnemyFainted);
        }

        private async Task<PokemonData> ProcessMoveLearning(GameState state, PokemonData pokemon, List<PokemonLevelUpMoveMetadata> newMoves)
        {
            bool first = true;
            foreach(var newMove in newMoves)
            {
                if (first)
                {
                    first = false;
                }
                else
                {
                    // we potentially learned a new move and the state is different now. Read the
                    // real (non-battle) party state directly here: by now the enemy may already
                    // be gone (fainted), so the battle reader's enemy pointer chain can time out
                    // for no reason even though the party data itself is perfectly readable.
                    var freshParty = await reader.GetPokemonState(state.Bot);
                    state.BattleState!.PartyPokemon = freshParty.PartyPokemon.Take(freshParty.PartySize).Select(x => x!).ToList();
                    pokemon = state.BattleState.PartyPokemon.Where(x => x.PID == pokemon.PID).First();
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
                    await state.Bot.WaitForMacroAsync();
                    int moveId = await discordBotService.SendNewMoveDecisionAndWaitAsync(pokemon, newMove);
                    if(moveId == -1)
                    {
                        state.Bot.RunMacroString($"W.5;{DoNotLearnMacro}");
                        await state.Bot.WaitForMacroAsync();
                    }
                    else
                    {
                        string moveStr = MoveShorthands[moveId];
                        state.Bot.RunMacroString($"W.5;{string.Format(LearnMoveMacroTemplate, moveStr)}");
                        await state.Bot.WaitForMacroAsync();
                    }
                }
                else
                {
                    // The mon has an open move slot, so the game auto-learns the move and just
                    // needs the "Pokemon learned Move!" text advanced.
                    Console.WriteLine($"{pokemon.Name} learned {newMove.Name} in an open move slot");
                    state.Bot.RunMacroString($"{LevelUpPrePromptMacro};{FreeSlotMacro}");
                    await state.Bot.WaitForMacroAsync();
                }
            }
            return pokemon;
        }

        private async Task<PokemonData> ProcessEvolutionMoveLearning(GameState state, PokemonData pokemon, List<PokemonLevelUpMoveMetadata> newMoves)
        {
            bool first = true;
            foreach(var newMove in newMoves)
            {
                if (first)
                {
                    first = false;
                }
                else
                {
                    var freshParty = await reader.GetPokemonState(state.Bot);
                    state.BattleState!.PartyPokemon = freshParty.PartyPokemon.Take(freshParty.PartySize).Select(x => x!).ToList();
                    pokemon = state.BattleState.PartyPokemon.Where(x => x.PID == pokemon.PID).First();
                }
                int moveId = await discordBotService.SendNewMoveDecisionAndWaitAsync(pokemon, newMove);
                if(moveId == -1)
                {
                    state.Bot.RunMacroString($"W.5;{EvolutionDoNotLearnMacro}");
                    await state.Bot.WaitForMacroAsync();
                }
                else
                {
                    string moveStr = MoveShorthands[moveId];
                    state.Bot.RunMacroString($"W.5;{string.Format(EvolutionLearnMoveMacroTemplate, moveStr)}");
                    await state.Bot.WaitForMacroAsync();
                }
            }
            return pokemon;
        }

        private void ProcessEvolutions(PokemonData levelUp, PokemonMetadata metadata, GameState state, PokemonBattleState battleState)
        {
            foreach(var evoDetails in metadata.EvolutionDetails[GameFlavor.HGSS])
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

        private bool WillEvolve(PokemonData levelUp, EvolutionMetadata evolutionMetadata, PokemonBattleState battleState)
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
            // TODO remove - block auto making a level up for the conditions we haven't checked yet.
            if(evolutionMetadata.MinLevel is null)
            {
                return false;
            }
            return true;
        }

        private async Task WaitForLevelUpOrField(GameState state)
        {
            bool endWait = false;
            

            Console.WriteLine("hit");
            while (!endWait)
            {
                await Task.Delay(Constant.PollIntervalMs);
                try
                {
                    if(state.PendingEvolutions.Count() > 0 && await state.Bot.GetBattleState() == 14)
                    {
                        state.SwapState(EventState.EvolutionInit);
                        endWait = true;
                    }
                    else if (!IsBattle(await state.Bot.GetBattleValue()))
                    {
                        state.SwapState(EventState.BattleComplete);
                        endWait = true;
                    }
                    else if (await reader.GetPokemonBattleState(state.Bot) is var battleState
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
                catch(Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    throw;
                }
            }
            Console.WriteLine(state.State.Peek().ToString());
        }

        private bool IsBattle(byte battleValue)
        {
            return battleValue == 0x41 || battleValue == 0x97 || battleValue == 0xC0;
        }

        private PokemonData? GetLevelUp(PokemonBattleState oldState, PokemonBattleState? newState)
        {
            if(newState is null)
            {
                return null;
            }
            for(int i = 0; i < oldState.PartyPokemon.Count(); i++)
            {
                if (newState.PartyPokemon[i].Level > oldState.PartyPokemon[i].Level)
                {
                    return newState.PartyPokemon[i];
                }
            }
            return null;
        }

        private async Task WaitForMoveEnd(GameState state)
        {
            using var cts = new CancellationTokenSource();
            CancellationToken ct = cts.Token;

            // Poll each end-of-move condition concurrently and act on whichever trips first.
            // The cheap battle-end check no longer waits behind the heavy battle-state read.
            var tasks = new List<Task<(EventState next, PokemonBattleState? bs)?>>
            {
                PollBattleEnded(ct),
                PollMenuUp(ct),
                PollFaints(ct),
            };

            (EventState next, PokemonBattleState? bs)? outcome = null;
            while (outcome is null && tasks.Count > 0)
            {
                var finished = await Task.WhenAny(tasks);
                tasks.Remove(finished);
                outcome = await finished; // non-null: this task detected its condition
            }

            cts.Cancel(); // stop the losing polls

            if (outcome is null)
                return;

            state.SwapState(outcome.Value.next);
            if (outcome.Value.bs is not null)
                state.BattleState = outcome.Value.bs;

            // ── Local poll tasks ────────────────────────────────────────────────────
            // Battle ended (e.g. a successful escape). One cheap byte read, polled at full rate.
            async Task<(EventState, PokemonBattleState?)?> PollBattleEnded(CancellationToken token)
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        if (!IsBattle(await state.Bot.GetBattleValue()))
                            return (EventState.BattleComplete, null);
                        await Task.Delay(Constant.PollIntervalMs, token);
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException || token.IsCancellationRequested) { }
                return null;
            }

            // Command menu is back up → decide the next action. Guarded on a valid battle state
            // so a stale post-battle read can't push DecideAction (the bug-2b invariant).
            async Task<(EventState, PokemonBattleState?)?> PollMenuUp(CancellationToken token)
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        if (await state.Bot.GetBattleMenuState() == 1)
                        {
                            var battleState = await reader.GetPokemonBattleState(state.Bot);
                            if (battleState is not null)
                                return (EventState.DecideAction, battleState);
                        }
                        await Task.Delay(Constant.PollIntervalMs, token);
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException || token.IsCancellationRequested) { }
                return null;
            }

            // A combatant fainted. Both checks share one battle-state read.
            async Task<(EventState, PokemonBattleState?)?> PollFaints(CancellationToken token)
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        var battleState = await reader.GetPokemonBattleState(state.Bot);
                        if (battleState is not null)
                        {
                            if (battleState.ActivePokemon.HP == 0)
                                return (EventState.OurPokemonFainted, null);
                            if (battleState.EnemyPokemon.HP == 0)
                                return (EventState.EnemyFainted, null);
                        }
                        await Task.Delay(Constant.PollIntervalMs, token);
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException || token.IsCancellationRequested) { }
                return null;
            }
        }

        private void DecideAction(GameState state)
        {
            var lambda = (GameState s) =>
            {
                if(s.BattleMode == BattleMode.Flee)
                {
                    return EventState.Flee;
                }
                else if (state.BattleState!.PartyPokemon[0].HP == 0)
                {
                    return EventState.Flee;
                }
                else if(state.BattleState.ActivePokemon.HP <= state.BattleState.ActivePokemon.MaxHP / 4 || TotalDamagePP(state.BattleState.ActivePokemon) == 0) // if we can't kill or close to dying flee.
                {
                    return EventState.Flee;
                }
                else
                {
                    return EventState.Fight;
                }
            };
            EventState nextState = lambda(state);
            state.State.Pop();
            state.State.Push(nextState);
        }

        private int TotalDamagePP(BattlePokemon activePokemon)
        {
            var moves = activePokemon.Moves;
            int total = 0;
            foreach(var move in moves)
            {
                if(move.Id > 0 && metadataService.GetPokemonMoveMetadata(move.Id).IsDamageMove)
                {
                    total += move.PP;
                }
            }
            return total;
        }

        private async Task SelectMove(GameState state)
        {
            int move = damageService.PickBestMove(state.BattleState!);
            if(move == -1)
            {
                state.State.Pop();
                state.State.Push(EventState.Flee);
            }
            else
            {
                string moveStr = MoveShorthands[move];
                state.Bot.RunMacroString($"W.5;S.{moveStr};W.10;S.{moveStr}");
                await state.Bot.WaitForMacroAsync();
                state.State.Pop();
                state.State.Push(EventState.WaitForMovesEnd);
            }

        }

        private async Task PokemonDataPending(GameState state)
        {
            IBot bot = state.Bot;
            uint anchor = await bot.ReadUIntAsync(0x21D4158);
            uint foeAnchorPtr = anchor + 0x6930;
            uint foeAnchor = await bot.ReadUIntAsync(foeAnchorPtr);
            uint foeCountPtr = foeAnchor + 0xC14;
            var foeCount = await bot.ReadByteAsync(foeCountPtr);
            while (foeCount == 0)
            {
                await Task.Delay(Constant.PollIntervalMs);
                anchor = await bot.ReadUIntAsync(0x21D4158);
                foeAnchorPtr = anchor + 0x6930;
                foeAnchor = await bot.ReadUIntAsync(foeAnchorPtr);
                foeCountPtr = foeAnchor + 0xC14;
                foeCount = await bot.ReadByteAsync(foeCountPtr);
            }

            PokemonData? pokemon = pokemon = parser.Parse(await bot.ReadBytesAsync(foeAnchor + 0xC18, 0xEC));
            while (pokemon is null || pokemon.IsCorrupt)
            {
                await Task.Delay(Constant.PollIntervalMs);
                anchor = await bot.ReadUIntAsync(0x21D4158);
                foeAnchor = await bot.ReadUIntAsync(anchor + 0x6930);
                pokemon = pokemon = parser.Parse(await bot.ReadBytesAsync(foeAnchor + 0xC18, 0xEC));
            }


            // tap B until battle menu

            // this code is repeated but it's unique cause we need to progress through the first pokemon.
            bot.RunRepeatMacro("T.B;W.5");
            anchor = await bot.ReadUIntAsync(0x21D4158);
            var battleMenuStatePtr = anchor + 0x230EC;
            byte battleMenuState = await bot.ReadByteAsync(battleMenuStatePtr);
            while (battleMenuState != 1)
            {
                await Task.Delay(Constant.PollIntervalMs);
                anchor = await bot.ReadUIntAsync(0x21D4158);
                battleMenuStatePtr = anchor + 0x230EC;
                battleMenuState = await bot.ReadByteAsync(battleMenuStatePtr);
            }
            bot.ClearMacro();
            try
            {
                await bot.WaitForMacroAsync();
                bot.RunMacroString("W.5"); // TODO tune this. the menu flag is available before we can actually press so we want to wait by frame not real time.
                await bot.WaitForMacroAsync();
                state.BattleState = await reader.GetPokemonBattleState(bot);
                state.PendingEvolutions = new Dictionary<(uint, ushort), EvolutionMetadata>();
                List<int> startPositions = new List<int>();
                for (int i = 0; i < state.BattleState!.PartyPokemon.Count(); i++)
                {
                    startPositions.Add(i);
                }
                if (pokemon is null)
                {
                    // corrupted state. this should never happen but if it does we just flee cause we can't recover.
                    Console.WriteLine("read pokemon too fast");
                    state.State.Pop();
                    state.State.Push(EventState.Flee);
                }
                else
                {
                    Console.WriteLine($"{pokemon.Name}, isShiny: {pokemon.IsShiny}");
                    var persistPokemon = new PersistPokemonData()
                    {
                        Game = state.GameFlavor,
                        Phase = state.Milestone,
                        PokemonData = pokemon,
                        DateTime = DateTime.UtcNow,
                        GameFileName = bot.GameFileName,
                    };
                    await persistenceService.AddEncounterAsync(persistPokemon);
                    if (await state.Target.IsTargetAsync(pokemon))
                    {
                        state.TargetPokemon = persistPokemon;
                        await NotifyDiscordOfTarget(persistPokemon);
                        state.State.Pop();
                        state.State.Push(EventState.Waiting);
                    }
                    else
                    {
                        state.State.Pop();
                        state.State.Push(EventState.DecideAction);
                    }
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.ToString());
                throw;
            }
        }

        private async Task NotifyDiscordOfTarget(PersistPokemonData persistPokemonData)
        {
            EmbedBuilder eb = new EmbedBuilder();
            eb.WithColor(Color.Gold);
            bool isFemale = persistPokemonData.PokemonData.FormCode.EndsWith(";f");
            string formCode = persistPokemonData.PokemonData.FormCode.Split(";f")[0];
            string spriteFolder = persistPokemonData.PokemonData.IsShiny ? (isFemale ? "Female_Shiny" : "Shiny") : (isFemale ? "Female_Default" : "Default");
            string spritePath = $"{appSettings.SpriteBaseDirectory}{spriteFolder}/{formCode}.png";
            string attachment = $"attachment://{Path.GetFileName(spritePath)}";
            Console.WriteLine(attachment);
            eb.WithThumbnailUrl(attachment);
            eb.WithTitle($":sparkles: Shiny {persistPokemonData.PokemonData.Name} encountered! :sparkles:");
            eb.AddField("Nature", WildPokemonEncounterEvent.NATURE_NAMES[persistPokemonData.PokemonData.Nature]);
            eb.AddField("Shiny Value", persistPokemonData.PokemonData.ShinyValue);
            var pokemon = persistPokemonData.PokemonData;
            int ivSum = pokemon.IVHp + pokemon.IVAtk + pokemon.IVDef + pokemon.IVSpAtk + pokemon.IVSpDef + pokemon.IVSpe;
            eb.AddField($"IVs ({ivSum})", $"```╔═══╤═══╤═══╤═══╤═══╤═══╗\r\n║HP │ATK│DEF│SPA│SPD│SPE║\r\n╠═══╪═══╪═══╪═══╪═══╪═══╣\r\n║{PadIv(pokemon.IVHp)} │{PadIv(pokemon.IVAtk)} │{PadIv(pokemon.IVDef)} │{PadIv(pokemon.IVSpAtk)} │{PadIv(pokemon.IVSpDef)} │{PadIv(pokemon.IVSpe)} ║\r\n╚═══╧═══╧═══╧═══╧═══╧═══╝```");
            await discordBotService.SendMessageAsync(new DiscordMessage()
            {
                StringMessage = $"<@&{appSettings.ShinyLogMention}>",
                GuildId = appSettings.DiscordGuildId,
                ChannelId = appSettings.ShinyLogChannel,
                EmbedMessage = eb.Build(),
                Files = [spritePath]
            });
        }

        private string PadIv(byte iv)
        {
            if (iv < 10)
            {
                return $"0{iv}";
            }
            return $"{iv}";
        }
    }
}
