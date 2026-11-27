using MongoDB.Bson.Serialization.Attributes;
using ShinyBotHunter.Games;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Milestones
{
    // One game file's replacement anchors, one document per file in GameFiles. A document written before the rework also holds
    // its goal and picker fields; they are ignored on load, so nothing stored is migrated (FR-42).
    [BsonIgnoreExtraElements]
    public class GameFile
    {
        // The game file name the bot logs as GameFileName.
        [BsonId]
        public string Id { get; set; } = "";
        // The owner's replacement anchors (FR-22), keyed by the saved milestone's name.
        public List<MilestoneAnchor> Anchors { get; set; } = new List<MilestoneAnchor>();

        // FR-21 to FR-23: the file's replacement, else the generated land anchor with the facing the bot has, else for a headbutt
        // milestone with the trainer ID the file's tree for the species still wanted, else null, which is hunted where the bot stands.
        public MilestoneAnchor? AnchorFor(string milestone, Milestone? generated, uint? trainerId, IEnumerable<int> wanted)
            => Anchors.FirstOrDefault(a => a.Milestone == milestone)
                ?? (generated?.Anchor is { } anchor ? new MilestoneAnchor(milestone, anchor, Direction.None) : null)
                ?? (generated is { Modality: Modality.Headbutt } && trainerId is { } id
                    ? HeadbuttTrees.AnchorFor(generated, id, wanted.ToHashSet())
                    : null);
    }

    // A wanted species in any form (FormCode null) or in one form, at Amount; an amount of 0 is a row that is never a target.
    public record GoalEntry(int Species, string? FormCode, int Amount)
    {
        // The count cache's key: the form code, or the species key for any form.
        [BsonIgnore]
        public string Key => FormCode ?? CacheLoadRequest.SpeciesKey(Species);

        public bool Matches(PokemonData pokemon) => pokemon.Species == Species && (FormCode is null || pokemon.FormCode == FormCode);

        public int CaughtIn(IEnumerable<CaughtPokemon> caught, string gameFileName)
            => caught.Count(c => c.Pokemon.IsShiny && c.GameFileName == gameFileName && Matches(c.Pokemon));
    }

    // A milestone's replacement anchor on one file (FR-22). A list entry rather than a dictionary because a milestone name can
    // hold a "." and a dictionary key would become a Mongo field name. Facing is the one the player had when it was set; None
    // keeps the facing the bot has at Start.
    public record MilestoneAnchor(string Milestone, Anchor Anchor, Direction Facing);
}
