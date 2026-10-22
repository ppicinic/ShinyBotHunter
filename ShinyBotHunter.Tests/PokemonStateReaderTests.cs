using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Rs;
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

        private static readonly HgssPointerService Addresses = new();

        private static readonly GameProfile Profile = new(
            GameFlavor.HGSS,
            new GameCapabilities(false, false, false, true, false, false),
            [0],
            _ => Addresses,
            new StubParser(),
            battleParser: null);

        private static PokemonStateReader BuildReader() => new();

        [Fact]
        public async Task PartyRead_UsesTheHgssMemoryMap()
        {
            FakeMemoryClient memory = new(partySize: 2);

            await BuildReader().GetPartyState(memory, Profile, Addresses);

            FakeMemoryClient.PartyRead read = Assert.IsType<FakeMemoryClient.PartyRead>(memory.LastPartyRead);
            Assert.Equal(0x21D4158u, read.Anchor);
            Assert.Equal(-0x23F44, read.CountOffset);
            Assert.Equal(-0x23F40, read.SlotsOffset);
            Assert.Equal(0xEC, read.SlotSize);
            Assert.Equal(0xEC, read.SlotStride);
            Assert.Equal(6, read.MaxSlots);
            // Gen 4 reaches the party through a pointer and stores it in its own structure; asking
            // the emulator to validate it any other way rejects every slot.
            Assert.Equal(PartyAddressing.Anchor, read.Addressing);
            Assert.Equal(PokemonScheme.Gen4, read.Scheme);
        }

        [Fact]
        public async Task PartyRead_ReturnsOneParsedPokemonPerBlock()
        {
            FakeMemoryClient memory = new(partySize: 3);

            var state = await BuildReader().GetPartyState(memory, Profile, Addresses);

            Assert.Equal(3, state.PartySize);
            Assert.NotNull(state.PartyPokemon[0]);
            Assert.NotNull(state.PartyPokemon[2]);
        }

        [Fact]
        public async Task BoxRead_DumpsEveryBoxInOneRead()
        {
            FakeMemoryClient memory = new(partySize: 0);

            await BuildReader().GetPokemonState(memory, Profile, Addresses);

            // 18 boxes of 4096 bytes, read as a single block rather than slot by slot.
            Assert.Contains(memory.ByteReads, r => r.Length == 18 * 4096);
            // The box base is the dereferenced anchor less 0x148D8; the fake anchor reads as 0.
            Assert.Contains(memory.ByteReads, r => r.Address == unchecked((uint)(0 - 0x148D8)));
        }

        [Fact]
        public async Task BoxRead_OnRsProfile_ThrowsNamingCanReadBoxes()
        {
            GameProfile rs = RsProfile.Create(new PokemonMetadataService(null!));
            FakeMemoryClient memory = new(partySize: 0);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => BuildReader().GetPokemonState(memory, rs, rs.Addresses(1)));

            Assert.Contains("CanReadBoxes", ex.Message);
            Assert.Null(memory.LastPartyRead);
            Assert.Empty(memory.ByteReads);
        }
    }
}
