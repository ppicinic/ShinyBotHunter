using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    public class PickupTallyTests
    {
        private static PickupTally Tally(List<PickupItemsResponse> pushes)
            => new(push => { pushes.Add(push); return Task.CompletedTask; });

        [Fact]
        public async Task CountsPerGameAndItem()
        {
            PickupTally tally = Tally([]);

            await tally.RecordAsync("B", "M", 17);
            await tally.RecordAsync("A", "M", 17);
            await tally.RecordAsync("A", "M", 50);
            await tally.RecordAsync("A", "M", 17);

            List<PickupItemsResponse> read = tally.Read();
            Assert.Equal(["A", "B"], read.Select(r => r.GameFileName));
            Assert.Equal([new PickupItemCount(17, "Potion", 2), new PickupItemCount(50, "Rare Candy", 1)], read[0].Items);
            Assert.Equal([new PickupItemCount(17, "Potion", 1)], read[1].Items);
        }

        [Fact]
        public async Task ReadReturnsCopies()
        {
            PickupTally tally = Tally([]);
            await tally.RecordAsync("A", "M", 17);

            List<PickupItemsResponse> first = tally.Read();
            await tally.RecordAsync("A", "M", 17);
            List<PickupItemsResponse> second = tally.Read();
            Assert.Equal([new PickupItemCount(17, "Potion", 1)], first[0].Items);
            first[0].Items.Clear();
            List<PickupItemsResponse> third = tally.Read();

            Assert.Equal([new PickupItemCount(17, "Potion", 2)], second[0].Items);
            Assert.Equal([new PickupItemCount(17, "Potion", 2)], third[0].Items);
        }

        [Fact]
        public async Task ItemsSortByCountThenName()
        {
            PickupTally tally = Tally([]);

            await tally.RecordAsync("A", "M", 50);
            await tally.RecordAsync("A", "M", 26);
            await tally.RecordAsync("A", "M", 17);
            await tally.RecordAsync("A", "M", 26);

            Assert.Equal(
                [new PickupItemCount(26, "Super Potion", 2), new PickupItemCount(17, "Potion", 1), new PickupItemCount(50, "Rare Candy", 1)],
                tally.Read()[0].Items);
        }

        [Fact]
        public async Task ALaterMilestoneReplacesTheGamesMilestoneAndKeepsCounts()
        {
            List<PickupItemsResponse> pushes = [];
            PickupTally tally = Tally(pushes);

            await tally.RecordAsync("A", "Route 33", 17);
            await tally.RecordAsync("B", "Lake", 50);
            await tally.RecordAsync("A", "Route 34", 50);

            List<PickupItemsResponse> read = tally.Read();
            Assert.Equal("Route 34", read[0].Milestone);
            Assert.Equal([new PickupItemCount(17, "Potion", 1), new PickupItemCount(50, "Rare Candy", 1)], read[0].Items);
            Assert.Equal("Lake", read[1].Milestone);
            Assert.Equal("Route 34", pushes[^1].Milestone);
        }

        [Fact]
        public void ItemNames()
        {
            Assert.Equal("Potion", PickupTally.ItemName(17));
            Assert.Equal("Rare Candy", PickupTally.ItemName(50));
            Assert.Equal("Item 9999", PickupTally.ItemName(9999));
        }

        [Fact]
        public async Task ATakeSendsOnePushForItsGame()
        {
            List<PickupItemsResponse> pushes = [];
            PickupTally tally = Tally(pushes);
            await tally.RecordAsync("B", "M", 50);

            await tally.RecordAsync("A", "M", 17);

            Assert.Equal(2, pushes.Count);
            PickupItemsResponse push = pushes[1];
            Assert.Equal("A", push.GameFileName);
            Assert.Equal([new PickupItemCount(17, "Potion", 1)], push.Items);
        }
    }
}
