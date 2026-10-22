using Microsoft.Extensions.Options;
using ShinyBotHunter.Games.Hgss;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Scripts;
using ShinyBotHunter.Scripts.Maps;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tools
{
    public static class SetupCommands
    {
        // Returns false when args name no setup command, so the caller starts the host.
        public static async Task<bool> TryRunAsync(string[] args)
        {
            if (args.Length == 0)
            {
                return false;
            }
            switch (args[0].ToLower())
            {
                case "--parsepokeapi":
                    var config = new ConfigurationBuilder()
                        .AddJsonFile("appsettings.json", optional: false)
                        .AddUserSecrets<Startup>()
                        .Build();
                    var settings = config.GetSection("ApplicationSettings").Get<ApplicationSettings>()!;
                    await new PokeApiParser(Options.Create(settings)).ScrapePokeApi();
                    return true;
                case "--generatemapdatahgss":
                    if (args.Length < 5)
                    {
                        Console.Error.WriteLine("Usage: --generatemapdatahgss <romDataDir> <decompDir> <headerCsv> <outputDir>");
                        Environment.ExitCode = 1;
                        return true;
                    }
                    string romDataDir = args[1];
                    string decompDir = args[2];
                    string headerCsv = args[3];
                    string outputDir = args[4];
                    var maps = new MapProcessor(romDataDir, decompDir, outputDir);
                    maps.ExtractMovePermissionsFile();
                    new MatrixProcessor(headerCsv, romDataDir, outputDir).GenerateAllMaps();
                    maps.GenerateMapFiles();
                    var mapService = new MapService(HgssProfile.DataDirectory);
                    var graphService = new GraphService(HgssProfile.DataDirectory);
                    var table = new HgssPointerService();
                    var pathfinding = new PathfindingService(mapService, graphService, table.TrainerX, table.TrainerY, table.Anchor, table.MapIdOffset);
                    await new GraphProcessor(mapService, pathfinding, outputDir).CreateBaseGraph();
                    return true;
                default:
                    return false;
            }
        }
    }
}
