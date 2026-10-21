using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Options;
using ShinyBotHunter.Context;
using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Service
{
    /// <summary>
    /// Identifies ROMs by parsing their headers, so the bot never has to infer a game from a filename.
    /// Renaming a ROM must not change behaviour, and a mislabeled ROM must be detected rather than
    /// believed — both fall out of reading the header instead of the name.
    ///
    /// Identity comes from the header alone, deliberately. Checking dumps against known-good hashes was
    /// tried and dropped: it rejected ROMs that demonstrably work, because dumps vary in secure-area
    /// handling and header patches without the game code differing at all. The header states what the
    /// game is, which is the question being asked, and it answers it without a reference table to keep
    /// current.
    /// </summary>
    public class RomInfoService
    {
        // GBA header. Fields are contiguous from 0xA0: title(12) code(4) maker(2), then a fixed 0x96
        // that identifies a real cartridge header, and the revision byte further along.
        private const int GbaTitleOffset = 0xA0;
        private const int GbaGameCodeOffset = 0xAC;
        private const int GbaMakerOffset = 0xB0;
        private const int GbaMagicOffset = 0xB2;
        private const int GbaRevisionOffset = 0xBC;
        private const byte GbaMagic = 0x96;

        // NDS header, same fields in a different layout.
        private const int NdsTitleOffset = 0x00;
        private const int NdsGameCodeOffset = 0x0C;
        private const int NdsMakerOffset = 0x10;
        private const int NdsRevisionOffset = 0x1E;

        /// <summary>
        /// Where the loaded game's 4-byte game code can be read back through the emulator, to confirm
        /// the core is running what we think we launched. On GBA the cartridge is memory-mapped so the
        /// header is read in place; on NDS it is mirrored into main RAM at boot.
        /// </summary>
        public static uint RuntimeGameCodeAddress(ConsoleType console) => console switch
        {
            ConsoleType.GBA => 0x080000AC,
            ConsoleType.DS => 0x027FFE0C,
            _ => throw new ArgumentOutOfRangeException(nameof(console), console, "No runtime header address known.")
        };

        /// <summary>First three characters of the game code identify the title; the fourth is region.</summary>
        private static readonly Dictionary<string, (string Name, GameFlavor? Flavor)> GameCodes = new()
        {
            ["AXV"] = ("Pokemon Ruby", GameFlavor.RS),
            ["AXP"] = ("Pokemon Sapphire", GameFlavor.RS),
            ["IPK"] = ("Pokemon HeartGold", GameFlavor.HGSS),
            ["IPG"] = ("Pokemon SoulSilver", GameFlavor.HGSS),
            // Recognised so the dashboard can name them, but no flavor yet: each becomes its own
            // GameFlavor when implemented, and both relocate their save blocks via DMA.
            ["BPE"] = ("Pokemon Emerald", null),
            ["BPR"] = ("Pokemon FireRed", null),
            ["BPG"] = ("Pokemon LeafGreen", null),
        };

        private static readonly Dictionary<char, string> Regions = new()
        {
            ['E'] = "USA/English",
            ['J'] = "Japan",
            ['P'] = "Europe",
            ['D'] = "German",
            ['F'] = "French",
            ['I'] = "Italian",
            ['S'] = "Spanish",
            ['K'] = "Korea",
        };

        /// <summary>
        /// The revision each flavor is actually exercised on. Everything else still runs — it is only
        /// flagged as unproven, so promoting a revision later is a change here plus recorded macros,
        /// never a refactor.
        /// </summary>
        private static readonly Dictionary<GameFlavor, int> TestedRevisions = new()
        {
            [GameFlavor.HGSS] = 0,
            [GameFlavor.RS] = 2,
        };

        private readonly string romDirectory;
        private readonly ILogger<RomInfoService> logger;

        // Rehashing on every lookup would re-read gigabytes, so a scan is an explicit operation:
        // once at startup, and again only when someone asks for a refresh. Everything in between is
        // served from this snapshot.
        private volatile IReadOnlyList<RomInfo> snapshot = Array.Empty<RomInfo>();

        // Survives a refresh so rescanning only rehashes files that are new or actually changed.
        private readonly ConcurrentDictionary<string, (long Size, DateTime WriteUtc, RomInfo Info)> cache = new();

        public RomInfoService(IOptions<ApplicationSettings> appSettings, ILogger<RomInfoService> logger)
        {
            this.romDirectory = appSettings.Value.ROMBaseDirectory;
            this.logger = logger;
        }

        /// <summary>Scans and hashes the ROM folder once at application start.</summary>
        public Task InitAsync() => RefreshAsync();

        /// <summary>
        /// Rescans the ROM folder, picking up files added or replaced since the last scan. This is the
        /// only thing that reads ROMs from disk — lookups never do.
        /// </summary>
        public async Task<IReadOnlyList<RomInfo>> RefreshAsync()
        {
            if (!Directory.Exists(romDirectory))
            {
                logger.LogWarning("ROM directory {Dir} does not exist.", romDirectory);
                snapshot = Array.Empty<RomInfo>();
                return snapshot;
            }

            string[] paths = Directory.EnumerateFiles(romDirectory)
                .Where(p =>
                {
                    string ext = Path.GetExtension(p).ToLowerInvariant();
                    return ext == ".gba" || ext == ".nds";
                })
                .ToArray();

            // Hashing is IO-bound and files run to 128 MB apiece, so read them concurrently rather
            // than serially stalling startup.
            RomInfo?[] scanned = await Task.WhenAll(paths.Select(p => Task.Run(() => TryRead(p))));

            snapshot = scanned
                .Where(r => r is not null)
                .Select(r => r!)
                .OrderBy(r => r.Handle, StringComparer.OrdinalIgnoreCase)
                .ToList();

            logger.LogInformation("Scanned {Count} ROM(s) in {Dir}.", snapshot.Count, romDirectory);
            return snapshot;
        }

        /// <summary>Every ROM found by the last scan. Does not touch the disk.</summary>
        public IReadOnlyList<RomInfo> GetAll() => snapshot;

        /// <summary>
        /// Resolves the file handle the dashboard passes around (a ROM's base filename) to its real
        /// identity. The handle says which file; the returned info says what game that file is.
        /// </summary>
        public RomInfo? GetByHandle(string handle)
            => GetAll().FirstOrDefault(r => string.Equals(r.Handle, handle, StringComparison.OrdinalIgnoreCase));

        /// <summary>Detected ROMs for a game, e.g. "Pokemon HeartGold" — replaces matching on filenames.</summary>
        public IReadOnlyList<RomInfo> FindByGame(string gameName)
            => GetAll().Where(r => string.Equals(r.GameName, gameName, StringComparison.OrdinalIgnoreCase)).ToList();

        private RomInfo? TryRead(string path)
        {
            FileInfo fi = new(path);
            if (cache.TryGetValue(path, out var entry)
                && entry.Size == fi.Length
                && entry.WriteUtc == fi.LastWriteTimeUtc)
            {
                return entry.Info;
            }

            try
            {
                RomInfo info = ReadHeader(path);
                cache[path] = (fi.Length, fi.LastWriteTimeUtc, info);
                LogFindings(info);
                return info;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not read ROM header from {Path}; skipping.", path);
                return null;
            }
        }

        private static RomInfo ReadHeader(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            bool isGba = ext == ".gba";

            using FileStream fs = File.OpenRead(path);

            string title, code, maker;
            int revision;

            if (isGba)
            {
                byte magic = ReadBytes(fs, GbaMagicOffset, 1)[0];
                if (magic != GbaMagic)
                    throw new InvalidDataException($"Not a GBA cartridge header (0x{magic:X2} at 0x{GbaMagicOffset:X} != 0x96).");

                title = Ascii(ReadBytes(fs, GbaTitleOffset, 12));
                code = Ascii(ReadBytes(fs, GbaGameCodeOffset, 4));
                maker = Ascii(ReadBytes(fs, GbaMakerOffset, 2));
                revision = ReadBytes(fs, GbaRevisionOffset, 1)[0];
            }
            else
            {
                title = Ascii(ReadBytes(fs, NdsTitleOffset, 12));
                code = Ascii(ReadBytes(fs, NdsGameCodeOffset, 4));
                maker = Ascii(ReadBytes(fs, NdsMakerOffset, 2));
                revision = ReadBytes(fs, NdsRevisionOffset, 1)[0];
            }

            if (code.Length < 4)
                throw new InvalidDataException($"Truncated game code '{code}'.");

            string prefix = code.Substring(0, 3);
            char regionChar = code[3];

            (string name, GameFlavor? flavor) = GameCodes.TryGetValue(prefix, out var hit)
                ? hit
                : ($"Unknown ({code})", (GameFlavor?)null);

            string language = Regions.TryGetValue(regionChar, out string? lang) ? lang : $"Unknown ({regionChar})";

            RevisionSupport support = flavor is not null
                && TestedRevisions.TryGetValue(flavor.Value, out int tested)
                && tested == revision
                    ? RevisionSupport.Tested
                    : RevisionSupport.SupportedUntested;

            return new RomInfo(
                Handle: Path.GetFileNameWithoutExtension(path),
                FilePath: path,
                Extension: ext,
                Console: isGba ? ConsoleType.GBA : ConsoleType.DS,
                Flavor: flavor,
                GameName: name,
                GameCode: code,
                Title: title,
                Language: language,
                Revision: revision,
                RevisionSupport: support);
        }

        private void LogFindings(RomInfo info)
        {
            if (info.IsKnownGame && info.RevisionSupport == RevisionSupport.SupportedUntested)
            {
                logger.LogWarning(
                    "ROM {Handle} is {Game}, an untested revision. It will run, but macros and addresses " +
                    "are only verified against rev{Tested}.",
                    info.Handle, info.DisplayName, TestedRevisions[info.Flavor!.Value]);
            }
        }

        private static byte[] ReadBytes(FileStream fs, int offset, int count)
        {
            fs.Seek(offset, SeekOrigin.Begin);
            byte[] buffer = new byte[count];
            int read = fs.Read(buffer, 0, count);
            if (read != count)
                throw new InvalidDataException($"File too short to contain a header (wanted {count} bytes at 0x{offset:X}).");
            return buffer;
        }

        private static string Ascii(byte[] bytes) => Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ');
    }
}
