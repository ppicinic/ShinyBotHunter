using MongoDB.Bson.Serialization.Attributes;
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
        public List<HiddenRow> Hidden { get; set; } = new List<HiddenRow>();
        // Milestone ids and custom phase names the owner archived on this file (FR-17).
        public List<string> Archived { get; set; } = new List<string>();
        // The owner's order labels (FR-18).
        public List<MilestoneLabel> Order { get; set; } = new List<MilestoneLabel>();
        // Custom phase names assigned to this file (FR-16).
        public List<string> Assigned { get; set; } = new List<string>();
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
}
