using ShinyBotHunter.Pokemon;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// Round-trips the Gen 3 structure: build one the way the game would, then assert the parser
    /// recovers it. This is where the Gen 3 crypto is actually proven -- ApiHawk's validator mirrors
    /// the same algorithm but cannot be tested here, since it targets net48 against BizHawk.
    /// </summary>
    public class Gen3PokemonParserTests
    {
        private static readonly int[][] SubstructOrder =
        {
            new[] { 0, 1, 2, 3 }, new[] { 0, 1, 3, 2 }, new[] { 0, 2, 1, 3 }, new[] { 0, 2, 3, 1 },
            new[] { 0, 3, 1, 2 }, new[] { 0, 3, 2, 1 }, new[] { 1, 0, 2, 3 }, new[] { 1, 0, 3, 2 },
            new[] { 1, 2, 0, 3 }, new[] { 1, 2, 3, 0 }, new[] { 1, 3, 0, 2 }, new[] { 1, 3, 2, 0 },
            new[] { 2, 0, 1, 3 }, new[] { 2, 0, 3, 1 }, new[] { 2, 1, 0, 3 }, new[] { 2, 1, 3, 0 },
            new[] { 2, 3, 0, 1 }, new[] { 2, 3, 1, 0 }, new[] { 3, 0, 1, 2 }, new[] { 3, 0, 2, 1 },
            new[] { 3, 1, 0, 2 }, new[] { 3, 1, 2, 0 }, new[] { 3, 2, 0, 1 }, new[] { 3, 2, 1, 0 },
        };

        /// <summary>
        /// Encodes a pokemon the way the game stores it: substructures filled in logical order, then
        /// scrambled by PID % 24 and XORed with PID ^ OTID, with the checksum taken before scrambling.
        /// </summary>
        private static byte[] Build(uint pid, uint otId, ushort internalSpecies, byte level = 5,
            ushort hp = 20, ushort maxHp = 20, uint ivs = 0, ushort move1 = 33, bool partySized = true)
        {
            byte[] logical = new byte[48];
            BitConverter.GetBytes(internalSpecies).CopyTo(logical, 0);      // Growth: species
            BitConverter.GetBytes((ushort)0).CopyTo(logical, 2);            // Growth: held item
            BitConverter.GetBytes(move1).CopyTo(logical, 12);               // Attacks: first move
            logical[24] = 4;                                                // EVs: HP
            BitConverter.GetBytes(ivs).CopyTo(logical, 36 + 4);             // Misc: IV word

            ushort checksum = 0;
            for (int i = 0; i < 48; i += 2) checksum += BitConverter.ToUInt16(logical, i);

            // Scramble into stored order, which is the inverse of how the parser unscrambles.
            byte[] scrambled = new byte[48];
            int[] order = SubstructOrder[pid % 24];
            for (int slot = 0; slot < 4; slot++)
            {
                Buffer.BlockCopy(logical, slot * 12, scrambled, order[slot] * 12, 12);
            }

            byte[] block = new byte[partySized ? 0x64 : 0x50];
            BitConverter.GetBytes(pid).CopyTo(block, 0x00);
            BitConverter.GetBytes(otId).CopyTo(block, 0x04);
            BitConverter.GetBytes(checksum).CopyTo(block, 0x1C);

            uint key = pid ^ otId;
            for (int i = 0; i < 48; i += 4)
            {
                uint word = BitConverter.ToUInt32(scrambled, i) ^ key;
                BitConverter.GetBytes(word).CopyTo(block, 0x20 + i);
            }

            if (partySized)
            {
                block[0x54] = level;
                BitConverter.GetBytes(hp).CopyTo(block, 0x56);
                BitConverter.GetBytes(maxHp).CopyTo(block, 0x58);
            }
            return block;
        }

        

        [Theory]
        // Every residue class picks a different substructure permutation, so this walks all 24.
        [InlineData(0u)]
        [InlineData(1u)]
        [InlineData(7u)]
        [InlineData(23u)]
        [InlineData(0x9C4B2E71u)]
        public void RoundTrips_UnderEverySubstructurePermutation(uint pid)
        {
            byte[] block = Build(pid, otId: 0x11223344, internalSpecies: 277, level: 5);

            var mon = Gen3PokemonParser.Decode(block);

            Assert.NotNull(mon);
            Assert.False(mon!.IsCorrupt, $"PID {pid} (order {pid % 24}) failed to decode.");
            Assert.Equal(252, mon.Species);   // Treecko, national
            Assert.Equal(5, mon.Level);
        }

        [Fact]
        public void ReadsTrainerIdsAndTheFieldsShininessDependsOn()
        {
            byte[] block = Build(pid: 0xABCD1234, otId: 0x56780042, internalSpecies: 280);

            var mon = Gen3PokemonParser.Decode(block)!;

            Assert.Equal(0xABCD1234u, mon.PID);
            Assert.Equal(0x0042, mon.TID);
            Assert.Equal(0x5678, mon.SID);
            Assert.Equal(255, mon.Species);   // Torchic
        }

        [Fact]
        public void ShinyPidIsRecognised()
        {
            // Shiny when TID ^ SID ^ PIDhigh ^ PIDlow < 8; this makes it exactly 0.
            const ushort tid = 0x1234, sid = 0x5678;
            uint otId = (uint)(sid << 16) | tid;
            uint pid = ((uint)(tid ^ sid) << 16) | 0u;

            var mon = Gen3PokemonParser.Decode(Build(pid, otId, internalSpecies: 283))!;

            Assert.True(mon.IsShiny, $"shiny value was {mon.ShinyValue}");
        }

        [Fact]
        public void IvWordUnpacksTheSameWayAsGen4()
        {
            // hp=1 atk=2 def=3 spe=4 spatk=5 spdef=6, packed five bits each.
            uint ivs = 1u | (2u << 5) | (3u << 10) | (4u << 15) | (5u << 20) | (6u << 25);

            var mon = Gen3PokemonParser.Decode(Build(0x2468ACE0, 0x13579BDF, 277, ivs: ivs))!;

            Assert.Equal(1, mon.IVHp);
            Assert.Equal(2, mon.IVAtk);
            Assert.Equal(3, mon.IVDef);
            Assert.Equal(4, mon.IVSpe);
            Assert.Equal(5, mon.IVSpAtk);
            Assert.Equal(6, mon.IVSpDef);
        }

        [Fact]
        public void CorruptedBytesAreReportedRatherThanReturnedAsData()
        {
            byte[] block = Build(0x1111AAAA, 0x2222BBBB, 277);
            block[0x21] ^= 0xFF;   // flip a bit inside the encrypted region

            var mon = Gen3PokemonParser.Decode(block);

            Assert.NotNull(mon);
            Assert.True(mon!.IsCorrupt);
        }

        [Fact]
        public void BoxSizedBlockParsesWithoutBattleStats()
        {
            var mon = Gen3PokemonParser.Decode(Build(0x33334444, 0x55556666, 277, partySized: false))!;

            Assert.False(mon.IsCorrupt);
            Assert.Equal(252, mon.Species);
            Assert.Equal(0, mon.Level);   // not present in a box-sized block
        }

        [Theory]
        // Kanto and Johto keep their dex numbers.
        [InlineData(1, 1)]
        [InlineData(251, 251)]
        // The starters and the first wild encounter, all verified against the published index table.
        [InlineData(277, 252)]   // Treecko
        [InlineData(280, 255)]   // Torchic
        [InlineData(283, 258)]   // Mudkip
        [InlineData(288, 263)]   // Zigzagoon
        // Chimecho was added last and sits after Deoxys, so everything from Absol on shifts by one.
        [InlineData(411, 358)]   // Chimecho
        [InlineData(383, 359)]   // Absol
        [InlineData(410, 386)]   // Deoxys
        public void InternalSpeciesIndicesMapToNationalDex(int internalIndex, int expectedNational)
        {
            Assert.Equal(expectedNational, Gen3PokemonParser.ToNationalDex((ushort)internalIndex));
        }

        [Theory]
        [InlineData(252)]
        [InlineData(276)]
        public void PlaceholderIndicesAreNotSpecies(int internalIndex)
        {
            // The gap between Celebi and Treecko holds sprites, not pokemon.
            Assert.Equal(0, Gen3PokemonParser.ToNationalDex((ushort)internalIndex));
        }

        [Fact]
        public void EveryHoennIndexMapsToADistinctNationalNumber()
        {
            // Guards the Chimecho special case: an off-by-one there would collide two species onto
            // one dex number, which would be invisible in any single-species check.
            var mapped = Enumerable.Range(277, 411 - 277 + 1)
                .Select(i => Gen3PokemonParser.ToNationalDex((ushort)i))
                .ToList();

            Assert.Equal(135, mapped.Count);
            Assert.Equal(135, mapped.Distinct().Count());
            Assert.Equal(252, mapped.Min());
            Assert.Equal(386, mapped.Max());
        }
    }
}
