using MongoDB.Bson;
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

        /// <summary>
        /// Where each caught shiny actually lives. The name list above answers "is this done"; this
        /// answers "which ROM copy's save is the shiny on", which is what someone opening the
        /// collection later needs. Kept alongside the names rather than replacing them so documents
        /// written before this field existed still load.
        /// </summary>
        public List<CompletedFind> CompletedFinds { get; set; } = new();

        /// <summary>The starter this hunt is working on, or null between starters.</summary>
        public string? CurrentStarter { get; set; }

        /// <summary>
        /// ROM copies whose save has been created and flushed to disk.
        ///
        /// Recorded here rather than inferred from the copy existing on disk. Creating a save copies
        /// the ROM first and writes the save minutes later, so a run interrupted in between leaves a
        /// copy with no save behind it. Treating that copy as ready means continuing a game that was
        /// never started, which desyncs every macro after it rather than failing.
        ///
        /// Only written once the save is flushed, so an interrupted creation is simply redone.
        /// </summary>
        public List<string> SavesCreated { get; set; } = new();

        /// <summary>
        /// ROM copies holding a saved shiny, permanently. A retired copy is never allocated to a
        /// hunt again: its save file IS the caught shiny, and pointing an emulator at it would
        /// eventually overwrite the one thing this whole system exists to produce. Mirrors what the
        /// HGSS hunt does with its GameIds map.
        /// </summary>
        public List<string> RetiredCopies { get; set; } = new();

        /// <summary>
        /// Shinies found but not yet played out and saved, within the current run.
        ///
        /// Deliberately not carried across a restart. A parked shiny only exists as a live emulator
        /// sitting mid-battle, and that emulator dies with the process, so a pending entry that
        /// survived would describe something that is no longer there. A hunt clears these when it
        /// starts and reports what it dropped.
        /// </summary>
        public List<PendingShiny> PendingShinies { get; set; } = new();

        public DateTime StartedUtc { get; set; }

        public DateTime UpdatedUtc { get; set; }
    }

    /// <summary>A caught shiny and the ROM copy whose save file it lives on.</summary>
    public class CompletedFind
    {
        public required string Starter { get; set; }

        /// <summary>The ROM copy holding the save -- open this copy in BizHawk to see the shiny.</summary>
        public required string CopyHandle { get; set; }

        public string? RecordingName { get; set; }

        [BsonRepresentation(BsonType.Int64)]
        public uint PID { get; set; }

        public ushort Species { get; set; }

        public DateTime CaughtUtc { get; set; }
    }

    /// <summary>A shiny waiting to be played out by hand and saved.</summary>
    public class PendingShiny
    {
        public required string Starter { get; set; }

        /// <summary>Which ROM copy it is sitting on, so it can be identified after a restart.</summary>
        public required string RomHandle { get; set; }

        public required string RecordingName { get; set; }

        // Int64 like PokemonData's uint fields: the driver stores a bare uint as a checked Int32,
        // so any PID with the high bit set -- half of them -- threw an overflow at the exact moment
        // a shiny was being recorded.
        [BsonRepresentation(BsonType.Int64)]
        public uint PID { get; set; }

        public ushort Species { get; set; }

        public DateTime FoundUtc { get; set; }

        /// <summary>The pokemon itself, so the entry says what is waiting without reading it again.</summary>
        [BsonIgnoreIfNull]
        public PokemonData? PokemonData { get; set; }
    }
}
