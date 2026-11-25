using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using ShinyBotHunter.Games;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Milestones
{
    // One game file's goal, the rows its owner hid (FR-7, FR-12) and its picker settings (FR-16 to FR-18), one document per file
    // in GameFiles. A document without a field reads as the field's empty initialiser, so nothing stored is migrated (FR-42).
    public class GameFile
    {
        // The game file name the bot logs as GameFileName.
        [BsonId]
        public string Id { get; set; } = "";
        public List<GoalEntry> Entries { get; set; } = new List<GoalEntry>();
        // The preset that supplied the default amounts in Entries (FR-8); a string, so reordering the enum changes nothing stored.
        [BsonRepresentation(BsonType.String)]
        public GoalPreset Preset { get; set; }
        public List<HiddenRow> Hidden { get; set; } = new List<HiddenRow>();
        // Milestone ids and custom phase names the owner archived on this file (FR-17).
        public List<string> Archived { get; set; } = new List<string>();
        // The owner's order labels (FR-18).
        public List<MilestoneLabel> Order { get; set; } = new List<MilestoneLabel>();
        // Custom phase names assigned to this file (FR-16).
        public List<string> Assigned { get; set; } = new List<string>();
        // The owner's replacement anchors (FR-22).
        public List<MilestoneAnchor> Anchors { get; set; } = new List<MilestoneAnchor>();
        // Existing phases the owner linked to a generated milestone on this file (FR-43).
        public List<MilestoneLink> Links { get; set; } = new List<MilestoneLink>();

        // FR-44: a generated milestone's counts on this file are its own and those of every phase linked to it here.
        public List<string> CountedPhases(string milestoneId)
            => [milestoneId, .. Links.Where(l => l.MilestoneId == milestoneId).Select(l => l.Phase)];

        // FR-21 to FR-23: the file's replacement, else the generated land anchor with the facing the bot has, else for a headbutt
        // milestone with the trainer ID the file's tree for the species still wanted, else null, which is hunted where the bot stands.
        public MilestoneAnchor? AnchorFor(string milestone, Milestone? generated, uint? trainerId, IReadOnlyList<CaughtPokemon> caught)
            => Anchors.FirstOrDefault(a => a.Milestone == milestone)
                ?? (generated?.Anchor is { } anchor ? new MilestoneAnchor(milestone, anchor, Direction.None) : null)
                ?? (generated is { Modality: Modality.Headbutt } && trainerId is { } id
                    ? HeadbuttTrees.AnchorFor(generated, id, MilestoneRows.Build(generated, this, caught).Where(r => r.IsTarget).Select(r => r.Species).ToHashSet())
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

    // A row of one milestone the owner hid; Key is the row's GoalEntry.Key.
    public record HiddenRow(string MilestoneId, string Key);

    // A milestone's order label on one file ("1-4"). A list of these rather than a dictionary, as HiddenRow is, because a phase
    // name can hold a "." and a dictionary key would become a Mongo field name.
    public record MilestoneLabel(string Milestone, string Label);

    // A milestone's replacement anchor on one file (FR-22), a list entry as MilestoneLabel is. Facing is the one the player had
    // when it was set; None keeps the facing the bot has at Start.
    public record MilestoneAnchor(string Milestone, Anchor Anchor, Direction Facing);

    // An existing phase read as part of a generated milestone on one file (FR-43), a list entry as HiddenRow is. Nothing is
    // logged under Phase once it is linked (FR-44a).
    public record MilestoneLink(string Phase, string MilestoneId);
}
