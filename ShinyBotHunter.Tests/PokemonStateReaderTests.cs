using Microsoft.Extensions.DependencyInjection;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// Pins the memory map a party read uses.
    ///
    /// These offsets used to be literals inside the reader and now come from the flavor's pointer
    /// service. That refactor is invisible if it works and catastrophic if it does not -- a wrong
    /// offset does not throw, it returns believable garbage that gets parsed and persisted. Asserting
    /// on the numbers the reader asks for catches that without an emulator.
    /// </summary>
    public class PokemonStateReaderTests
    {
        private sealed class StubParser : IPokemonParser
        {
            public PokemonData? Parse(byte[] raw) => new PokemonData();
        }

        private static PokemonStateReader BuildReader()
        {
            ServiceCollection services = new();
            services.AddKeyedSingleton<IPointerService>(GameFlavor.HGSS, new HgssPointerService());
            services.AddKeyedSingleton<IPokemonParser>(GameFlavor.HGSS, new StubParser());
            services.AddSingleton<GameServices>();
            return new PokemonStateReader(services.BuildServiceProvider().GetRequiredService<GameServices>());
        }

        [Fact]
        public async Task PartyRead_UsesTheHgssMemoryMap()
        {
            FakeMemoryClient memory = new(partySize: 2);

            await BuildReader().GetPartyState(memory, GameFlavor.HGSS);

            FakeMemoryClient.PartyRead read = Assert.IsType<FakeMemoryClient.PartyRead>(memory.LastPartyRead);
            Assert.Equal(0x21D4158u, read.Anchor);
            Assert.Equal(-0x23F44, read.CountOffset);
            Assert.Equal(-0x23F40, read.SlotsOffset);
            Assert.Equal(0xEC, read.SlotSize);
            Assert.Equal(0xEC, read.SlotStride);
            Assert.Equal(6, read.MaxSlots);
        }

        [Fact]
        public async Task PartyRead_ReturnsOneParsedPokemonPerBlock()
        {
            FakeMemoryClient memory = new(partySize: 3);

            var state = await BuildReader().GetPartyState(memory, GameFlavor.HGSS);

            Assert.Equal(3, state.PartySize);
            Assert.NotNull(state.PartyPokemon[0]);
            Assert.NotNull(state.PartyPokemon[2]);
        }

        [Fact]
        public async Task BoxRead_DumpsEveryBoxInOneRead()
        {
            FakeMemoryClient memory = new(partySize: 0);

            await BuildReader().GetPokemonState(memory, GameFlavor.HGSS);

            // 18 boxes of 4096 bytes, read as a single block rather than slot by slot.
            Assert.Contains(memory.ByteReads, r => r.Length == 18 * 4096);
            // The box base is the dereferenced anchor less 0x148D8; the fake anchor reads as 0.
            Assert.Contains(memory.ByteReads, r => r.Address == unchecked((uint)(0 - 0x148D8)));
        }

        [Fact]
        public async Task ReadingAFlavorWithNoServices_SaysWhatIsMissing()
        {
            FakeMemoryClient memory = new();

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => BuildReader().GetPartyState(memory, GameFlavor.RS));

            Assert.Contains("IPointerService", ex.Message);
            Assert.Contains("RS", ex.Message);
        }
    }
}
