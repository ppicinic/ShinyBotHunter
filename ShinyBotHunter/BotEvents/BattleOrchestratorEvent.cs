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
                state.Bot.RunMacroString("W.45;T.A;W.1125;T.A;W.15");
                await state.Bot.WaitForMacroAsync();
                // TODO learn moves
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
                bool first = true;
                var metadata = metadataService.GetPokemonMetadata(levelUp.Species);
                var newMoves = metadata.LevelUpMoves[GameFlavor.HGSS].Where(x => x.Level == levelUp.Level).ToList();
                newMoves.Sort();
                foreach(var newMove in newMoves)
                {
                    if (first)
                    {
                        first = false;
                    }
                    else
                    {
                        // we potentially learned a new move and the state is different now.
                        state.BattleState = await reader.GetPokemonBattleState(state.Bot);
                        levelUp = state.BattleState!.PartyPokemon.Where(x => x.Species == levelUp.Species && x.PID == levelUp.PID).First();
                    }
                    bool hasFourMoves = true;
                    foreach(var knownMove in levelUp.Moves)
                    {
                        if(knownMove.Id == 0)
                        {
                            hasFourMoves = false;
                        }
                    }
                    if (hasFourMoves)
                    {
                        // replace move.
                        state.Bot.RunMacroString("W.340");
                        await state.Bot.WaitForMacroAsync();
                        int moveId = await discordBotService.SendNewMoveDecisionAndWaitAsync(levelUp, newMove);
                        if(moveId == -1)
                        {
                            state.Bot.RunMacroString($"W.5;S.DoNotLearn;W.225;S.DoNotLearnComplete;W.5");
                            await state.Bot.WaitForMacroAsync();
                        }
                        else
                        {
                            string moveStr = MoveShorthands[moveId];
                            state.Bot.RunMacroString($"W.5;S.NewMove;W.165;S.New{moveStr};W.20;S.Forget;W.5");
                            await state.Bot.WaitForMacroAsync();
                        }
                    }
                    else
                    {
                        // TODO just press A until move learn complete
                    }
                }
                ProcessEvolutions(levelUp, metadata, state, state.BattleState!);
            }
            state.SwapState(EventState.EnemyFainted);
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
                await Task.Delay(100);
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
            bool moveEnd = false;
            while (!moveEnd)
            {
                await Task.Delay(100);
                if(!IsBattle(await state.Bot.GetBattleValue()))
                {
                    state.SwapState(EventState.BattleComplete);
                    moveEnd = true;
                }
                if (await reader.GetPokemonBattleState(state.Bot) is var battleState && await state.Bot.GetBattleMenuState() == 1)
                {
                    state.State.Pop();
                    state.State.Push(EventState.DecideAction);
                    state.BattleState = battleState;
                    moveEnd = true;
                }
                // TODO we could hit a point of both fainting.
                else if(battleState is not null && battleState.ActivePokemon.HP == 0)
                {
                    // action if our pokemon dies
                    state.State.Pop();
                    state.State.Push(EventState.OurPokemonFainted);
                    //state.BattleState = battleState;
                    moveEnd = true;
                }
                else if(battleState is not null && battleState.EnemyPokemon.HP == 0)
                {
                    state.State.Pop();
                    state.State.Push(EventState.EnemyFainted);
                    // we don't override state here. we could level up and that this could hide the level up. 
                    moveEnd = true;
                }
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
                await Task.Delay(166);
                anchor = await bot.ReadUIntAsync(0x21D4158);
                foeAnchorPtr = anchor + 0x6930;
                foeAnchor = await bot.ReadUIntAsync(foeAnchorPtr);
                foeCountPtr = foeAnchor + 0xC14;
                foeCount = await bot.ReadByteAsync(foeCountPtr);
            }

            PokemonData? pokemon = pokemon = parser.Parse(await bot.ReadBytesAsync(foeAnchor + 0xC18, 0xEC));
            while (pokemon is null || pokemon.IsCorrupt)
            {
                await Task.Delay(166);
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
                await Task.Delay(166);
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
