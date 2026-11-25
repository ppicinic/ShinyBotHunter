using Newtonsoft.Json.Linq;
using ShinyBotHunter.Scripts.Milestones;

namespace ShinyBotHunter.Tests
{
    public class RsMilestoneProcessorTests
    {
        private static JObject Trainer(string type, string movement, int sight, int rangeX = 0)
            => JObject.Parse($$"""
                { "object_events": [ { "x": 5, "y": 5, "movement_type": "{{movement}}", "movement_range_x": {{rangeX}}, "movement_range_y": 0,
                                       "trainer_type": "{{type}}", "trainer_sight_or_berry_tree_id": "{{sight}}" } ] }
                """);

        [Fact]
        public void TrainerSight_FacingRight_ExcludesItsLineOnly()
        {
            var tiles = RsMilestoneProcessor.TrainerSight(Trainer("TRAINER_TYPE_NORMAL", "MOVEMENT_TYPE_FACE_RIGHT", 3));

            Assert.Equal(new HashSet<(int X, int Y)> { (6, 5), (7, 5), (8, 5) }, tiles);
        }

        [Theory]
        [InlineData("TRAINER_TYPE_NORMAL", "MOVEMENT_TYPE_ROTATE_CLOCKWISE")]
        [InlineData("TRAINER_TYPE_BURIED", "MOVEMENT_TYPE_FACE_DOWN")]
        public void TrainerSight_RotatingOrBuried_ExcludesAllFour(string type, string movement)
        {
            var tiles = RsMilestoneProcessor.TrainerSight(Trainer(type, movement, 2));

            Assert.Equal(new HashSet<(int X, int Y)> { (5, 4), (5, 3), (5, 6), (5, 7), (4, 5), (3, 5), (6, 5), (7, 5) }, tiles);
        }

        [Fact]
        public void TrainerSight_WalkingTrainer_FromEveryTileOfItsBox()
        {
            var tiles = RsMilestoneProcessor.TrainerSight(Trainer("TRAINER_TYPE_NORMAL", "MOVEMENT_TYPE_WALK_LEFT_AND_RIGHT", 1, rangeX: 1));

            // One step in each direction from (4, 5), (5, 5) and (6, 5).
            Assert.Equal(new HashSet<(int X, int Y)>
            {
                (3, 5), (4, 5), (5, 5), (6, 5), (7, 5),
                (4, 4), (5, 4), (6, 4),
                (4, 6), (5, 6), (6, 6),
            }, tiles);
        }

        [Fact]
        public void TrainerSight_NotATrainer_ExcludesNothing()
        {
            var tiles = RsMilestoneProcessor.TrainerSight(Trainer("TRAINER_TYPE_NONE", "MOVEMENT_TYPE_FACE_RIGHT", 0));

            Assert.Empty(tiles);
        }
    }
}
