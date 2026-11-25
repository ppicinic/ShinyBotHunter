using MongoDB.Bson.Serialization.Attributes;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Milestones
{
    // One game file's goal and the rows its owner hid (FR-7, FR-12), one document per file in GameFiles.
    public class GameFile
    {
        // The game file name the bot logs as GameFileName.
        [BsonId]
        public string Id { get; set; } = "";
        public List<GoalEntry> Entries { get; set; } = new List<GoalEntry>();
        public List<HiddenRow> Hidden { get; set; } = new List<HiddenRow>();
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
}
