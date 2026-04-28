namespace ShinyBotHunter.Persistence.MongoDb
{
    public class PokemonFormMetadata
    {
        public string Name { get; set; } = null!;
        public string Version { get; set; } = null!;
        public bool HasFemaleForm { get; set; }
    }
}
