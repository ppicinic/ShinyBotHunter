using ShinyBotHunter.BotEvents;

namespace ShinyBotHunter.Tests
{
    // The shipped Pickup config carries the research doc's values, and validation rejects what would fail a trip later.
    [Collection("macro-registry")]
    public class PickupConfigTests
    {
        public PickupConfigTests()
        {
            Directory.SetCurrentDirectory(RepoPaths.AppProject);
        }

        [Fact]
        public void ShippedConfigLoadsAndValidates()
        {
            PickupConfig config = PickupConfig.Load(PickupConfig.DefaultPath);

            Assert.Equal([44, 77], config.PokemonTouch);
            Assert.Equal([[63, 27], [191, 27], [63, 77], [191, 77], [63, 123], [191, 123]], config.SlotTouches);
            Assert.Equal([191, 106], config.ItemTouch);
            Assert.Equal([191, 66], config.TakeTouch);
            Assert.Equal([227, 178], config.CancelTouch);
            Assert.Equal((5000, 10000, 5000, 10000), (config.StartMenuStaleMs, config.PartyMenuStaleMs, config.StepStaleMs, config.FieldFreeStaleMs));
        }

        [Fact]
        public void BadValuesAreRejected()
        {
            var cases = new (string Key, Action<PickupConfig> Set)[]
            {
                ("slotTouches", c => c.SlotTouches = c.SlotTouches[..5]),
                ("slotTouches", c => c.SlotTouches[2] = [63]),
                ("pokemonTouch", c => c.PokemonTouch = [44, 77, 3]),
                ("cancelTouch", c => c.CancelTouch = []),
                ("startMenuStaleMs", c => c.StartMenuStaleMs = 0),
                ("stepStaleMs", c => c.StepStaleMs = -5),
                ("fieldFreeStaleMs", c => c.FieldFreeStaleMs = 0),
            };

            foreach (var (key, set) in cases)
            {
                PickupConfig config = PickupConfig.Load(PickupConfig.DefaultPath);
                set(config);

                var ex = Assert.Throws<InvalidOperationException>(() => config.Validate("Pickup.json"));
                Assert.Contains($"Pickup.json: {key}", ex.Message);
            }
        }
    }
}
