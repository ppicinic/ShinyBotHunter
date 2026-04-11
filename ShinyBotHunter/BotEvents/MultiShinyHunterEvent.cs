using ShinyBotHunter.Bot;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using System.Threading.Channels;

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

    public class ShinyStarterGameState
    {
        // Persist to MongoDB
        public required string Name { get; set; }
        public ShinyStarterStates State { get; set; }
        public List<StarterPokemonData> PokemonData { get; set; } = null!;
        public Dictionary<int, bool> GameIds { get; set; } = new Dictionary<int, bool>();
        // Active memory only do not persist.
        //public Dictionary<int, IBot> Bots { get; set; } = new Dictionary<int, IBot>();
        public Dictionary<int, int> Placements { get; set; } = new Dictionary<int, int>();
        //public Dictionary<int, CancellationToken> CancellationTokens { get; set; } = new Dictionary<int, CancellationToken>();

    }
    public class MultiShinyHunterEvent : IBotEvent<List<StarterPokemonData>>
    {
        private ShinyStarterGameState gameState = new ShinyStarterGameState() { Name = "Blank"};
        private int maxEmulators;
        private static readonly object _locker=new object();

        public MultiShinyHunterEvent(int maxEmulators = 14)
        {
            this.maxEmulators = maxEmulators;
        }
        public async Task<List<StarterPokemonData>> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            gameState = CreateOrLoadGameState();
            while (gameState.State != ShinyStarterStates.Finished)
            {
                if (gameState.State == ShinyStarterStates.CreateOppisiteSaves)
                {
                    await CreateOppositeSaves();
                }
                else if(gameState.State == ShinyStarterStates.HuntFirstShiny)
                {
                    await HuntFirstShiny();
                }
                // is this stage needed? or can the next stage do it anyway?
                else if(gameState.State == ShinyStarterStates.CreatePlayingSaves)
                {
                    await CreatePlayingSaves();
                }
                else if(gameState.State == ShinyStarterStates.HuntRestOfShinies)
                {
                    await HuntRestOfShinies();
                }
            }
            return gameState.PokemonData;
        }

        private async Task CreateOppositeSaves()
        {
            List<Task> tasks = new List<Task>();
            for(int i = 0; i < maxEmulators; i++)
            {
                var botFactory = new BotFactory();
                var context = botFactory.BuildGameContext("Pokemon HeartGold", i + 1, i);
                IBot bot = botFactory.CreateBot(context);
                CreateSaveEvent createSave = new CreateSaveEvent(bot, context);
                tasks.Add(createSave.ExecuteAsync());
                await Task.Delay(5000);
            }
            await Task.WhenAll(tasks);
            gameState.State = ShinyStarterStates.HuntFirstShiny;
            // TODO persist to mongodb

            await Task.Delay(10000);
        }

        private async Task HuntFirstShiny()
        {
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            List<Task<PokemonData>> tasks = new List<Task<PokemonData>>();
            List<IBot> bots = new List<IBot>();
            for (int i = 0; i < maxEmulators; i++)
            {
                var botFactory = new BotFactory();
                var context = botFactory.BuildGameContext("Pokemon HeartGold", i + 1, i);
                IBot bot = botFactory.CreateBot(context);
                bots.Add(bot);
                HgssShinyStarterEvent hgssShinyStarter = new HgssShinyStarterEvent(bot, GameFlavor.HGSS);
                tasks.Add(hgssShinyStarter.ExecuteAsync(cancellationTokenSource.Token));
                await Task.Delay(5000);
            }
            var task = await Task.WhenAny(tasks);
            var pokemonData = await task;
            gameState.PokemonData.Add(new StarterPokemonData() { PokemonData = pokemonData, Game = "Pokemon HeartGold", GameId = (tasks.IndexOf(task) + 1).ToString()});
            gameState.State = ShinyStarterStates.HuntRestOfShinies;
            // TODO persist to mongodb
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

        private async Task CreatePlayingSaves()
        {
            List<Task> tasks = new List<Task>();
            for (int i = 0; i < maxEmulators; i++)
            {
                var botFactory = new BotFactory();
                var context = botFactory.BuildGameContext("Pokemon SoulSilver", i + 1, i);
                IBot bot = botFactory.CreateBot(context);
                CreateSaveEvent createSave = new CreateSaveEvent(bot, context);
                tasks.Add(createSave.ExecuteAsync());
                await Task.Delay(5000);
            }
            await Task.WhenAll(tasks);
            for(int i = 0;i < maxEmulators; i++)
            {
                gameState.GameIds.Add(i+1, false);
            }
            gameState.State = ShinyStarterStates.HuntRestOfShinies;
            // persist mongodb
            await Task.Delay(10000);
        }

        private async Task HuntRestOfShinies()
        {
            Channel<StarterPokemonData> channel = Channel.CreateUnbounded<StarterPokemonData>();
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
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
                    //TODO persist mongodb
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
                // TODO BEFORE ANYTHING PERSIST TO MONGODB IN CASE IT CRASHES YOU CAN RESUME
                if (IsComplete())
                {
                    if (!complete)
                    {
                        complete = true;
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
                var botFactory = new BotFactory();
                var context = botFactory.BuildGameContext("Pokemon SoulSilver", gameId, placement);
                IBot bot = botFactory.CreateBot(context);
                var e = new HgssShinyStarterEvent(bot, context.GameFlavor);
                var pokemonData = await e.ExecuteAsync(token);
                if (pokemonData is not null)
                {
                    await channel.Writer.WriteAsync(new StarterPokemonData() { Game = "Pokemon SoulSilver", GameId = gameId.ToString(), PokemonData = pokemonData });
                }
            });
        }

        private void CreateAndHuntTask(int gameId, int placement, Channel<StarterPokemonData> channel, CancellationToken token)
        {
            var _ = Task.Run(async () => {
                var botFactory = new BotFactory();
                var context = botFactory.BuildGameContext("Pokemon SoulSilver", gameId, placement);
                IBot createBot = botFactory.CreateBot(context);
                CreateSaveEvent saveEvent = new CreateSaveEvent(createBot, context);
                await saveEvent.ExecuteAsync();
                await Task.Delay(10000);
                IBot shinyBot = botFactory.CreateBot(context);
                HgssShinyStarterEvent shinyEvent = new HgssShinyStarterEvent(shinyBot, GameFlavor.HGSS);
                var pokemonData = await shinyEvent.ExecuteAsync(token);
                if (pokemonData != null)
                {
                    await Task.Delay(10000);
                    await channel.Writer.WriteAsync(new StarterPokemonData() { Game = "Pokemon SoulSilver", GameId = gameId.ToString(), PokemonData = pokemonData });
                }
            });
        }
        private ShinyStarterGameState CreateOrLoadGameState()
        {
            var newState = new ShinyStarterGameState() { Name = "HGSS Shiny Starter Hunt"};
            //newState.State = ShinyStarterStates.HuntFirstShiny;
            newState.PokemonData = new List<StarterPokemonData>();
            return newState;
        }
    }
}
