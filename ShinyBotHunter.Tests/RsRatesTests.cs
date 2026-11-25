using PKHeX.Core;
using ShinyBotHunter.Milestones;

namespace ShinyBotHunter.Tests
{
    public class RsRatesTests
    {
        private const int Wurmple = 265, Zigzagoon = 263, Poochyena = 261, Tentacool = 72, Wingull = 278, Pelipper = 279;
        private const int Magikarp = 129, Goldeen = 118, Carvanha = 341, Geodude = 74, Nosepass = 299;

        // Sapphire's Route 101 land (map 16), as milestones.json.gz has it.
        private static readonly Slot[] Route101Slots =
        [
            new(0, Wurmple, 2, 2, 20, SlotCondition.None), new(1, Zigzagoon, 2, 2, 20, SlotCondition.None),
            new(2, Wurmple, 2, 2, 10, SlotCondition.None), new(3, Wurmple, 3, 3, 10, SlotCondition.None),
            new(4, Zigzagoon, 3, 3, 10, SlotCondition.None), new(5, Zigzagoon, 3, 3, 10, SlotCondition.None),
            new(6, Wurmple, 3, 3, 5, SlotCondition.None), new(7, Zigzagoon, 3, 3, 5, SlotCondition.None),
            new(8, Poochyena, 2, 2, 4, SlotCondition.None), new(9, Poochyena, 2, 2, 4, SlotCondition.None),
            new(10, Poochyena, 3, 3, 1, SlotCondition.None), new(11, Poochyena, 3, 3, 1, SlotCondition.None)
        ];

        private static readonly Milestone Route101 = new([16], Modality.Land, true, Route101Slots, []);

        // Sapphire's Route 103 surf (map 18).
        private static readonly Milestone Route103Surf = new([18], Modality.Surf, false,
        [
            new(0, Tentacool, 5, 35, 60, SlotCondition.None), new(1, Wingull, 10, 30, 30, SlotCondition.None),
            new(2, Wingull, 15, 25, 5, SlotCondition.None), new(3, Pelipper, 25, 30, 4, SlotCondition.None),
            new(4, Pelipper, 25, 30, 1, SlotCondition.None)
        ], []);

        // Sapphire's Route 102 Good Rod (map 17).
        private static readonly Milestone Route102GoodRod = new([17], Modality.GoodRod, false,
        [
            new(2, Magikarp, 10, 30, 60, SlotCondition.None), new(3, Goldeen, 10, 30, 20, SlotCondition.None),
            new(4, Carvanha, 10, 30, 20, SlotCondition.None)
        ], []);

        // Sapphire's rock smash table on map 6153.
        private static readonly Slot[] RockSmashSlots =
        [
            new(0, Geodude, 10, 15, 60, SlotCondition.None), new(1, Nosepass, 10, 20, 30, SlotCondition.None),
            new(2, Geodude, 5, 10, 5, SlotCondition.None), new(3, Geodude, 15, 20, 4, SlotCondition.None),
            new(4, Geodude, 15, 20, 1, SlotCondition.None)
        ];

        private static void AssertShares(Dictionary<int, double> expected, IReadOnlyDictionary<int, double> actual, int precision = 6)
        {
            Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
            foreach (var (species, share) in expected)
                Assert.Equal(share, actual[species], precision);
        }

        [Fact]
        public void Route101Plain_45_45_10()
        {
            var (shares, survive) = RsRates.Compute(Route101, SlotCondition.None, null, 0, 5, false);

            AssertShares(new() { [Wurmple] = 0.45, [Zigzagoon] = 0.45, [Poochyena] = 0.10 }, shares);
            Assert.Equal(1, survive, 6);
        }

        [Fact]
        public void Route101RepelAtThree_EqualLevelKept()
        {
            var (shares, survive) = RsRates.Compute(Route101, SlotCondition.None, 3, 0, 3, false);

            AssertShares(new() { [Wurmple] = 15.0 / 42, [Zigzagoon] = 25.0 / 42, [Poochyena] = 2.0 / 42 }, shares);
            Assert.Equal(0.42, survive, 6);
        }

        [Fact]
        public void Route103SurfRepelInsideTheRanges()
        {
            var (shares, survive) = RsRates.Compute(Route103Surf, SlotCondition.None, 20, 0, 20, false);

            AssertShares(new() { [Tentacool] = 0.5692, [Wingull] = 0.3389, [Pelipper] = 0.0919 }, shares, 4);
            Assert.Equal(0.5441, survive, 4);
        }

        [Fact]
        public void RepelAboveEveryLevel_SurviveZero()
        {
            var (shares, survive) = RsRates.Compute(Route101, SlotCondition.None, 10, 0, 10, false);

            AssertShares(new() { [Wurmple] = 0, [Zigzagoon] = 0, [Poochyena] = 0 }, shares);
            Assert.Equal(0, survive);
        }

        [Fact]
        public void Fishing_IgnoresRepel()
        {
            var (shares, survive) = RsRates.Compute(Route102GoodRod, SlotCondition.None, 50, 0, 50, false);

            AssertShares(new() { [Magikarp] = 0.6, [Goldeen] = 0.2, [Carvanha] = 0.2 }, shares);
            Assert.Equal(1, survive, 6);
        }

        [Fact]
        public void RockSmash_HonoursRepel()
        {
            var rockSmash = new Milestone([6153], Modality.RockSmash, false, RockSmashSlots, []);
            var surf = new Milestone([6153], Modality.Surf, false, RockSmashSlots, []);

            var (shares, survive) = RsRates.Compute(rockSmash, SlotCondition.None, 13, 0, 13, false);
            var (surfShares, surfSurvive) = RsRates.Compute(surf, SlotCondition.None, 13, 0, 13, false);

            AssertShares(surfShares.ToDictionary(), shares);
            Assert.Equal(surfSurvive, survive, 6);
            Assert.True(survive < 1);
        }

        [Theory]
        [InlineData(Ability.Stench)]
        [InlineData(Ability.Illuminate)]
        [InlineData(Ability.KeenEye)]
        [InlineData(Ability.Intimidate)]
        [InlineData(Ability.Static)]
        [InlineData(Ability.MagnetPull)]
        [InlineData(Ability.Hustle)]
        public void SharesAreTheSameForEveryLead(Ability ability)
        {
            foreach (int? repel in new int?[] { null, 3 })
            {
                var (baseShares, baseSurvive) = RsRates.Compute(Route101, SlotCondition.None, repel, 0, 1, false);
                foreach (int level in new[] { 1, 50 })
                    foreach (bool egg in new[] { false, true })
                    {
                        var (shares, survive) = RsRates.Compute(Route101, SlotCondition.None, repel, (byte)ability, level, egg);

                        Assert.Equal(baseShares, shares);
                        Assert.Equal(baseSurvive, survive);
                    }
            }
        }
    }
}
