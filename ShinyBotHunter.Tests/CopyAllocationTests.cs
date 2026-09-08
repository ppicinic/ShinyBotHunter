using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// Pins the rule the copy registry exists for: a ROM copy whose save is a shiny -- parked or
    /// already saved -- is never handed to a hunt again. The HGSS hunt has enforced this for years
    /// with its GameIds map; the RS hunt initially shipped without it, allocating copy ids from the
    /// emulator position, which would have pointed a fresh wave straight at a parked shiny's copy.
    /// </summary>
    public class CopyAllocationTests
    {
        private const string Source = "Pokemon Sapphire";

        [Fact]
        public void FreshHuntTakesTheLowestIds()
        {
            var ids = StarterHuntStateStore.AllocateCopyIds(Source, 4, Array.Empty<string>());

            Assert.Equal(new[] { 1, 2, 3, 4 }, ids);
        }

        [Fact]
        public void ARetiredCopyIsNeverAllocated()
        {
            // Copy 2 holds a saved shiny. Its id must be skipped, not reused.
            var ids = StarterHuntStateStore.AllocateCopyIds(Source, 4, new[] { $"{Source}2" });

            Assert.Equal(new[] { 1, 3, 4, 5 }, ids);
            Assert.DoesNotContain(2, ids);
        }

        [Fact]
        public void AParkedCopyIsTreatedExactlyLikeARetiredOne()
        {
            // Copy 1 is parked mid-battle with an unsaved shiny; copy 3 is retired. The next wave
            // must route around both -- overwriting the parked one loses a shiny that exists but was
            // never saved, which is the least recoverable failure this system has.
            var ids = StarterHuntStateStore.AllocateCopyIds(Source, 3, new[] { $"{Source}1", $"{Source}3" });

            Assert.Equal(new[] { 2, 4, 5 }, ids);
        }

        [Fact]
        public void UnavailabilityIsCaseInsensitive()
        {
            var ids = StarterHuntStateStore.AllocateCopyIds(Source, 2, new[] { $"{Source.ToUpperInvariant()}1" });

            Assert.Equal(new[] { 2, 3 }, ids);
        }

        [Fact]
        public void DeepQueuesKeepAdvancingPastManyRetirements()
        {
            // Three starters caught over time retire three copies; the fourth hunt still gets a full
            // wave from the ids that remain.
            var unavailable = new[] { $"{Source}1", $"{Source}2", $"{Source}4" };

            var ids = StarterHuntStateStore.AllocateCopyIds(Source, 4, unavailable);

            Assert.Equal(new[] { 3, 5, 6, 7 }, ids);
        }
    }
}
