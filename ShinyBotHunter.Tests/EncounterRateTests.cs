using ShinyBotHunter.Persistence.PostgreSQL;
using static ShinyBotHunter.Persistence.PostgreSQL.PostgreSQLEncounterDao;

namespace ShinyBotHunter.Tests
{
    public class EncounterRateTests
    {
        private static DateTime At(int hour, int minute) => new(2026, 1, 1, hour, minute, 0, DateTimeKind.Utc);

        [Fact]
        public void SingleBin_ScalesCountByElapsedSpan()
        {
            var bins = new List<EncounterBin> { new(At(10, 0), 2, At(10, 15), At(10, 45)) };

            var rate = PostgreSQLEncounterDao.CalculateEncounters(bins, At(10, 15), At(10, 45));

            Assert.Equal(4.0, rate, 1e-9);
        }

        [Fact]
        public void ThreeBins_WeightsEdgeBinsAndAveragesOverBinCount()
        {
            var bins = new List<EncounterBin>
            {
                new(At(10, 0), 2, At(10, 15), At(10, 45)),
                new(At(11, 0), 2, At(11, 10), At(11, 30)),
                new(At(12, 0), 1, At(12, 5), At(12, 5)),
            };

            var rate = PostgreSQLEncounterDao.CalculateEncounters(bins, At(10, 15), At(12, 5));

            // (60/45 * 2 + 2 + 60/5 * 1) / 3
            Assert.Equal(5.556, rate, 1e-3);
        }

        [Fact]
        public void GapHours_AreNotInTheDivisor()
        {
            var bins = new List<EncounterBin>
            {
                new(At(10, 0), 3, At(10, 30), At(10, 50)),
                new(At(13, 0), 2, At(13, 10), At(13, 30)),
            };

            var rate = PostgreSQLEncounterDao.CalculateEncounters(bins, At(10, 30), At(13, 30));

            // (60/30 * 3 + 60/30 * 2) / 2 bins, not / 4 hours
            Assert.Equal(5.0, rate, 1e-9);
        }
    }
}
