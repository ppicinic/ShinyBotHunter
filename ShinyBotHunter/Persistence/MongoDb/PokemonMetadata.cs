using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Options;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;

namespace ShinyBotHunter.Persistence.MongoDb
{
    public class PokemonMetadata
    {
        public int Id { get; set; }
        public string PokemonName { get; set; } = null!;
        public string PokeApiName { get; set; } = null!;
        public string PokeApiSpeciesName { get; set; } = null!;
        public List<PokemonFormMetadata> Forms { get; set; } = new List<PokemonFormMetadata>();
        [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfDocuments)]
        public Dictionary<GameFlavor, List<PokemonLevelUpMoveMetadata>> LevelUpMoves { get; set; } = new Dictionary<GameFlavor, List<PokemonLevelUpMoveMetadata>>();
        [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfDocuments)]
        public Dictionary<GameFlavor, List<EvolutionMetadata>> EvolutionDetails { get; set; } = new Dictionary<GameFlavor, List<EvolutionMetadata>>();
        public bool HasFemaleForm { get; set; } = false;
    }

    public class EvolutionMetadata
    {
        public int? MinLevel { get; set; } = null;
        public int EvolveToId { get; set; }
        public int? HeldItemId { get; set; } = null;
        public int? Gender { get; set; } = null;
        public bool? IsNight { get; set; } = null;
        public bool? IsDay { get; set; } = null;
        public int? MinHappiness { get; set; } = null;
        public int? TyrogueValue { get; set; } = null; // -1 - hitmonchan, 0 - hitmontop, 1 - hitmonlee
        public int? KnownMoveId { get; set; } = null;
        public int? PartySpeciesId { get; set; } = null;
        public int? MinBeauty { get; set; } = null;
    }

    public class WrappedEvolutionMetadata
    {
        public EvolutionMetadata EvolutionDetails { get; set; } = null!;
        public PokemonData Pokemon { get; set; } = null!;
    }

    public class PokemonMoveMetadata
    {
        public int Id { get; set; }
        public string NameId { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string DamageClass { get; set; } = null!;
        [BsonIgnore]
        public bool IsPhysicalMove { get => DamageClass == "physical"; }
        [BsonIgnore]
        public bool IsSpecialMove { get => DamageClass == "special"; }
        [BsonIgnore]
        public bool IsDamageMove { get => IsPhysicalMove || IsSpecialMove; }
    }

    public class PokemonLevelUpMoveMetadata : IComparable<PokemonLevelUpMoveMetadata>
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public int Level { get; set; }
        public int? Order { get; set; } = null;

        public int CompareTo(PokemonLevelUpMoveMetadata? other)
        {
            if (other is null)
            {
                return -1;
            }
            if (Level == other.Level)
            {
                return Nullable.Compare(Order, other.Order);
            }
            return Level - other.Level;
        }
    }
}
