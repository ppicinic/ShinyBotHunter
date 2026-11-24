using PKHeX.Core;
using ShinyBotHunter.Milestones;

namespace ShinyBotHunter.Tests
{
    public class HgssRatesTests
    {
        private const int Wooper = 194, Ekans = 23, Bellsprout = 69, Mareep = 179, Zubat = 41, Hoppip = 187, Rattata = 19;
        private const int Weedle = 13, Ledyba = 165, Kakuna = 14, Pidgey = 16, Magnemite = 81, Azurill = 298;
        private const int Magikarp = 129, Tentacool = 72, Goldeen = 118, Poliwag = 60, Geodude = 74, Krabby = 98;
        private const int Spearow = 21, Aipom = 190, Heracross = 214, Exeggcute = 102, Pineco = 204;

        private static readonly int[] LandWeights = [20, 20, 10, 10, 10, 10, 5, 5, 4, 4, 1, 1];
        private static readonly int[] Route32Levels = [6, 4, 6, 4, 6, 6, 6, 6, 6, 4, 6, 4];
        private static readonly int[] Route32Night = [Wooper, Ekans, Wooper, Ekans, Bellsprout, Bellsprout, Mareep, Mareep, Wooper, Zubat, Wooper, Zubat];
        private static readonly int[] Route32Morning = [Bellsprout, Ekans, Bellsprout, Ekans, Mareep, Mareep, Hoppip, Hoppip, Rattata, Zubat, Rattata, Zubat];
        private static readonly int[] Route32Day = [Bellsprout, Ekans, Bellsprout, Ekans, Mareep, Mareep, Hoppip, Hoppip, Rattata, Rattata, Rattata, Rattata];

        // SoulSilver's Route 32 and Route 30 grass (gs_enc_data.json), each with another time of day beside it.
        private static readonly Milestone Route32 = LandMilestone(
            Land(SlotCondition.Night, Route32Night, Route32Levels),
            Land(SlotCondition.Morning, Route32Morning, Route32Levels),
            Land(SlotCondition.Day, Route32Day, Route32Levels));

        private static readonly Milestone Route30 = LandMilestone(
            Land(SlotCondition.Morning,
                [Ledyba, Weedle, Ledyba, Weedle, Weedle, Weedle, Kakuna, Kakuna, Pidgey, Pidgey, Pidgey, Pidgey],
                [3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4]),
            Land(SlotCondition.Day,
                [Pidgey, Weedle, Pidgey, Weedle, Pidgey, Pidgey, Kakuna, Kakuna, Weedle, Kakuna, Weedle, Kakuna],
                [3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 4, 4]));

        private static Slot[] Land(SlotCondition condition, int[] species, int[] levels)
            => species.Select((s, i) => new Slot(i, s, levels[i], levels[i], LandWeights[i], condition)).ToArray();

        private static Milestone LandMilestone(params Slot[][] tables) => new([32], Modality.Land, true, tables.SelectMany(t => t).ToArray(), []);

        private static void AssertShares(Dictionary<int, double> expected, IReadOnlyDictionary<int, double> actual)
        {
            Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
            foreach (var (species, share) in expected)
                Assert.Equal(share, actual[species], 6);
        }

        private static readonly Dictionary<int, double> Route32NightPlain = new()
        {
            [Wooper] = 0.35, [Ekans] = 0.30, [Bellsprout] = 0.20, [Mareep] = 0.10, [Zubat] = 0.05
        };

        private static readonly Dictionary<int, double> Route32NightRepel6 = new()
        {
            [Wooper] = 35.0 / 65, [Bellsprout] = 20.0 / 65, [Mareep] = 10.0 / 65, [Ekans] = 0, [Zubat] = 0
        };

        private static readonly Dictionary<int, double> Route32DayPlain = new()
        {
            [Bellsprout] = 0.30, [Ekans] = 0.30, [Mareep] = 0.20, [Hoppip] = 0.10, [Rattata] = 0.10
        };

        [Fact]
        public void Route32Night_Plain()
        {
            var (shares, survive) = HgssRates.Compute(Route32, SlotCondition.Night, null, 0, 20, false);

            AssertShares(Route32NightPlain, shares);
            Assert.Equal(1, survive, 6);
        }

        [Fact]
        public void Route32Night_RepelLevel6()
        {
            var (shares, survive) = HgssRates.Compute(Route32, SlotCondition.Night, 6, 0, 20, false);

            AssertShares(Route32NightRepel6, shares);
            Assert.Equal(0.65, survive, 6);
        }

        [Fact]
        public void Route32Day_Plain()
        {
            var (shares, survive) = HgssRates.Compute(Route32, SlotCondition.Day, null, 0, 20, false);

            AssertShares(Route32DayPlain, shares);
            Assert.Equal(1, survive, 6);
        }

        [Fact]
        public void Route32Day_RepelLevel6()
        {
            var (shares, survive) = HgssRates.Compute(Route32, SlotCondition.Day, 6, 0, 20, false);

            AssertShares(new()
            {
                [Bellsprout] = 30.0 / 65, [Mareep] = 20.0 / 65, [Hoppip] = 10.0 / 65, [Rattata] = 5.0 / 65, [Ekans] = 0
            }, shares);
            Assert.Equal(0.65, survive, 6);
        }

        [Fact]
        public void Route30Morning_Plain()
        {
            var (shares, survive) = HgssRates.Compute(Route30, SlotCondition.Morning, null, 0, 20, false);

            AssertShares(new() { [Weedle] = 0.50, [Ledyba] = 0.30, [Kakuna] = 0.10, [Pidgey] = 0.10 }, shares);
            Assert.Equal(1, survive, 6);
        }

        [Fact]
        public void Route30Morning_RepelLevel4()
        {
            var (shares, survive) = HgssRates.Compute(Route30, SlotCondition.Morning, 4, 0, 20, false);

            AssertShares(new() { [Weedle] = 0.5, [Kakuna] = 0.25, [Pidgey] = 0.25, [Ledyba] = 0 }, shares);
            Assert.Equal(0.40, survive, 6);
        }

        [Fact]
        public void Repel_EqualLevelIsNotCancelled()
        {
            var (shares, survive) = HgssRates.Compute(Route32, SlotCondition.Night, 4, 0, 20, false);

            AssertShares(Route32NightPlain, shares);
            Assert.Equal(1, survive, 6);
        }

        [Fact]
        public void Repel_AboveEveryLevel_SurviveZeroAndSharesZero()
        {
            var (shares, survive) = HgssRates.Compute(Route32, SlotCondition.Night, 50, 0, 20, false);

            Assert.Equal(0, survive);
            AssertShares(new() { [Wooper] = 0, [Ekans] = 0, [Bellsprout] = 0, [Mareep] = 0, [Zubat] = 0 }, shares);
        }

        [Theory]
        [InlineData(Modality.OldRod)]
        [InlineData(Modality.GoodRod)]
        [InlineData(Modality.SuperRod)]
        [InlineData(Modality.RockSmash)]
        public void Repel_DoesNotApplyToFishingOrRockSmash(Modality modality)
        {
            Slot[] slots = modality == Modality.RockSmash
                ? [new(0, Geodude, 5, 10, 80, SlotCondition.None), new(1, Krabby, 5, 10, 20, SlotCondition.None)]
                : [new(0, Magikarp, 10, 10, 40, SlotCondition.None), new(1, Magikarp, 10, 10, 30, SlotCondition.None),
                   new(2, Tentacool, 10, 10, 15, SlotCondition.None), new(3, Tentacool, 10, 10, 10, SlotCondition.None),
                   new(4, Goldeen, 10, 10, 5, SlotCondition.None)];
            var milestone = new Milestone([32], modality, false, slots, []);

            var (shares, survive) = HgssRates.Compute(milestone, SlotCondition.Day, 100, 0, 20, false);

            Assert.Equal(1, survive, 6);
            AssertShares(modality == Modality.RockSmash
                ? new() { [Geodude] = 0.80, [Krabby] = 0.20 }
                : new() { [Magikarp] = 0.70, [Tentacool] = 0.25, [Goldeen] = 0.05 }, shares);
        }

        private static readonly Milestone Surf10To19 = new([32], Modality.Surf, false, [new(0, Tentacool, 10, 19, 100, SlotCondition.None)], []);

        [Fact]
        public void Repel_LevelRange_CancelsTheFractionBelow()
        {
            var (shares, survive) = HgssRates.Compute(Surf10To19, SlotCondition.Day, 15, 0, 20, false);

            Assert.Equal(0.5, survive, 6);
            AssertShares(new() { [Tentacool] = 1 }, shares);
        }

        [Theory]
        [InlineData(Ability.KeenEye)]
        [InlineData(Ability.Intimidate)]
        public void KeenEyeOrIntimidate_HalvesSlotsFiveBelowTheLead(Ability ability)
        {
            var (shares, survive) = HgssRates.Compute(Route32, SlotCondition.Night, null, (byte)ability, 9, false);

            Assert.Equal(0.825, survive, 6);
            AssertShares(new()
            {
                [Wooper] = 0.35 / 0.825, [Ekans] = 0.15 / 0.825, [Bellsprout] = 0.20 / 0.825, [Mareep] = 0.10 / 0.825,
                [Zubat] = 0.025 / 0.825
            }, shares);
        }

        [Fact]
        public void KeenEye_LeadLevel8_NoEffect()
        {
            var (shares, survive) = HgssRates.Compute(Route32, SlotCondition.Night, null, (byte)Ability.KeenEye, 8, false);

            AssertShares(Route32NightPlain, shares);
            Assert.Equal(1, survive, 6);
        }

        [Fact]
        public void KeenEye_WithRepel()
        {
            var (shares, survive) = HgssRates.Compute(Route32, SlotCondition.Night, 6, (byte)Ability.KeenEye, 11, false);

            Assert.Equal(0.325, survive, 6);
            AssertShares(Route32NightRepel6, shares);
        }

        [Theory]
        [InlineData(Ability.Illuminate)]
        [InlineData(Ability.Stench)]
        [InlineData(Ability.ArenaTrap)]
        public void RateOnlyAbilities_DoNotChangeShares(Ability ability)
        {
            var (shares, survive) = HgssRates.Compute(Route32, SlotCondition.Night, null, (byte)ability, 20, false);

            AssertShares(Route32NightPlain, shares);
            Assert.Equal(1, survive, 6);
        }

        [Fact]
        public void SoundSlots_CountedAsOff()
        {
            Slot[] sounds = new[] { 2, 3, 4, 5 }.Select(i => new Slot(i, Azurill, 6, 6, LandWeights[i], SlotCondition.HoennSound)).ToArray();
            var milestone = Route32 with { Slots = [.. Route32.Slots, .. sounds] };

            var (shares, survive) = HgssRates.Compute(milestone, SlotCondition.Night, null, 0, 20, false);

            Assert.False(shares.ContainsKey(Azurill));
            AssertShares(Route32NightPlain, shares);
            Assert.Equal(1, survive, 6);
        }

        private static readonly Milestone GoodRod = new([32], Modality.GoodRod, false,
            [new(0, Magikarp, 20, 20, 40, SlotCondition.None), new(1, Poliwag, 20, 20, 30, SlotCondition.None),
             new(2, Poliwag, 20, 20, 15, SlotCondition.None), new(3, Tentacool, 20, 20, 10, SlotCondition.None),
             new(4, Magikarp, 20, 20, 5, SlotCondition.None), new(3, Goldeen, 20, 20, 10, SlotCondition.NightFishing)], []);

        [Fact]
        public void NightFishing_ReplacesItsIndexAtNight()
        {
            var (night, _) = HgssRates.Compute(GoodRod, SlotCondition.Night, null, 0, 20, false);
            var (day, _) = HgssRates.Compute(GoodRod, SlotCondition.Day, null, 0, 20, false);

            AssertShares(new() { [Magikarp] = 0.45, [Poliwag] = 0.45, [Goldeen] = 0.10 }, night);
            AssertShares(new() { [Magikarp] = 0.45, [Poliwag] = 0.45, [Tentacool] = 0.10 }, day);
        }

        [Fact]
        public void Headbutt_TakesTheTreeKindsTable()
        {
            int[] weights = [50, 15, 15, 10, 5, 5];
            Slot[] common = new[] { Spearow, Spearow, Aipom, Aipom, Exeggcute, Exeggcute }
                .Select((s, i) => new Slot(i, s, 10, 14, weights[i], SlotCondition.CommonTree)).ToArray();
            Slot[] rare = new[] { Pineco, Pineco, Heracross, Heracross, Aipom, Aipom }
                .Select((s, i) => new Slot(i, s, 10, 14, weights[i], SlotCondition.RareTree)).ToArray();
            var milestone = new Milestone([32], Modality.Headbutt, false, [.. common, .. rare], []);

            var (shares, survive) = HgssRates.Compute(milestone, SlotCondition.RareTree, 20, 0, 20, false);

            Assert.Equal(1, survive, 6);
            AssertShares(new() { [Pineco] = 0.65, [Heracross] = 0.25, [Aipom] = 0.10 }, shares);
        }
    }
}
