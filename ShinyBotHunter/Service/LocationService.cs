using Microsoft.Extensions.Options;
using ShinyBotHunter.Bot;
using ShinyBotHunter.Games;
using ShinyBotHunter.Pathfinding;

namespace ShinyBotHunter.Service
{
    /// <summary>
    /// A single read of where the player currently is.
    ///
    /// <paramref name="MapId"/> is the raw game map id — the value <c>PathfindingService.Pathfind</c>
    /// takes and the one the <c>M.</c> warp guard compares against. <paramref name="BotMapId"/> is the
    /// internal layout id many physical locations share, and is null when the raw id is not in the
    /// map lookup table.
    /// </summary>
    public record LocationInfo(
        int MapId,
        int? BotMapId,
        string? InternalName,
        int X,
        int Y,
        byte Facing,
        string FacingName);

    /// <summary>
    /// Dev-only position readout. Reads over the memory server, which is a separate path from the
    /// command server, so this keeps working while a macro recording or a bot event is in progress.
    ///
    /// Gated by <see cref="ApplicationSettings.EnableMacroRecording"/> — the same dev-mode flag the
    /// dashboard's Dev Mode tab is gated on. The controller enforces it too.
    /// </summary>
    public class LocationService
    {
        private readonly BotService botService;
        private readonly MapService mapService;
        private readonly ApplicationSettings settings;

        public LocationService(BotService botService, MapService mapService, IOptions<ApplicationSettings> appSettings)
        {
            this.botService = botService;
            this.mapService = mapService;
            this.settings = appSettings.Value;
        }

        public bool Enabled => settings.EnableMacroRecording;

        public async Task<LocationInfo> GetLocationAsync(int pos)
        {
            IBot bot = botService.GetBotAt(pos)
                ?? throw new InvalidOperationException($"No emulator running at position {pos}.");

            // Sequential rather than concurrent: each read is serviced on the emulator's frame
            // thread, and the memory client is not built for parallel in-flight requests.
            ushort mapId = await bot.Overworld.GetMapIdAsync(CancellationToken.None);
            (short x, short y) = await bot.Overworld.GetPositionAsync(CancellationToken.None);
            Direction facing = await bot.Overworld.GetFacingAsync(CancellationToken.None);

            (int? botMapId, string? internalName) = LookupMap(mapId);

            return new LocationInfo(mapId, botMapId, internalName, x, y, (byte)facing, DescribeFacing(facing));
        }

        /// <summary>
        /// MapService.GetMapLookup indexes its dictionary unguarded, so an unmapped raw id throws.
        /// A map the lookup table has never heard of is a normal thing to stand on while exploring,
        /// not a server error — report the raw id and leave the name fields empty.
        /// </summary>
        private (int? BotMapId, string? InternalName) LookupMap(int mapId)
        {
            try
            {
                MapLookup lookup = mapService.GetMapLookup(mapId);
                return (lookup.BotMapId, lookup.InternalName);
            }
            catch (KeyNotFoundException)
            {
                return (null, null);
            }
        }

        private static string DescribeFacing(Direction facing)
            => Enum.IsDefined(facing) ? facing.ToString() : ((byte)facing).ToString();
    }
}
