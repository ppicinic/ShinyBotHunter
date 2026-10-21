using Microsoft.Extensions.DependencyInjection;
using ShinyBotHunter.Emulators;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// Pins the Ruby/Sapphire party memory map by placing a party at the pret addresses and reading
    /// it back. A wrong offset in RsPointerService does not throw on its own; it reads zeros or
    /// garbage, so the addresses here are literals and never come from the service under test.
    /// </summary>
    public class RsPointerServiceTests
    {
        private const uint PartyCountAddress = 0x03004350; // gPlayerPartyCount
        private const uint PartyAddress = 0x03004360;      // gPlayerParty
        private const int PartySlotSize = 100;

        private static PokemonStateReader BuildReader()
        {
            ServiceCollection services = new();
            services.AddKeyedSingleton<IPointerService>(GameFlavor.RS, new RsPointerService());
            // The DAO is only read by InitAsync; Parse uses the static gender table and an empty
            // metadata lookup, so a null DAO is never touched.
            services.AddKeyedSingleton<IPokemonParser>(GameFlavor.RS, new Gen3PokemonParser(new PokemonMetadataService(null!)));
            services.AddSingleton<GameServices>();
            return new PokemonStateReader(services.BuildServiceProvider().GetRequiredService<GameServices>());
        }

        /// <summary>
        /// A party block with otId equal to pid and pid % 24 == 0: the XOR key is 0 and the
        /// substructures are in identity order, so the growth block's species is the only data and
        /// the checksum is the species itself.
        /// </summary>
        private static byte[] Encode(uint pid, ushort internalSpecies, byte level)
        {
            byte[] block = new byte[PartySlotSize];
            BitConverter.GetBytes(pid).CopyTo(block, 0x00);
            BitConverter.GetBytes(pid).CopyTo(block, 0x04);
            BitConverter.GetBytes(internalSpecies).CopyTo(block, 0x1C);
            BitConverter.GetBytes(internalSpecies).CopyTo(block, 0x20);
            block[0x54] = level;
            return block;
        }

        private static FakeMemoryClient PlaceParty(uint slotsAddress)
        {
            FakeMemoryClient memory = new();
            memory.Place(PartyCountAddress, new byte[] { 2 });
            memory.Place(slotsAddress, Encode(0x30, 277, 5));
            memory.Place(slotsAddress + PartySlotSize, Encode(0x48, 280, 7));
            return memory;
        }

        [Fact]
        public async Task PartyRead_FindsAPartyPlacedAtTheRsAddresses()
        {
            FakeMemoryClient memory = PlaceParty(PartyAddress);

            var state = await BuildReader().GetPartyState(memory, GameFlavor.RS);

            Assert.Equal(2, state.PartySize);
            Assert.Equal(252, state.PartyPokemon[0]!.Species); // Treecko
            Assert.Equal(0x30u, state.PartyPokemon[0]!.PID);
            Assert.Equal(5, state.PartyPokemon[0]!.Level);
            Assert.Equal(255, state.PartyPokemon[1]!.Species); // Torchic
            Assert.Equal(0x48u, state.PartyPokemon[1]!.PID);
            Assert.Equal(7, state.PartyPokemon[1]!.Level);
            // The emulator validates by these, and the party only sits at the requested address
            // when it is read as Absolute.
            FakeMemoryClient.PartyRead read = Assert.IsType<FakeMemoryClient.PartyRead>(memory.LastPartyRead);
            Assert.Equal(PartyAddressing.Absolute, read.Addressing);
            Assert.Equal(PokemonScheme.Gen3, read.Scheme);
        }

        [Fact]
        public async Task PartyRead_RejectsAPartyPlacedOffTheRsSlotAddress()
        {
            FakeMemoryClient memory = PlaceParty(PartyAddress + 4);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => BuildReader().GetPartyState(memory, GameFlavor.RS));
        }
    }
}
