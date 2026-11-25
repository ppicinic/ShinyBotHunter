using Newtonsoft.Json.Linq;
using ShinyBotHunter.Scripts.Milestones;

namespace ShinyBotHunter.Tests
{
    public class HgssAnchorFinderTests
    {
        private static JObject Trainer(int movement, int facing, int sight)
            => JObject.Parse($$"""
                { "objects": [ { "type": 1, "movement": {{movement}}, "facingDirection": {{facing}}, "param0": {{sight}},
                                 "xRange": 0, "yRange": 0, "x": 5, "z": 5, "y": 0 } ] }
                """);

        [Fact]
        public void Unsafe_TrainerFacingRight_ExcludesItsSightLineOnly()
        {
            var tiles = HgssAnchorFinder.Unsafe(Trainer(movement: 17, facing: 3, sight: 3));

            Assert.Equal([(6, 5), (7, 5), (8, 5)], tiles.OrderBy(t => t.X));
        }

        [Fact]
        public void Unsafe_TurningTrainer_ExcludesAllFourDirections()
        {
            var tiles = HgssAnchorFinder.Unsafe(Trainer(movement: 2, facing: 0, sight: 2));

            Assert.Equal(new HashSet<(int X, int Y)> { (5, 4), (5, 3), (5, 6), (5, 7), (4, 5), (3, 5), (6, 5), (7, 5) }, tiles);
        }

        [Fact]
        public void Unsafe_Sign_ExcludesTheTileBelow()
        {
            var zoneEvent = JObject.Parse("""
                { "bgs": [ { "type": 1, "x": 4, "z": 4, "y": 0 }, { "type": 0, "x": 8, "z": 8, "y": 0 }, { "type": 2, "x": 9, "z": 9, "y": 0 } ] }
                """);

            var tiles = HgssAnchorFinder.Unsafe(zoneEvent);

            Assert.Equal([(4, 5)], tiles);
        }

        [Fact]
        public void Runs_FindsMaximalRunsBothWays()
        {
            // 5 x 3 with the middle tile (2, 1) missing.
            var tiles = new HashSet<(int X, int Y)>();
            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 5; x++)
                {
                    if ((x, y) != (2, 1))
                    {
                        tiles.Add((x, y));
                    }
                }
            }

            var runs = HgssAnchorFinder.Runs(tiles);

            Assert.Equal([(0, 0, 5), (0, 1, 2), (3, 1, 2), (0, 2, 5)], runs.Where(r => r.Horizontal).Select(r => (r.X, r.Y, r.Length)));
            Assert.Equal([(0, 0, 3), (1, 0, 3), (2, 0, 1), (3, 0, 3), (4, 0, 3), (2, 2, 1)], runs.Where(r => !r.Horizontal).Select(r => (r.X, r.Y, r.Length)));
        }
    }
}
