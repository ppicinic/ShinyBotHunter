using MongoDB.Bson.Serialization.Attributes;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;

namespace ShinyBotHunter.BotEvents
{
    /// <summary>
    /// What a Ruby/Sapphire starter hunt has achieved so far, kept in MongoDB so restarting the
    /// application does not lose it.
    ///
    /// A hunt runs for hours or days. Without this, a crash or a restart means a caught shiny is
    /// remembered only as an encounter row, with nothing recording that this hunt had finished with
    /// that starter -- so the next run would begin hunting a starter that is already done.
    ///
    /// Mirrors what the HGSS hunt keeps, in the same PhaseStates collection and keyed the same way,
    /// so both are inspectable together.
    /// </summary>
    public class StarterHuntGameState : BaseGameState
    {
        /// <summary>Starter names already caught, so a resumed hunt skips them.</summary>
        public List<string> CompletedStarters { get; set; } = new();

        /// <summary>The starter this hunt is working on, or null between starters.</summary>
        public string? CurrentStarter { get; set; }

        /// <summary>
        /// Shinies found but not yet played out and saved. A hunt parks rather than finishing when it
        /// finds one, and that pause can outlive the process.
        /// </summary>
        public List<PendingShiny> PendingShinies { get; set; } = new();

        /// <summary>Encounters seen across the whole hunt, for progress rather than statistics.</summary>
        public long TotalEncounters { get; set; }

        /// <summary>Shinies rejected as illegal, which is the number the QA pass exists to measure.</summary>
        public long RejectedIllegalShinies { get; set; }

        public DateTime StartedUtc { get; set; }

        public DateTime UpdatedUtc { get; set; }
    }

    /// <summary>A shiny waiting to be played out by hand and saved.</summary>
    public class PendingShiny
    {
        public required string Starter { get; set; }

        /// <summary>Which ROM copy it is sitting on, so it can be identified after a restart.</summary>
        public required string RomHandle { get; set; }

        public required string RecordingName { get; set; }

        public uint PID { get; set; }

        public ushort Species { get; set; }

        public DateTime FoundUtc { get; set; }

        /// <summary>
        /// The pokemon itself, so a restart can still report what is waiting without the emulator it
        /// was found on being available.
        /// </summary>
        [BsonIgnoreIfNull]
        public PokemonData? PokemonData { get; set; }
    }
}
