using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Games.Rs;
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

        // The DAO is only read by InitAsync; Parse uses the static gender table and an empty
        // metadata lookup, so a null DAO is never touched.
        private static readonly GameProfile Profile = RsProfile.Create(new PokemonMetadataService(null!), new Pathfinding.MapService(new Dictionary<int, Pathfinding.Map>()));

        private static PokemonStateReader BuildReader() => new();

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

            var state = await BuildReader().GetPartyState(memory, Profile, Profile.Addresses("AXPE", 2));

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
                () => BuildReader().GetPartyState(memory, Profile, Profile.Addresses("AXPE", 2)));
        }

        [Fact]
        public async Task BoxRead_FindsAPokemonPlacedInABoxAtGPokemonStorage()
        {
            const uint BoxesAddress = 0x020300A4; // gPokemonStorage.boxes
            FakeMemoryClient memory = PlaceParty(PartyAddress);
            memory.Place(BoxesAddress + 2 * 2400 + 6 * 80, Encode(0x60, 280, 0)[..80]); // box 3, slot 7

            var state = await BuildReader().GetPokemonState(memory, Profile, Profile.Addresses("AXPE", 2));

            // 14 boxes of 30 80-byte slots in one read, and the party count is never read as a pointer.
            Assert.Equal((BoxesAddress, 14 * 2400), Assert.Single(memory.ByteReads));
            Assert.Empty(memory.UIntReads);
            Assert.Equal(0x60u, state.BoxPokemon[2][6]!.PID);
            Assert.Single(state.BoxPokemon.SelectMany(box => box), slot => slot is not null);
            Assert.True(state.Search(new PokemonData { PID = 0x60, Species = 255 }));
        }

        [Fact]
        public void MembersForLaterTasksAreThePretAddresses()
        {
            RsPointerService table = new();

            Assert.Equal(0x020239F8u, table.BattleTypeFlags);
            Assert.Equal(0x030045C0u, table.EnemyParty);
            Assert.Equal(0x02025732u, table.RunWatchX);
            Assert.Equal(0x02025734u, table.RunWatchY);
            Assert.Equal(0x0802C099u, table.HandleInputChooseAction);
            Assert.Equal(0x0802C68Du, table.HandleInputChooseMove);
            Assert.Equal(0x02024C10u, table.BattlescriptCurrInstr);
            Assert.Equal(0x02024E82u, table.MoveToLearn);
            Assert.Equal(0x0809E281u, table.SummaryMoveInputTask);
            Assert.Equal(0x0809E15Du, table.SummaryDestroyTask);
            Assert.Equal(0x0202F38Fu, table.PaletteFadeActive);
        }

        // pret symbols, commit 78b1990, line 24455: BattleScript_AskToLearnMove + 0x11 and + 0x20, script addresses
        // without a Thumb bit.
        [Theory]
        [InlineData(2)]
        [InlineData(1)]
        public void SapphireHasItsLearnMoveScript(int revision)
        {
            var table = (RsPointerService)Profile.Addresses("AXPE", revision);

            Assert.Equal(0x081D8ED2u, table.DeleteMoveYesNo);
            Assert.Equal(0x081D8EE1u, table.StopLearningYesNo);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(1)]
        public void RubyHasItsLearnMoveScript(int revision)
        {
            var table = (RsPointerService)Profile.Addresses("AXVE", revision);

            Assert.Equal(0x081D8F42u, table.DeleteMoveYesNo);
            Assert.Equal(0x081D8F51u, table.StopLearningYesNo);
        }

        // pret symbols, commit 78b1990, line 4437: WaitForAorBPress, + 1 for the Thumb bit.
        [Theory]
        [InlineData(2)]
        [InlineData(1)]
        public void SapphireHasItsWaitForButton(int revision)
        {
            var table = (RsPointerService)Profile.Addresses("AXPE", revision);

            Assert.Equal(0x080670E5u, table.WaitForAorBPress);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(1)]
        public void RubyHasItsWaitForButton(int revision)
        {
            var table = (RsPointerService)Profile.Addresses("AXVE", revision);

            Assert.Equal(0x080670E1u, table.WaitForAorBPress);
        }

        [Theory]
        [InlineData("AXVD")]
        [InlineData(null)]
        public void OtherCodesHaveNoPerGamePart(string? gameCode)
        {
            var table = (RsPointerService)Profile.Addresses(gameCode, 2);

            Assert.Null(table.WaitForAorBPress);
            Assert.Null(table.DeleteMoveYesNo);
            Assert.Null(table.StopLearningYesNo);
            Assert.Null(table.PartyMenuMainCallback2);
        }

        // pret symbols, commit 78b1990, line 4647: CB2_PartyMenuMain, + 1 for the Thumb bit.
        [Theory]
        [InlineData("AXPE", 2, 0x0806AF01u)]
        [InlineData("AXPE", 1, 0x0806AF01u)]
        [InlineData("AXVE", 2, 0x0806AEFDu)]
        [InlineData("AXVE", 1, 0x0806AEFDu)]
        public void EachGameHasItsPartyMenuMain(string gameCode, int revision, uint callback2)
        {
            var table = (RsPointerService)Profile.Addresses(gameCode, revision);

            Assert.Equal(callback2, table.PartyMenuMainCallback2);
        }

        [Theory]
        [InlineData("AXPE")]
        [InlineData("AXVE")]
        public void RevisionZeroIsStillRefused(string gameCode)
        {
            Assert.Throws<NotSupportedException>(() => Profile.Addresses(gameCode, 0));
        }

        [Fact]
        public void EveryGameAndRevisionSharesTheRamTable()
        {
            RsPointerService[] tables =
            [
                (RsPointerService)Profile.Addresses("AXPE", 2),
                (RsPointerService)Profile.Addresses("AXPE", 1),
                (RsPointerService)Profile.Addresses("AXVE", 2),
                (RsPointerService)Profile.Addresses("AXVE", 1),
                (RsPointerService)Profile.Addresses("AXVD", 2),
                (RsPointerService)Profile.Addresses(null, 2),
            ];

            foreach (RsPointerService table in tables)
            {
                Assert.Equal(0x03004350u, table.Anchor);
                Assert.Equal(new BoxLayout(0x020300A4 - 0x03004350, 14, 30, 30 * 80), table.BoxLayout);
                AssertSharedMembers(table);
            }
        }

        [Fact]
        public void SharedMembersHaveThePretAddresses()
        {
            AssertSharedMembers(new RsPointerService());
        }

        [Fact]
        public void EventFlagByteIsTheFlagsByteForTheFlag()
        {
            // Calvin's trainer flag, 0x500 + 318 (research "Flags, variables and trainers in memory").
            Assert.Equal(0x02026954u + 199, new RsPointerService().EventFlagByte(0x500 + 318));
        }

        // pret symbols, commit 78b1990; functions + 1 for the Thumb bit.
        private static void AssertSharedMembers(RsPointerService table)
        {
            Assert.Equal(0x02026954u, table.EventFlags);
            Assert.Equal(0x120, table.EventFlagBytes);
            Assert.Equal(0x02026A74u, table.Vars);
            Assert.Equal(0x200, table.VarBytes);
            Assert.Equal(0x0202FF5Eu, table.TrainerBattleOpponent);
            Assert.Equal(0x0202E892u, table.FieldMessageWindowState);
            Assert.Equal(0x030005BCu, table.ScriptNative);
            Assert.Equal(0x030005B9u, table.ScriptContextMode);
            Assert.Equal(0x0202E85Au, table.PlayerRunningState);
            Assert.Equal(0x0202E85Bu, table.PlayerTileTransition);
            Assert.Equal(0x080B5505u, table.TaskHandleYesNoInput);
            Assert.Equal(0x03004B20u, table.Tasks);
            Assert.Equal(0x03001774u, table.Callback2);
            Assert.Equal(0x0800F809u, table.BattleMainCallback2);
            Assert.Equal(0x02024E64u, table.MoveSelectionCursor);
            Assert.Equal(0x02016018u, table.ExpGetterMonId);
            Assert.Equal(0x0201601Au, table.MoveLearnPromptState);
            Assert.Equal(0x0201609Cu, table.LevelUpBoxState);
            Assert.Equal(0x02018079u, table.ForgetMoveCursor);
            Assert.Equal(0x0811242Du, table.TaskEvolutionScene);
            Assert.Equal(0x02024A6Au, table.ActiveBattlerPartyIndex);
            Assert.Equal(0x08095139u, table.BattlePartyMenuTask);
            Assert.Equal(0x08095429u, table.BattlePartyPopupTask);
            Assert.Equal(0x02020004u, table.Sprites);
        }

        [Fact]
        public void HgssRunWatchIsTheTrainerCoordinates()
        {
            HgssPointerService table = new();

            Assert.Equal(table.TrainerX, table.RunWatchX);
            Assert.Equal(table.TrainerY, table.RunWatchY);
        }
    }
}
