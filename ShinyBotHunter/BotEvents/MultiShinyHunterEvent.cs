using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Options;
using MongoDB.Driver;
using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.Context;
using ShinyBotHunter.Service;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using System.Threading.Channels;
using System.Xml.Linq;

namespace ShinyBotHunter.BotEvents
{
    public class StarterPokemonData
    {
        public required PokemonData PokemonData { get; set; }
        public required string Game { get; set; }
        public required string GameId { get; set; }
    }

    public enum ShinyStarterStates
    {
        CreateOppisiteSaves = 0,
        HuntFirstShiny = 1,
        CreatePlayingSaves = 2,
        HuntRestOfShinies = 3,
        Finished = 4,
    }

    public enum ShinyBotState
    {
        CreateSave,
        ShinyHunt,
        Finish
    }

    public class ShinyStarterGameState : BaseGameState
    {
        // Persist to MongoDB
        public ShinyStarterStates State { get; set; }
        public List<StarterPokemonData> PokemonData { get; set; } = null!;
        [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfDocuments)]
        public Dictionary<int, bool> GameIds { get; set; } = new Dictionary<int, bool>();
        // Active memory only do not persist.
        [BsonIgnore]
        public Dictionary<int, int> Placements { get; set; } = new Dictionary<int, int>();
    }
    public class MultiShinyHunterEvent : AbstractBotEvent<List<StarterPokemonData>>
    {
        private ShinyStarterGameState gameState = new ShinyStarterGameState() { Name = "Blank"};
        private int maxEmulators;
        private static readonly object _locker=new object();
        private IServiceProvider serviceProvider;
        private BotFactory botFactory;
        private RomInfoService romInfoService;
        private HgssShinyStarterEventFactory shinyStarterEventFactory;
        private IMongoCollection<BaseGameState> _collection;


        public override EventState EventState 
        { 
            get 
            { 
                return EventState.Complete;  
            } 
        }
        public int[] BotPositions 
        { 
            get 
            {
                int[] pos = new int[this.maxEmulators];
                for (int i = 0; i < this.maxEmulators; i++)
                {
                    pos[i] = i;
                }
                return pos;
            } 
        }

        public MultiShinyHunterEvent(IOptions<ApplicationSettings> appSettings, BotFactory botFactory, IServiceProvider serviceProvider, HgssShinyStarterEventFactory shinyStarterEventFactory, MongoDbConnection dbConnection, RomInfoService romInfoService)
        {
            this.maxEmulators = appSettings.Value.MaxEmulatorsFor(ConsoleType.DS);
            this.romInfoService = romInfoService;
            this.serviceProvider = serviceProvider;
            this.botFactory = botFactory;
            this.shinyStarterEventFactory = shinyStarterEventFactory;
            _collection = dbConnection.GetDatabase().GetCollection<BaseGameState>("PhaseStates");
        }

        /// <summary>
        /// Resolves which ROM file to use for a game by asking what the ROMs actually are, rather than
        /// assuming a filename. A renamed ROM keeps working, and a missing one fails here by name
        /// instead of surfacing later as an emulator that opened nothing.
        /// </summary>
        private string RomHandleFor(string gameName)
        {
            var rom = romInfoService.FindByGame(gameName).FirstOrDefault();
            if (rom is null)
            {
                throw new InvalidOperationException(
                    $"No ROM in the ROM folder identifies as {gameName}. The starter hunt needs one.");
            }
            return rom.Handle;
        }

        public async override Task<List<StarterPokemonData>> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            Console.WriteLine("Load state");
            gameState = await CreateOrLoadGameState();

            while (gameState.State != ShinyStarterStates.Finished && !cancellationToken.IsCancellationRequested)
            {
                if (gameState.State == ShinyStarterStates.CreateOppisiteSaves)
                {
                    await CreateOppositeSaves();
                }
                else if(gameState.State == ShinyStarterStates.HuntFirstShiny)
                {
                    await HuntFirstShiny(cancellationToken);
                }
                // is this stage needed? or can the next stage do it anyway?
                else if(gameState.State == ShinyStarterStates.HuntRestOfShinies)
                {
                    try
                    {
                        await HuntRestOfShinies(cancellationToken);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
                        return new List<StarterPokemonData>();
                    }
                }
            }
            return gameState.PokemonData;
        }

        private async Task CreateOppositeSaves()
        {
            List<Task> tasks = new List<Task>();
            for(int i = 0; i < maxEmulators; i++)
            {
                var context = botFactory.BuildGameContext(RomHandleFor("Pokemon HeartGold"), i + 1, i);
                IBot bot = botFactory.CreateBot(context);
                CreateSaveEvent createSave = new CreateSaveEvent(bot, context, serviceProvider);
                tasks.Add(createSave.ExecuteAsync(new GameState()));
                await Task.Delay(5000);
            }
            try
            {
                await Task.WhenAll(tasks);
                gameState.State = ShinyStarterStates.HuntFirstShiny;
                await SaveStateAsync(gameState);
                await Task.Delay(10000);
            }
            catch { } // catch a user controlled cancellation
        }

        private async Task HuntFirstShiny(CancellationToken cancellationToken)
        {
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            List<Task<PokemonData?>> tasks = new List<Task<PokemonData?>>();
            List<IBot> bots = new List<IBot>();
            for (int i = 0; i < maxEmulators; i++)
            {
                var context = botFactory.BuildGameContext(RomHandleFor("Pokemon HeartGold"), i + 1, i);
                IBot bot = botFactory.CreateBot(context);
                bots.Add(bot);
                HgssShinyStarterEvent hgssShinyStarter = shinyStarterEventFactory.CreateEvent(bot, GameFlavor.HGSS);
                tasks.Add(hgssShinyStarter.ExecuteWithResultAsync(new GameState(), cancellationTokenSource.Token));
                await Task.Delay(5000);
            }
            var pauseTasks = new List<Task<PokemonData?>>(tasks);
            pauseTasks.Add(Task.Run(async () =>
            {
                await Task.Delay(-1, cancellationToken);
                return (PokemonData?)null;
            }));
            var task = await Task.WhenAny(pauseTasks);
            try
            {
                var pokemonData = await task;
                if (pokemonData is not null)
                {
                    gameState.PokemonData.Add(new StarterPokemonData() { PokemonData = pokemonData, Game = RomHandleFor("Pokemon HeartGold"), GameId = (tasks.IndexOf(task) + 1).ToString() });
                    gameState.State = ShinyStarterStates.HuntRestOfShinies;
                    await SaveStateAsync(gameState);
                    await cancellationTokenSource.CancelAsync();
                    await Task.WhenAll(tasks); // might throw exception, might need to this in a try/catch
                                               //foreach(var bot in bots) 
                                               //{
                                               //    try
                                               //    {
                                               //        await bot.DisposeAsync();
                                               //    }
                                               //    catch { }
                                               //}
                    await Task.Delay(20000);
                }
            }
            catch (OperationCanceledException) 
            {
                await cancellationTokenSource.CancelAsync();
                await Task.WhenAll();
                await Task.Delay(20000);
            }
        }

        private async Task HuntRestOfShinies(CancellationToken cancellationToken)
        {
            Channel<StarterPokemonData> channel = Channel.CreateUnbounded<StarterPokemonData>();
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            var _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(-1, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    await cancellationTokenSource.CancelAsync(); 
                    await Task.Delay(2 * 60 * 1000);
                    channel.Writer.TryComplete();
                }

            });
            // load and starter all save gameIds up to max
            List<int> gameIdsReady = gameState.GameIds.Where(x => !x.Value).OrderBy(x => x.Key).Select(x => x.Key).ToList();
            for (int i = 0; i < gameIdsReady.Count() && i < maxEmulators; i++) {
                int gameId = gameIdsReady[i];
                int placement = i;
                gameState.Placements.Add(gameId, placement);
                ResumeHuntTask(gameId, placement, channel, cancellationTokenSource.Token);
                await Task.Delay(5000);
            }
            int lastGameId = gameState.GameIds.OrderBy(x => x.Key).Select(x => x.Key).LastOrDefault(0);
            int lastPlacement = gameIdsReady.Count() - 1;
            if (gameIdsReady.Count() < maxEmulators)
            {
                for (int i = 0; i < maxEmulators - gameIdsReady.Count(); i++)
                {
                    lastGameId++;
                    int gameId = lastGameId;
                    lastPlacement++;
                    int placement = lastPlacement;
                    gameState.Placements.Add(gameId, placement);
                    gameState.GameIds.Add(gameId, false);
                    await SaveStateAsync(gameState);
                    CreateAndHuntTask(gameId, placement, channel, cancellationTokenSource.Token);
                    await Task.Delay(5000);
                }
            }
            bool complete = false;
            await foreach (var item in channel.Reader.ReadAllAsync())
            {
                gameState.PokemonData.Add(item);
                int finishedGameId = Int32.Parse(item.GameId);
                gameState.GameIds[finishedGameId] = true;
                await SaveStateAsync(gameState);
                if (IsComplete())
                {
                    if (!complete)
                    {
                        complete = true;
                        gameState.State = ShinyStarterStates.Finished;
                        await SaveStateAsync(gameState);
                        await cancellationTokenSource.CancelAsync();
                        await Task.Delay(2 * 60 * 1000); // we wait 2 minutes for all tasks to cancel and queue
                        channel.Writer.TryComplete(); // doesn't really matter if we close multiple times
                                                      // we don't want to do more here cause it could run multiple times,
                                                      // we should break out of this for loop after cancelling and running this multiple times
                    }
                }else
                {
                    int placement = gameState.Placements[Int32.Parse(item.GameId)];
                    List<int> readyGameIds = gameState.GameIds.Where(x => !x.Value && !gameState.Placements.ContainsKey(x.Key)).Select(x => x.Key).OrderBy(x => x).ToList();
                    if(readyGameIds.Count() > 0)
                    {
                        // resume a game
                        int gameId = readyGameIds[0];
                        gameState.Placements.Add(gameId, placement);
                        ResumeHuntTask(gameId, placement, channel, cancellationTokenSource.Token);
                    }
                    else
                    {
                        // create a new game and hunt
                        int gameId = gameState.GameIds.Select(x => x.Key).OrderBy(x => x).LastOrDefault(0) + 1;
                        gameState.Placements.Add(gameId, placement);
                        gameState.GameIds.Add(gameId, false);
                        await SaveStateAsync(gameState);
                        // it's possible gameIds can crash on create save and never get made, this will perm stuck them on false
                        // ideally we want a concurrent dictionary and have CreateAndHuntTask add the gameId. we may want to have states as well. as with
                        // concurrency we create a bigger problem of that this could try to create to games with the same id at the same time -yikes!-
                        CreateAndHuntTask(gameId, placement, channel, cancellationTokenSource.Token);
                    }
                }
            }
            gameState.State = ShinyStarterStates.Finished;
        }

        private bool IsComplete()
        {
            bool[] found = new bool[3];
            foreach(var pokemon in gameState.PokemonData)
            {
                int species = pokemon.PokemonData.Species;
                if(species == 152)
                {
                    found[0] = true;
                }else if(species == 155)
                {
                    found[1] = true;
                }
                else if(species == 158)
                {
                    found[2] = true;
                }
            }
            return found[0] && found[1] && found[2];
        }

        private void ResumeHuntTask(int gameId, int placement, Channel<StarterPokemonData> channel, CancellationToken token)
        {
            var _ = Task.Run(async () =>
            {
                var context = botFactory.BuildGameContext(RomHandleFor("Pokemon SoulSilver"), gameId, placement);
                IBot bot = botFactory.CreateBot(context);
                var e = shinyStarterEventFactory.CreateEvent(bot, context.GameFlavor);
                var pokemonData = await e.ExecuteWithResultAsync(new GameState(), token);
                if (pokemonData is not null)
                {
                    await channel.Writer.WriteAsync(new StarterPokemonData() { Game = RomHandleFor("Pokemon SoulSilver"), GameId = gameId.ToString(), PokemonData = pokemonData });
                }
            });
        }

        private void CreateAndHuntTask(int gameId, int placement, Channel<StarterPokemonData> channel, CancellationToken token)
        {
            var _ = Task.Run(async () => {
                var context = botFactory.BuildGameContext(RomHandleFor("Pokemon SoulSilver"), gameId, placement);
                IBot createBot = botFactory.CreateBot(context);
                CreateSaveEvent saveEvent = new CreateSaveEvent(createBot, context, serviceProvider);
                await saveEvent.ExecuteAsync(new GameState());
                await Task.Delay(10000);
                IBot shinyBot = botFactory.CreateBot(context);
                HgssShinyStarterEvent shinyEvent = shinyStarterEventFactory.CreateEvent(shinyBot, GameFlavor.HGSS);
                var pokemonData = await shinyEvent.ExecuteWithResultAsync(new GameState(), token);
                if (pokemonData is not null)
                {
                    await Task.Delay(10000);
                    await channel.Writer.WriteAsync(new StarterPokemonData() { Game = RomHandleFor("Pokemon SoulSilver"), GameId = gameId.ToString(), PokemonData = pokemonData });
                }
            });
        }
        private async Task<ShinyStarterGameState> CreateOrLoadGameState()
        {
            var state = await _collection.Find<BaseGameState>(x => x.Name == "1-1 Starters" && x.Game == GameFlavor.HGSS).FirstOrDefaultAsync();
            if(state is not null)
            {
                return (state as ShinyStarterGameState)!;
            }
            var newState = new ShinyStarterGameState() { Name = "1-1 Starters", Game = GameFlavor.HGSS, Id = Guid.NewGuid() };
            newState.State = ShinyStarterStates.CreateOppisiteSaves;
            newState.PokemonData = new List<StarterPokemonData>();
            await SaveStateAsync(newState);
            return newState;
        }

        private async Task SaveStateAsync(ShinyStarterGameState state)
        {
            var filter = Builders<BaseGameState>.Filter.Eq(x => x.Id, state.Id);
            var options = new ReplaceOptions { IsUpsert = true };
            await _collection.ReplaceOneAsync(filter, state, options);
        }
    }
}
