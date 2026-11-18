using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.BattlePokemonParser;

namespace ShinyBotHunter.Tests
{
    // The damage picker over the bundled calculator (Data/smogon-*.min.js), read from the app project's folder.
    public class SmogonDamageServiceTests
    {
        private const int Crunch = 242;
        private const int Headbutt = 29;

        public SmogonDamageServiceTests()
        {
            Directory.SetCurrentDirectory(RepoPaths.AppProject);
        }

        private static SmogonDamageService Service()
        {
            PokemonMetadataService metadata = new(null!);
            // The move collection's classes are Gen 4's: Crunch is physical there, special by type in Gen 3.
            metadata.SeedMove(new PokemonMoveMetadata { Id = Crunch, Name = "Crunch", DamageClass = "physical" });
            metadata.SeedMove(new PokemonMoveMetadata { Id = Headbutt, Name = "Headbutt", DamageClass = "physical" });
            return new SmogonDamageService(metadata);
        }

        // A Machamp (Attack 130, Sp. Atk 65) with Crunch in slot 0 and Headbutt in slot 1 against a Rattata that no move
        // knocks out. Physical Crunch (Gen 4) beats Headbutt; special Crunch (Gen 3) does not.
        private static PokemonBattleState Machamp(byte crunchPp = 15, byte headbuttPp = 15)
        {
            PokemonData machamp = new() { Name = "Machamp", Move1 = Crunch, Move2 = Headbutt };
            PokemonData rattata = new() { Name = "Rattata" };
            return new PokemonBattleState
            {
                ActivePokemon = new BattlePokemon { Pokemon = machamp, Level = 50, HP = 150, MaxHP = 150, Move1PP = crunchPp, Move2PP = headbuttPp },
                EnemyPokemon = new BattlePokemon { Pokemon = rattata, Level = 50, HP = 9999, MaxHP = 9999 },
            };
        }

        [Fact]
        public async Task PickBestMove_WithoutAGeneration_IsGenerationFour()
        {
            await using SmogonDamageService service = Service();

            int byDefault = service.PickBestMove(Machamp());

            // HGSS's call passes no generation: its answer is the Gen 4 one.
            Assert.Equal(0, byDefault);
            Assert.Equal(service.PickBestMove(Machamp(), 4), byDefault);
            Assert.Equal(1, service.PickBestMove(Machamp(), 3));
        }

        [Fact]
        public async Task PickBestMove_SkipsMovesWithoutPp()
        {
            await using SmogonDamageService service = Service();

            Assert.Equal(1, service.PickBestMove(Machamp(crunchPp: 0)));
            Assert.Equal(-1, service.PickBestMove(Machamp(crunchPp: 0, headbuttPp: 0)));
        }
    }
}
