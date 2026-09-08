using ShinyBotHunter.Macro;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Context
{
    public class GameContext
    {
        /// <summary>
        /// Which ROM file this emulator opened — a handle, not an identity. The bot copies a ROM per
        /// instance so each gets its own save; what game that file actually is comes from
        /// <see cref="Rom"/>, which is read from the header.
        /// </summary>
        public required string SourceGameName { get; set; }
        public int GameId { get; set; } = -1;
        public string GameName { get { 
            if(GameId == -1)
                {
                    return SourceGameName;
                }
                else
                {
                    return $"{SourceGameName}{GameId}";
                }
            } 
        }
        public int CommandPort { get; set; }
        public int MemoryPort { get; set; }
        public int DisplayPosition { get; set; }
        public int Width { get; set; } = 205;
        public int Height { get; set; } = 399;
        public int WidthOffset { get; set; } = 0;
        public ConsoleType ConsoleType { get; set; } = ConsoleType.DS;
        public GameFlavor GameFlavor { get; set; } = GameFlavor.HGSS;
        public bool SoundOn { get; set; }

        /// <summary>
        /// Header-derived identity of the source ROM. Null only when the ROM could not be identified,
        /// in which case the emulator can still be launched but flavor-dependent logic must refuse it.
        /// </summary>
        public RomInfo? Rom { get; set; }

        public int Revision => Rom?.Revision ?? 0;

        /// <summary>
        /// File extension for this console's ROMs. BizHawk picks its core from the extension — no core
        /// is ever named on the command line — so this is also what selects melonDS vs mGBA.
        /// </summary>
        public string RomExtension => ConsoleType switch
        {
            ConsoleType.GBA => ".gba",
            ConsoleType.DS => ".nds",
            _ => throw new InvalidOperationException($"No ROM extension known for console {ConsoleType}.")
        };


    }
}
