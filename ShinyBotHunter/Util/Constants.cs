namespace ShinyBotHunter.Util
{
    public class Constant
    {
        public const byte MAP_Free = 0;
        public const byte MAP_Portal1 = 1;
        public const byte MAP_Portal2 = 2;
        public const byte MAP_Grass = 3;
        public const byte MAP_Surf = 4;
        public const byte MAP_RightLedge = 5;
        public const byte MAP_LeftLedge = 6;
        public const byte MAP_DownLedge = 7;
        public const byte MAP_RockClimb = 0x08;
        public const byte MAP_Whirlpool = 0x09;
        public const byte MAP_BridgeStart = 0x0A;
        public const byte MAP_BridgeMiddle = 0x0B;
        public const byte MAP_Bridge = 0x0C;
        public const byte MAP_Waterfall = 0x0D;
        public const byte MAP_Ice = 0x0E;
        public const byte MAP_Cut = 0x0F;
        public const byte MAP_RockSmash = 0x10;
        public const byte MAP_WalkingTrainer = 0x11;
        public const byte MAP_Blocked = 0x80;

        public static readonly Dictionary<byte, string> NATURE_NAMES = new Dictionary<byte, string>() {
            { 0, "Hardy" }, {  1, "Lonely" }, {  2, "Brave" }, {  3, "Adamant" }, {  4, "Naughty" },
            { 5, "Bold" }, {  6, "Docile" }, {  7, "Relaxed" }, {  8, "Impish" }, {  9, "Lax" },
            { 10, "Timid" }, {  11, "Hasty" }, {  12, "Serious" }, {  13, "Jolly" }, {  14, "Naive" },
            { 15, "Modest" }, { 16, "Mild" }, {  17, "Quiet" }, {  18, "Bashful" }, {  19, "Rash" },
            { 20, "Calm" }, {  21, "Gentle" }, {  22, "Sassy" }, {  23, "Careful" }, {  24, "Quirky" } };
    }
}
