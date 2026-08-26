using System.Collections.Concurrent;
using System.Security.Cryptography;
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
    /// Scanning also hashes each file and compares against known-good dumps. That matters more than it
    /// looks: a patched ROM redirects its entry point into injected code, which moves the game out from
    /// under the symbol-derived address tables and makes any legality verdict meaningless.
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
        /// Canonical dump hashes, lowercase SHA1. GBA entries come from pokebot-gen3's ROM table; NDS
        /// entries from the pret decompilation projects' own <c>rom.sha1</c> files. Only games listed
        /// here can be judged; anything absent reports <see cref="RomVerification.NoReferenceData"/>
        /// rather than being called suspect.
        ///
        /// A ROM can fail to match and still be a perfectly playable dump — NDS dumps in particular
        /// vary in secure-area handling and header patches without the game code differing. That is why
        /// a mismatch warns rather than blocks, and why <see cref="ApplicationSettings.TrustedRomHashes"/>
        /// exists.
        /// </summary>
        private static readonly Dictionary<string, string> KnownGoodHashes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["f28b6ffc97847e94a6c21a63cacf633ee5c8df1e"] = "Pokemon Ruby (USA) rev0",
            ["610b96a9c9a7d03d2bafb655e7560ccff1a6d894"] = "Pokemon Ruby (USA, Europe) rev1",
            ["5b64eacf892920518db4ec664e62a086dd5f5bc8"] = "Pokemon Ruby (USA, Europe) rev2",
            ["3ccbbd45f8553c36463f13b938e833f652b793e4"] = "Pokemon Sapphire (USA) rev0",
            ["4722efb8cd45772ca32555b98fd3b9719f8e60a9"] = "Pokemon Sapphire (USA, Europe) rev1",
            ["89b45fb172e6b55d51fc0e61989775187f6fe63c"] = "Pokemon Sapphire (USA, Europe) rev2",
            ["4fcded0e2713dc03929845de631d0932ea2b5a37"] = "Pokemon HeartGold (USA)",
            ["f8dc38ea20c17541a43b58c5e6d18c1732c7e582"] = "Pokemon SoulSilver (USA)",
        };

        /// <summary>Games we hold reference hashes for. Others cannot be judged either way.</summary>
        private static readonly HashSet<GameFlavor> FlavorsWithReferenceHashes = new()
        {
            GameFlavor.RS,
            GameFlavor.HGSS,
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
        private readonly IReadOnlyDictionary<string, string> trustedHashes;
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
            this.trustedHashes = appSettings.Value.TrustedRomHashes ?? new Dictionary<string, string>();
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

        public IReadOnlyList<RomInfo> FindByFlavor(GameFlavor flavor)
            => GetAll().Where(r => r.Flavor == flavor).ToList();

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
                RomInfo info = ReadHeader(path, fi, trustedHashes);
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

        private static RomInfo ReadHeader(string path, FileInfo fi, IReadOnlyDictionary<string, string> trusted)
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

            fs.Seek(0, SeekOrigin.Begin);
            string sha1 = Convert.ToHexString(SHA1.HashData(fs)).ToLowerInvariant();

            bool canVerify = flavor is not null && FlavorsWithReferenceHashes.Contains(flavor.Value);
            RomVerification verification;
            if (KnownGoodHashes.ContainsKey(sha1) || trusted.ContainsKey(sha1)) verification = RomVerification.Verified;
            else if (canVerify) verification = RomVerification.Unrecognized;
            else verification = RomVerification.NoReferenceData;

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
                Sha1: sha1,
                Verification: verification,
                RevisionSupport: support);
        }

        private void LogFindings(RomInfo info)
        {
            if (info.Verification == RomVerification.Unrecognized)
            {
                logger.LogWarning(
                    "ROM {Handle} identifies as {Game} but its SHA1 {Sha1} matches no canonical dump. " +
                    "If this ROM is known to work, add that hash to ApplicationSettings:TrustedRomHashes; " +
                    "otherwise treat it as suspect, since patched ROMs invalidate address tables and legality checks.",
                    info.Handle, info.DisplayName, info.Sha1);
            }

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
