using System.Text.RegularExpressions;

namespace ShinyBotHunter.Macro
{
    public class CommandPreprocessor
    {

        private static readonly Dictionary<string, (int X, int Y)> TouchShorthands = new()
        {
            { "Pokedex",  (45,  35)  },
            { "Pokemon",  (45,  75)  },
            { "Bag",      (45,  115) },
            { "Trainer",  (125, 35)  },
            { "Save",     (125, 75)  },
            { "Options",  (125, 115) },
            { "Run",      (125, 175) },
            { "MoveOne",  (70,  60)  },
            { "MoveTwo",  (170, 60)  },
            { "MoveThree", (70,  110) },
            { "MoveFour",    (170, 110) },
            { "NewMove",    (128, 70) },
            { "NewMoveOne",    (60, 70) },
            { "NewMoveTwo",    (190, 70) },
            { "NewMoveThree",    (60, 120) },
            { "NewMoveFour",    (190, 120) },
            { "Forget",    (100, 170) },
            { "DoNotLearn", (128, 140) },
            { "DoNotLearnComplete", (128, 70) },
            { "EvoMoveOne",    (60, 25) },
            { "EvoMoveTwo",    (60, 55) },
            { "EvoMoveThree",    (60, 85) },
            { "EvoMoveFour",    (60, 115) },
        };
        public int DefaultTapFrames { get; set; } = 3;

        private static readonly Regex TapRegex = new(@"T\.([^;:.]+)", RegexOptions.Compiled);
        private static readonly Regex RandomRegex = new(@"W\.R\[(\d+),(\d+)\]", RegexOptions.Compiled);
        private static readonly Regex TouchShorthandRegex = new(@"S\.([A-Za-z]+)(?:\.(\d+))?", RegexOptions.Compiled);
        private static readonly Regex HexTokenRegex = new(@"0x[0-9A-Fa-f]+", RegexOptions.Compiled);

        private readonly Random _random = new();

        public string Process(string macro)
        {
            string result = macro;
            result = ConvertHexToDecimal(result);
            result = ConvertTaps(result);
            result = ConvertRandomWaits(result);
            result = ConvertTouchShorthands(result);
            return result;
        }

        private string ConvertHexToDecimal(string macro)
        {
            return HexTokenRegex.Replace(macro, m =>
                Convert.ToUInt32(m.Value, 16).ToString());
        }

        private string ConvertTaps(string macro)
        {
            return TapRegex.Replace(macro, m =>
                $"H.{m.Groups[1].Value}.{DefaultTapFrames}");
        }

        private string ConvertRandomWaits(string macro)
        {
            return RandomRegex.Replace(macro, m =>
            {
                int min = int.Parse(m.Groups[1].Value);
                int max = int.Parse(m.Groups[2].Value);
                return $"W.{_random.Next(min, max + 1)}";
            });
        }

        private string ConvertTouchShorthands(string macro)
        {
            return TouchShorthandRegex.Replace(macro, m =>
            {
                string name = m.Groups[1].Value;
                string frames = m.Groups[2].Success ? m.Groups[2].Value : DefaultTapFrames.ToString();

                if (!TouchShorthands.TryGetValue(name, out var coords))
                    throw new ArgumentException($"Unknown touch shorthand: '{name}'");

                return $"S.{coords.X}.{coords.Y}.{frames}";
            });
        }
    }
}
