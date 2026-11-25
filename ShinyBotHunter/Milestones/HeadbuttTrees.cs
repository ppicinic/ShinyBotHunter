namespace ShinyBotHunter.Milestones
{
    // Which table an HGSS headbutt tree gives, and the tree a game file hunts (FR-21). The rule is the game's
    // (src/field/headbutt.c at 8946577): a regular tree's kind is a lookup on the trainer ID's last digit, trainerId % 10
    // on the full 32-bit ID (profile->id, visible half in the low 16 bits), and the tree's position in its map's list.
    // That reading is from the source, not observed on the emulator.
    public static class HeadbuttTrees
    {
        private static readonly SlotCondition? C = SlotCondition.CommonTree, R = SlotCondition.RareTree, N = null;

        // sRareTreeLUT_1 to sRareTreeLUT_5Plus, headbutt.c lines 35 to 100, by digit then tree; TREETYPE_NONE is null.
        // _1 has 12 rows; a digit reaches only the first 10.
        private static readonly SlotCondition?[][][] Tables =
        [
            [[C], [R], [C], [R], [C], [R], [C], [R], [C], [R], [C], [C]],
            [[C, R], [R, C], [C, R], [R, C], [C, R], [R, C], [C, R], [R, C], [C, R], [R, C]],
            [[C, R, N], [C, N, R], [R, C, N], [R, N, C], [N, C, R], [N, R, C], [C, R, N], [C, N, R], [R, C, N], [R, N, C]],
            [[N, C, C, R], [R, N, C, C], [R, R, N, C], [C, R, R, N], [C, C, R, R], [N, C, R, C], [R, N, C, R], [C, R, N, C], [R, C, R, N], [C, R, C, R]],
            [[N, C, C, R, R], [R, N, C, C, R], [R, R, N, C, C], [C, R, R, N, C], [C, C, R, R, N], [N, C, R, C, R], [R, N, C, R, C], [C, R, N, C, R], [R, C, R, N, C], [C, R, C, R, N]],
        ];

        // The kind of the first tree in list order with the tile, as Headbutt_GetTreeTypeFromTable loops; null for a tile in
        // no tree's list or a tree that gives nothing for this ID.
        public static SlotCondition? KindAt(Milestone milestone, int x, int y, uint trainerId)
        {
            int index = Array.FindIndex(milestone.Trees, t => t.Tiles.Any(tile => tile[0] == x && tile[1] == y));
            return index < 0 ? null : Kind(milestone, index, trainerId);
        }

        // The file's tree: among the trees with a stand whose kind holds a wanted species, or with nothing wanted gives any
        // Pokémon, the cheapest to a Pokémon Center, the first in list order at equal cost. Null when there is none.
        public static MilestoneAnchor? AnchorFor(Milestone milestone, uint trainerId, IReadOnlySet<int> wantedSpecies)
        {
            var kinds = milestone.Slots.Where(s => wantedSpecies.Count == 0 || wantedSpecies.Contains(s.Species)).Select(s => s.Condition).ToHashSet();
            var tree = milestone.Trees
                .Where((t, i) => t.Anchor is not null && Kind(milestone, i, trainerId) is { } kind && kinds.Contains(kind))
                .MinBy(t => t.Cost);
            return tree is null ? null : new MilestoneAnchor(milestone.Id, tree.Anchor!, tree.Facing);
        }

        // Headbutt_GetTreeType_Regular: with five or more regular trees the column is the index mod 5, otherwise the
        // table of that many trees. Secret trees come after the regular ones in the list.
        private static SlotCondition? Kind(Milestone milestone, int index, uint trainerId)
        {
            if (milestone.Trees[index].Secret)
            {
                return SlotCondition.SecretTree;
            }
            int regular = milestone.Trees.Count(t => !t.Secret);
            var row = Tables[Math.Min(regular, 5) - 1][trainerId % 10];
            return row[regular >= 5 ? index % 5 : index];
        }
    }
}
