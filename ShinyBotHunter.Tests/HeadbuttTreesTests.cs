using ShinyBotHunter.Games;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    public class HeadbuttTreesTests
    {
        // The owner's HG-Headbutt save: visible ID 15613, secret ID 35374. The game's % 10 is on the full ID (digit 7),
        // read from the decomp; the visible half alone ends in 3.
        private const uint OwnersId = 35374u << 16 | 15613u;
        private const uint VisibleHalf = 15613;

        private static Milestone Azalea()
            => MilestoneFile.Load(Path.Combine(RepoPaths.AppProject, "Data", "HGSS"))[RomVersion.HeartGold].Single(m => m.Id == "74-headbutt");

        // Trees one tile each, at (i, 0), with an optional stand below each costing Cost.
        private static Milestone Hand(Slot[] slots, params (bool Secret, int? Cost)[] trees)
            => new([1], Modality.Headbutt, false, slots, trees.Select((t, i) => new HeadbuttTree(t.Secret, [[i, 0]],
                t.Cost is null ? null : new Anchor(1, i, 1, 0, 0), t.Cost is null ? Direction.None : Direction.Up, t.Cost ?? 0)).ToArray());

        private static Slot Slot(int species, SlotCondition condition) => new(0, species, 1, 1, 100, condition);

        [Fact]
        public void KindAt_AzaleaTree2_NoneForTheFullId_RareForTheVisibleHalf()
        {
            Assert.Equal(2318286077u, OwnersId);
            Assert.Null(HeadbuttTrees.KindAt(Azalea(), 414, 455, OwnersId));
            Assert.Equal(SlotCondition.RareTree, HeadbuttTrees.KindAt(Azalea(), 414, 455, VisibleHalf));
        }

        [Fact]
        public void KindAt_AzaleaTree0_CommonForTheOwnersFile()
        {
            Assert.Equal(SlotCondition.CommonTree, HeadbuttTrees.KindAt(Azalea(), 426, 465, OwnersId));
            Assert.Equal(SlotCondition.CommonTree, HeadbuttTrees.KindAt(Azalea(), 427, 465, OwnersId));
        }

        [Fact]
        public void KindAt_TileNotInTheList_None()
        {
            var azalea = Azalea();

            for (uint digit = 0; digit < 10; digit++)
            {
                Assert.Null(HeadbuttTrees.KindAt(azalea, 425, 465, digit));
            }
        }

        [Fact]
        public void Kind_SmallMaps_ReadTheirOwnTables()
        {
            var one = Hand([], (false, null));
            Assert.Equal(SlotCondition.RareTree, HeadbuttTrees.KindAt(one, 0, 0, 1));
            Assert.Equal(SlotCondition.CommonTree, HeadbuttTrees.KindAt(one, 0, 0, 10));

            var three = Hand([], (false, null), (false, null), (false, null));
            Assert.Equal(new SlotCondition?[] { SlotCondition.CommonTree, SlotCondition.RareTree, null }, Enumerable.Range(0, 3).Select(x => HeadbuttTrees.KindAt(three, x, 0, 0)));

            var four = Hand([], (false, null), (false, null), (false, null), (false, null));
            Assert.Equal(new SlotCondition?[] { SlotCondition.CommonTree, SlotCondition.RareTree, SlotCondition.RareTree, null },
                Enumerable.Range(0, 4).Select(x => HeadbuttTrees.KindAt(four, x, 0, 13)));
        }

        [Fact]
        public void Kind_SecretTree_IsSecretForEveryId()
        {
            var milestone = Hand([], (false, null), (false, null), (true, null));

            for (uint digit = 0; digit < 10; digit++)
            {
                Assert.Equal(SlotCondition.SecretTree, HeadbuttTrees.KindAt(milestone, 2, 0, digit));
            }
        }

        [Fact]
        public void AnchorFor_NoTreeOfTheWantedKind_Null()
        {
            // Two trees, digit 0: common then rare. Only the rare table holds species 2, and the rare tree has no stand.
            var milestone = Hand([Slot(1, SlotCondition.CommonTree), Slot(2, SlotCondition.RareTree)], (false, 5), (false, null));

            Assert.Null(HeadbuttTrees.AnchorFor(milestone, 0, new HashSet<int> { 2 }));
            Assert.Equal(new MilestoneAnchor("1-headbutt", new Anchor(1, 0, 1, 0, 0), Direction.Up), HeadbuttTrees.AnchorFor(milestone, 0, new HashSet<int> { 1 }));
        }

        [Fact]
        public void AnchorFor_CheapestOfTheWantedKind_FirstAtEqualCost()
        {
            // Five trees, digit 4 (common, common, rare, rare, none): the commons cost 9 and 7, the rares 3 and 3, the none 1.
            var milestone = Hand([Slot(1, SlotCondition.CommonTree), Slot(2, SlotCondition.RareTree)],
                (false, 9), (false, 7), (false, 3), (false, 3), (false, 1));

            Assert.Equal(1, HeadbuttTrees.AnchorFor(milestone, 4, new HashSet<int> { 1 })!.Anchor.X);
            Assert.Equal(2, HeadbuttTrees.AnchorFor(milestone, 4, new HashSet<int>())!.Anchor.X);
        }
    }
}
