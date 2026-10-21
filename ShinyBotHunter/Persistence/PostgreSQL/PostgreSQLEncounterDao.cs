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

        public async Task<(long, long)> GetAllEncountersForGame(GameFlavor game)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return (
                await context.Encounters.Where(x => x.Game == game).LongCountAsync(),
                await context.Encounters.Where(x => x.Game == game && x.IsShiny).LongCountAsync()
            );
        }

        public async Task<(long, long)> GetAllEncountersForGameName(string gameFileName)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return (
                await context.Encounters.Where(x => x.GameFileName == gameFileName).LongCountAsync(),
                await context.Encounters.Where(x => x.GameFileName == gameFileName && x.IsShiny).LongCountAsync()
            );
        }

        public async Task<(long, long)> GetAllPhaseEncountersForGame(GameFlavor game, string phase)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return (
                await context.Encounters.Where(x => x.Game == game && x.Milestone == phase).LongCountAsync(),
                await context.Encounters.Where(x => x.Game == game && x.Milestone == phase && x.IsShiny).LongCountAsync()
            );
        }

        public async Task<(long, long)> GetAllPhaseEncountersForGameName(string gameFileName, string phase)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return (
                await context.Encounters.Where(x => x.GameFileName == gameFileName && x.Milestone == phase).LongCountAsync(),
                await context.Encounters.Where(x => x.GameFileName == gameFileName && x.Milestone == phase && x.IsShiny).LongCountAsync()
            );
        }

        public async Task<(long, long)> GetAllPhaseFormEncountersForGame(GameFlavor game, string phase, string formCode)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return (
                await context.Encounters.Where(x => x.Game == game && x.Milestone == phase && x.FormCode == formCode).LongCountAsync(),
                await context.Encounters.Where(x => x.Game == game && x.Milestone == phase && x.FormCode == formCode && x.IsShiny).LongCountAsync()
            );
        }

        public async Task<(long, long)> GetAllPhaseFormEncountersForGameName(string gameFileName, string phase, string formCode)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return (
                await context.Encounters.Where(x => x.GameFileName == gameFileName && x.Milestone == phase && x.FormCode == formCode).LongCountAsync(),
                await context.Encounters.Where(x => x.GameFileName == gameFileName && x.Milestone == phase && x.FormCode == formCode && x.IsShiny).LongCountAsync()
            );
        }

        public async Task<(long, long)> GetAllFormEncountersForGame(GameFlavor game, string formCode)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return (
                await context.Encounters.Where(x => x.Game == game && x.FormCode == formCode).LongCountAsync(),
                await context.Encounters.Where(x => x.Game == game && x.FormCode == formCode && x.IsShiny).LongCountAsync()
            );
        }

        public async Task<(long, long)> GetAllFormEncountersForGameName(string gameFileName, string formCode)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return (
                await context.Encounters.Where(x => x.GameFileName == gameFileName && x.FormCode == formCode).LongCountAsync(),
                await context.Encounters.Where(x => x.GameFileName == gameFileName && x.FormCode == formCode && x.IsShiny).LongCountAsync()
            );
        }

        public async Task<int> GetCurrentPhaseCount(GameFlavor game, string phase)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var lastShiny = await context.Encounters
                .Where(x => x.Game == game && x.Milestone == phase && x.IsShiny)
                .OrderByDescending(x => x.Timestamp)
                .Select(x => (DateTime?)x.Timestamp)
                .FirstOrDefaultAsync();

            return await context.Encounters
                .Where(x => x.Game == game && x.Milestone == phase && (lastShiny == null || x.Timestamp > lastShiny))
                .CountAsync();
        }

        public async Task<int> GetCurrentPhaseCount(string gameFileName, string phase)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var lastShiny = await context.Encounters
                .Where(x => x.GameFileName == gameFileName && x.Milestone == phase && x.IsShiny)
                .OrderByDescending(x => x.Timestamp)
                .Select(x => (DateTime?)x.Timestamp)
                .FirstOrDefaultAsync();

            return await context.Encounters
                .Where(x => x.GameFileName == gameFileName && x.Milestone == phase && (lastShiny == null || x.Timestamp > lastShiny))
                .CountAsync();
        }

        public async Task<double> GetEncountersPerHour(GameFlavor game, string phase)
        {
            using var _context = await _contextFactory.CreateDbContextAsync();
            // Define the 24-hour cutoff
            var cutoff = DateTime.UtcNow.AddHours(-24);

            // 1. Get Hourly Bins using Raw SQL
            // We project into a simple anonymous or DTO type
            var hourlyData = await _context.Database
                .SqlQuery<EncounterBin>($@"
            SELECT 
                DATE_BIN(INTERVAL '1 hour', ""Timestamp"", TIMESTAMP '1970-01-01') AS Time, 
                COUNT(*)::int AS HourlyCount 
            FROM ""encounters"" 
            WHERE ""Timestamp"" >= {cutoff} 
              AND ""Game"" = {(int)game} 
              AND ""Milestone"" = {phase} 
            GROUP BY 1 
            ORDER BY 1")
                .ToListAsync();

            if (!hourlyData.Any()) return 0.0;

            // 2. Get First/Last timestamps using LINQ (Type-safe and efficient)
            var stats = await _context.Encounters
                .Where(e => e.Timestamp >= cutoff &&
                            e.Game == game &&
                            e.Milestone == phase)
                .GroupBy(_ => 1)
                .Select(g => new {
                    FirstTime = g.Min(e => e.Timestamp),
                    LastTime = g.Max(e => e.Timestamp)
                })
                .OrderBy(x => x.FirstTime)
                .FirstOrDefaultAsync();

            if (stats == null || stats.FirstTime == stats.LastTime) return 0.0;

            return CalculateEncounters(hourlyData, stats.FirstTime, stats.LastTime);
        }

        public async Task<double> GetEncountersPerHour(GameFlavor game, string phase, string gameFileName)
        {
            using var _context = await _contextFactory.CreateDbContextAsync();
            // Define the 24-hour cutoff
            var cutoff = DateTime.UtcNow.AddHours(-24);

            // 1. Get Hourly Bins using Raw SQL
            // We project into a simple anonymous or DTO type
            var hourlyData = await _context.Database
                .SqlQuery<EncounterBin>($@"
            SELECT 
                DATE_BIN(INTERVAL '1 hour', ""Timestamp"", TIMESTAMP '1970-01-01') AS Time, 
                COUNT(*)::int AS HourlyCount 
            FROM ""encounters"" 
            WHERE ""Timestamp"" >= {cutoff} 
              AND ""Game"" = {(int)game} 
              AND ""Milestone"" = {phase} 
              AND ""GameFileName"" = {gameFileName} 
            GROUP BY 1 
            ORDER BY 1")
                .ToListAsync();

            if (!hourlyData.Any()) return 0.0;

            // 2. Get First/Last timestamps using LINQ (Type-safe and efficient)
            var stats = await _context.Encounters
                .Where(e => e.Timestamp >= cutoff &&
                            e.Game == game &&
                            e.Milestone == phase &&
                            e.GameFileName == gameFileName)
                .GroupBy(_ => 1)
                .Select(g => new {
                    FirstTime = g.Min(e => e.Timestamp),
                    LastTime = g.Max(e => e.Timestamp)
                })
                .OrderBy(x => x.FirstTime)
                .FirstOrDefaultAsync();

            if (stats == null || stats.FirstTime == stats.LastTime) return 0.0;

            return CalculateEncounters(hourlyData, stats.FirstTime, stats.LastTime);
        }

        // DTO for raw SQL results
        public record EncounterBin(DateTime Time, int HourlyCount);

        private double CalculateEncounters(List<EncounterBin> bins, DateTime firstTime, DateTime lastTime)
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

        public async Task<(long, long)> GetAllPhaseSpeciesEncountersForGame(GameFlavor game, string phase, int species)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return (
                await context.Encounters.Where(x => x.Game == game && x.Milestone == phase && x.Species == species).LongCountAsync(),
                await context.Encounters.Where(x => x.Game == game && x.Milestone == phase && x.Species == species && x.IsShiny).LongCountAsync()
            );
        }

        public async Task<(long, long)> GetAllPhaseSpeciesEncountersForGameName(string gameFileName, string phase, int species)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return (
                await context.Encounters.Where(x => x.GameFileName == gameFileName && x.Milestone == phase && x.Species == species).LongCountAsync(),
                await context.Encounters.Where(x => x.GameFileName == gameFileName && x.Milestone == phase && x.Species == species && x.IsShiny).LongCountAsync()
            );
        }

        public async Task<(long, long)> GetAllSpeciesEncountersForGame(GameFlavor game, int species)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return (
                await context.Encounters.Where(x => x.Game == game && x.Species == species).LongCountAsync(),
                await context.Encounters.Where(x => x.Game == game && x.Species == species && x.IsShiny).LongCountAsync()
            );
        }

        public async Task<(long, long)> GetAllSpeciesEncountersForGameName(string gameFileName, int species)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return (
                await context.Encounters.Where(x => x.GameFileName == gameFileName && x.Species == species).LongCountAsync(),
                await context.Encounters.Where(x => x.GameFileName == gameFileName && x.Species == species && x.IsShiny).LongCountAsync()
            );
        }
    }
}
