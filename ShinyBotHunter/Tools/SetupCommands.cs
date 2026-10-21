using Microsoft.Extensions.Options;
using ShinyBotHunter.Pathfinding;
using ShinyBotHunter.Scripts;
using ShinyBotHunter.Scripts.Maps;

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
                    await new GraphProcessor(MapService.Instance, new PathfindingService(MapService.Instance, GraphService.Instance), outputDir).CreateBaseGraph();
                    return true;
                default:
                    return false;
            }
        }
    }
}
