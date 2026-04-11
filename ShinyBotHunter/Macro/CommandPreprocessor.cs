using System.Text.RegularExpressions;

namespace ShinyBotHunter.Macro
{
    public class CommandPreprocessor
    {
        public int DefaultTapFrames { get; set; } = 3;

        private static readonly Regex TapRegex = new(@"T\.([^;:.]+)", RegexOptions.Compiled);
        private static readonly Regex RandomRegex = new(@"W\.R\[(\d+),(\d+)\]", RegexOptions.Compiled);

        private readonly Random _random = new();

        public string Process(string macro)
        {
            string result = macro;
            result = ConvertTaps(result);
            result = ConvertRandomWaits(result);
            return result;
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
    }
}
