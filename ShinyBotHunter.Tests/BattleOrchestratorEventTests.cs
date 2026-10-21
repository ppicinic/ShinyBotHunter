using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    // Every metadata here leaves IsDay and IsNight null, so the clock read inside WillEvolve
    // does not affect the result.
    public class BattleOrchestratorEventTests
    {
        [Fact]
        public void WillEvolve_BeautyMetAndLevelReached_ReturnsTrue()
        {
            var metadata = new EvolutionMetadata { MinLevel = 30, MinBeauty = 170, EvolveToId = 350 };
            var mon = new PokemonData { Level = 30, Beauty = 170 };

            bool result = BattleOrchestratorEvent.WillEvolve(mon, metadata, new PokemonBattleState());

            Assert.True(result);
        }

        [Fact]
        public void WillEvolve_HappinessMetWithNoMinLevel_ReturnsTrue()
        {
            var metadata = new EvolutionMetadata { MinHappiness = 220, EvolveToId = 169 };
            var mon = new PokemonData { Level = 30, Happiness = 255 };

            bool result = BattleOrchestratorEvent.WillEvolve(mon, metadata, new PokemonBattleState());

            Assert.True(result);
        }

        [Fact]
        public void GetLevelUp_SecondMonLevelled_ReturnsIt()
        {
            var oldState = new PokemonBattleState { PartyPokemon = { new PokemonData { Level = 10 }, new PokemonData { Level = 10 } } };
            var levelled = new PokemonData { Level = 11 };
            var newState = new PokemonBattleState { PartyPokemon = { new PokemonData { Level = 10 }, levelled } };

            PokemonData? result = BattleOrchestratorEvent.GetLevelUp(oldState, newState);

            Assert.Same(levelled, result);
        }
    }
}
