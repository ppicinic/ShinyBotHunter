using Newtonsoft.Json;

namespace ShinyBotHunter.Milestones
{
    public enum Modality
    {
        Land,
        Surf,
        OldRod,
        GoodRod,
        SuperRod,
        RockSmash,
        Headbutt
    }

    // When a slot applies. Ruby and Sapphire use only None.
    public enum SlotCondition
    {
        None,
        Morning,
        Day,
        Night,
        HoennSound,
        SinnohSound,
        NightFishing,
        CommonTree,
        RareTree,
        SecretTree
    }

    // One line of the game's table. Species is the national dex number; Index is the slot's position in the game's table.
    public record Slot(int Index, int Species, int MinLevel, int MaxLevel, int Weight, SlotCondition Condition);

    // One headbutt tree, in its map's list order; each tile is [x, y].
    public record HeadbuttTree(bool Secret, int[][] Tiles);

    // Where a milestone is hunted: a tile on game map Map, run left and right XRange tiles each way, or up and down
    // YRange tiles each way, or with both 0 a spin.
    public record Anchor(int Map, int X, int Y, int XRange, int YRange);

    // One map, or several of one location that share a table, and one way of hunting it.
    public record Milestone(int[] Maps, Modality Modality, bool HasGrass, Slot[] Slots, HeadbuttTree[] Trees, Anchor? Anchor = null)
    {
        // Stable across name corrections and regenerations; never stored in the file.
        [JsonIgnore]
        public string Id => $"{Maps[0]}-{Modality.ToString().ToLowerInvariant()}";

        // Set by MilestoneFile.Load from map-names.json.
        [JsonIgnore]
        public string Name { get; init; } = "";
    }
}
