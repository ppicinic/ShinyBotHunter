namespace ShinyBotHunter.Persistence.MongoDb
{
    public class PokemonMetadata
    {
        public int Id { get; set; }
        public string PokemonName { get; set; } = null!;
        public string PokeApiName { get; set; } = null!;
        public List<PokemonFormMetadata> Forms { get; set; } = new List<PokemonFormMetadata>();
        public bool HasFemaleForm { get; set; } = false;
    }
}
