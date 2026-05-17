namespace ShinyBotHunter.Pathfinding
{
    public class CustomLoadZoneMappings
    {
        public static ushort[] LoadZone1 = { 9, 10, 6, 7, 8, 4, 5, 1, 2, 3, 0, 11, 12, 13, 14, 15, 16, 20, 19, 18, 17 }; // New Bark Town
        public static ushort[] LoadZone2 = { 123, 124, 125 }; // Pre Victory Road
        public static ushort[] LoadZone3 = { 115 }; // Pre E4
        public static ushort[] LoadZone4 = { 24, 22, 21, 23, 25, 26, 27, 28, 29 }; // Violet City
        public static ushort[] LoadZone5 = { 30, 31 }; // Ruins of Alph
        public static ushort[] LoadZone6 = { 32, 34, 33 }; //Azelea Town
        public static ushort[] LoadZone7 = { 37, 36, 35, 41, 40, 43, 42, 38, 39 }; //GoldenRod
        public static ushort[] LoadZone8 = { 45, 44, 46, 47, 48, 49, 53, 52, 50, 51 }; // Ecruteak
        public static ushort[] LoadZone9 = { 55, 54, 56, 57, 58, 59, 60, 61, 62, 63, 65, 67, 64, 66, 68, 69 }; //Cianwood
        public static ushort[] LoadZone10 = { 78, 81, 80, 77, 75, 73, 79, 76, 74, 72 }; // End game
        public static ushort[] LoadZone11 = { 82, 83, 84, 85, 101, 102, 103 }; //Mahogany
        public static ushort[] LoadZone12 = { 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114 }; // Blackthorn
        public static ushort[] LoadZone13 = { 88, 87, 89, 92, 90, 93, 91, 94 };
        public static ushort[] LoadZone14 = { 121, 122, 119, 117, 116, 118, 120 }; // Virdian
        public static ushort[] LoadZone15 = { 199, 200, 201, 202, 203, 204, 205, 206, 207, 188, 189, 187, 186 }; // Pallet Town - certain paths are blocked from load zone but /shrug
        public static ushort[] LoadZone16 = { 191, 190, 194, 192, 193, 195, 196, 197, 198 }; // Pewter
        public static ushort[] LoadZone17 = { 150, 151, 152, 141, 142, 145, 146, 147, 148, 149, 143, 144, 137, 138, 139, 140, 153 }; //Cerulean
        public static ushort[] LoadZone18 = { 133, 134, 135, 136 }; // Saffron
        public static ushort[] LoadZone19 = { 169, 171, 173, 170, 172, 174 }; // Celadon
        public static ushort[] LoadZone20 = { 175, 176, 177, 178, 179, 180, 181, 184, 182, 183, 185 }; // fuschia city
        public static ushort[] LoadZone21 = { 165, 166, 167, 168, 164, 161, 162, 163, 160, 159, 155, 154, 156, 157, 158 }; // lavender
        public static ushort[] LoadZone22 = { 130, 126, 128, 127, 129, 131, 132 };

        public static Dictionary<int, ushort[]> OverworldLoadZones = new Dictionary<int, ushort[]>()
        {
            {0, LoadZone1 },
            {1, LoadZone2 },
            {2, LoadZone3 },
            {3, LoadZone4 },
            {4, LoadZone5 },
            {5, LoadZone6 },
            {6, LoadZone7 },
            {7, LoadZone8 },
            {8, LoadZone9 },
            {9, LoadZone10 },
            {10, LoadZone11 },
            {11, LoadZone12 },
            {12, LoadZone13 },
            {13, LoadZone14 },
            {14, LoadZone15 },
            {15, LoadZone16 },
            {16, LoadZone17 },
            {17, LoadZone18 },
            {18, LoadZone19 },
            {19, LoadZone20 },
            {20, LoadZone21 },
            {21, LoadZone22 },
        };
    }
}
