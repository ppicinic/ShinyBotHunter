using ShinyBotHunter.Macro;

namespace ShinyBotHunter.Context
{
    public class GameContext
    {
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
        public ConsoleType ConsoleType { get; set; } = ConsoleType.DS;
        public GameFlavor GameFlavor { get; set; } = GameFlavor.HGSS;


    }
}
