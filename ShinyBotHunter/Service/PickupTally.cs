using Microsoft.AspNetCore.SignalR;
using PKHeX.Core;

namespace ShinyBotHunter.Service
{
    // The items the Pickup hunt took since the backend started, per game file and item id (docs/specs/pickup-items-dashboard.md).
    // Memory only: never cleared, gone on restart.
    public class PickupTally
    {
        private static readonly string[] Items = GameInfo.GetStrings("en").GetItemStrings(EntityContext.Gen4, GameVersion.HG);

        private readonly Func<PickupItemsResponse, Task> push;

        private readonly object gate = new();
        // counts and milestones are guarded by gate; written together by every bot's Pickup visit and read by the GET. Nothing awaits under it.
        private readonly Dictionary<string, Dictionary<ushort, int>> counts = new();
        // The milestone of the hunt that made each game file's latest take.
        private readonly Dictionary<string, string> milestones = new();

        public PickupTally(IHubContext<ElectronHub, IElectronClient> hub)
            : this(items => hub.Clients.All.ReceivePickupItems(items))
        {
        }

        // The test seam for the SignalR boundary.
        internal PickupTally(Func<PickupItemsResponse, Task> push)
        {
            this.push = push;
        }

        // Counts one take and pushes that game's tally.
        public async Task RecordAsync(string gameFileName, string milestone, ushort item)
        {
            PickupItemsResponse response;
            lock (gate)
            {
                if (!counts.TryGetValue(gameFileName, out Dictionary<ushort, int>? items))
                {
                    items = new();
                    counts[gameFileName] = items;
                }
                items[item] = items.GetValueOrDefault(item) + 1;
                milestones[gameFileName] = milestone;
                response = Snapshot(gameFileName, milestone, items);
            }
            await push(response);
        }

        // Every game's tally, ordered by game file name.
        public List<PickupItemsResponse> Read()
        {
            lock (gate)
            {
                return counts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => Snapshot(c.Key, milestones[c.Key], c.Value)).ToList();
            }
        }

        // A Gen 3 number with no Gen 4 item is kept as this bit plus the Gen 3 number: no sprite, and no collision, since Gen 4 ids stop near 540 and Gen 3 ids at 376.
        private const ushort Gen3Unknown = 0x8000;

        // The tally's id for an RS take's Gen 3 item number.
        internal static ushort FromGen3(ushort gen3Item)
        {
            ushort id = ItemConverter.GetItemFuture3(gen3Item);
            return ItemConverter.IsItemTransferable34(id) ? id : (ushort)(Gen3Unknown | gen3Item);
        }

        internal static string ItemName(ushort id)
        {
            if ((id & Gen3Unknown) != 0)
            {
                return $"Item {id & ~Gen3Unknown}";
            }
            return id < Items.Length ? Items[id] : $"Item {id}";
        }

        // Items by count, most first, then by name.
        private static PickupItemsResponse Snapshot(string gameFileName, string milestone, Dictionary<ushort, int> items)
        {
            List<PickupItemCount> rows = items
                .Select(i => new PickupItemCount(i.Key, ItemName(i.Key), i.Value))
                .OrderByDescending(r => r.Count)
                .ThenBy(r => r.Name, StringComparer.Ordinal)
                .ToList();
            return new PickupItemsResponse(gameFileName, milestone, rows.Sum(r => r.Count), rows);
        }
    }
}
