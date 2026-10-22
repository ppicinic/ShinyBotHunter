using Discord;
using Microsoft.EntityFrameworkCore;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using System.Threading.Channels;

namespace ShinyBotHunter.Persistence.PostgreSQL
{
    public class PostgreSQLEncounterDao : IEncounterDao, IAsyncDisposable
    {
        private readonly IDbContextFactory<PokemonDbContext> _contextFactory;
        private readonly ILogger<PostgreSQLEncounterDao> _logger;
        private Channel<List<PersistPokemonData>> ProducerQueue { get; }

        public PostgreSQLEncounterDao(IDbContextFactory<PokemonDbContext> contextFactory, ILogger<PostgreSQLEncounterDao> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
            ProducerQueue = Channel.CreateUnbounded<List<PersistPokemonData>>();
        }

        public void Init()
        {
            // Fire and forget background processor
            _ = Task.Run(() => BackgroundLoop.RunSupervisedAsync(ProcessAsync, _logger, nameof(PostgreSQLEncounterDao)));
        }

        public async Task ProcessAsync()
        {
            await foreach (var item in ProducerQueue.Reader.ReadAllAsync())
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                foreach (var pokemonData in item)
                {
                    var writeData = new PersistPokemonDataDao(pokemonData);
                    await context.AddAsync(writeData);
                }
                await context.SaveChangesAsync();
            }
        }

        public async Task<List<CountRow>> GetCountsAsync(GameFlavor game)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Database
                .SqlQuery<CountRow>($@"
            SELECT ""Milestone"" AS ""Milestone"",
                   ""GameFileName"" AS ""GameFileName"",
                   ""FormCode"" AS ""FormCode"",
                   ""Species""::int AS ""Species"",
                   COUNT(*) AS ""Encounters"",
                   COUNT(*) FILTER (WHERE ""IsShiny"") AS ""ShinyEncounters""
            FROM ""encounters""
            WHERE ""Game"" = {(int)game}
            GROUP BY 1, 2, 3, 4")
                .ToListAsync();
        }

        // One statement, two groupings: per ROM copy against that copy's last shiny, and per phase against the
        // last shiny across all copies. GROUPING SETS cannot do it because the since-filter differs per grouping.
        public async Task<List<ProgressRow>> GetPhaseProgressAsync(GameFlavor game)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Database
                .SqlQuery<ProgressRow>($@"
            WITH per_file AS (
                SELECT ""Milestone"", ""GameFileName"", MAX(""Timestamp"") FILTER (WHERE ""IsShiny"") AS last_shiny_at
                FROM ""encounters""
                WHERE ""Game"" = {(int)game}
                GROUP BY 1, 2
            ), per_phase AS (
                SELECT ""Milestone"", MAX(last_shiny_at) AS last_shiny_at
                FROM per_file
                GROUP BY 1
            )
            SELECT e.""Milestone"" AS ""Milestone"", e.""GameFileName"" AS ""GameFileName"", COUNT(*)::int AS ""SinceLastShiny""
            FROM ""encounters"" e
            JOIN per_file l ON l.""Milestone"" = e.""Milestone"" AND l.""GameFileName"" = e.""GameFileName""
            WHERE e.""Game"" = {(int)game} AND (l.last_shiny_at IS NULL OR e.""Timestamp"" > l.last_shiny_at)
            GROUP BY 1, 2
            UNION ALL
            SELECT e.""Milestone"", NULL, COUNT(*)::int
            FROM ""encounters"" e
            JOIN per_phase p ON p.""Milestone"" = e.""Milestone""
            WHERE e.""Game"" = {(int)game} AND (p.last_shiny_at IS NULL OR e.""Timestamp"" > p.last_shiny_at)
            GROUP BY 1")
                .ToListAsync();
        }

        public async Task<double> GetEncountersPerHour(GameFlavor game, string phase, string? gameFileName)
        {
            using var _context = await _contextFactory.CreateDbContextAsync();
            // Define the 24-hour cutoff
            var cutoff = DateTime.UtcNow.AddHours(-24);

            // Bins are ordered and disjoint, so the first bin's MIN and the last bin's MAX are the window bounds.
            var hourlyData = await _context.Database
                .SqlQuery<EncounterBin>($@"
            SELECT 
                DATE_BIN(INTERVAL '1 hour', ""Timestamp"", TIMESTAMP '1970-01-01') AS Time, 
                COUNT(*)::int AS HourlyCount, 
                MIN(""Timestamp"") AS FirstTime, 
                MAX(""Timestamp"") AS LastTime 
            FROM ""encounters"" 
            WHERE ""Timestamp"" >= {cutoff} 
              AND ""Game"" = {(int)game} 
              AND ""Milestone"" = {phase} 
              AND ({gameFileName}::text IS NULL OR ""GameFileName"" = {gameFileName}) 
            GROUP BY 1 
            ORDER BY 1")
                .ToListAsync();

            if (hourlyData.Count == 0 || hourlyData[0].FirstTime == hourlyData[^1].LastTime) return 0.0;

            return CalculateEncounters(hourlyData, hourlyData[0].FirstTime, hourlyData[^1].LastTime);
        }

        // DTO for raw SQL results
        public record EncounterBin(DateTime Time, int HourlyCount, DateTime FirstTime, DateTime LastTime);

        internal static double CalculateEncounters(List<EncounterBin> bins, DateTime firstTime, DateTime lastTime)
        {
            const double OneHourMs = 3600000.0;

            if (bins.Count == 1)
            {
                var diff = (lastTime - firstTime).TotalMilliseconds;
                return (OneHourMs / diff) * bins[0].HourlyCount;
            }

            double sum = 0.0;

            // Handle First Bin (Weighted by time since start of bin)
            var firstBin = bins[0];
            var firstBinEnd = firstBin.Time.AddHours(1);
            var firstDiff = (firstBinEnd - firstTime).TotalMilliseconds;
            sum += (OneHourMs / firstDiff) * firstBin.HourlyCount;

            // Handle Last Bin (Weighted by time since bin start)
            var lastBin = bins[^1];
            var lastDiff = (lastTime - lastBin.Time).TotalMilliseconds;
            sum += (OneHourMs / lastDiff) * lastBin.HourlyCount;

            // Handle Middle Bins (Full hours)
            for (int i = 1; i < bins.Count - 1; i++)
            {
                sum += bins[i].HourlyCount;
            }

            return sum / bins.Count;
        }

        public async Task<List<PokemonData>> GetLatestEncounters(GameFlavor game)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var latest = await context.Encounters
                .Where(x => x.Game == game)
                .OrderByDescending(x => x.Timestamp)
                .Take(20)
                .ToListAsync();

            return latest.Select(p => new PokemonData
            {
                Species = (ushort)p.Species,
                Name = p.Name,
                Ability = p.Ability,
                Gender = p.Gender,
                IVs = (uint)p.IVs,
                Nature = p.Nature,
                PID = (uint)p.PID,
                SID = (ushort)p.SID,
                TID = (ushort)p.TID,
                FormCode = p.FormCode,
                Level = p.Level
            }).ToList();
        }

        public async Task WriteToQueueAsync(List<PersistPokemonData> pokemonData) => await ProducerQueue.Writer.WriteAsync(pokemonData);

        public async ValueTask DisposeAsync()
        {
            ProducerQueue.Writer.TryComplete();
            await ProducerQueue.Reader.Completion;
        }
    }
}
