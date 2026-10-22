using ShinyBotHunter.Persistence.MongoDb;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// Round-trips the Gen 4 structure: build one the way the game would, then assert the parser
    /// recovers it. Gen 4 encryption is an XOR keystream, so the test encrypts with its own LCRNG.
    /// </summary>
    public class HgssPokemonParserTests
    {
        // Transcribed from the published Gen 4 PKM shuffle order (Project Pokemon / PKHeX), NOT from
        // the parser, which holds the inverse "unshuffle" table. Letter j names the logical block
        // stored at position j. The checksum is blind to block order, so only distinct per-block
        // values asserted under every shift catch a wrong row.
        private static readonly string[] ReferenceBlockOrder =
        {
            "ABCD", "ABDC", "ACBD", "ACDB", "ADBC", "ADCB",
            "BACD", "BADC", "BCAD", "BCDA", "BDAC", "BDCA",
            "CABD", "CADB", "CBAD", "CBDA", "CDAB", "CDBA",
            "DABC", "DACB", "DBAC", "DBCA", "DCAB", "DCBA",
        };

        private static readonly PokemonMetadata Cyndaquil = new() { Id = 155, PokemonName = "Cyndaquil", PokeApiName = "cyndaquil" };

        /// <summary>
        /// Encodes a pokemon the way the game stores it: blocks A-D filled in logical order, the
        /// checksum taken over them, then shuffled by PID bits 13-17 and encrypted with the checksum
        /// as LCRNG seed. A party-sized block adds battle stats encrypted with the PID as seed.
        /// </summary>
        private static byte[] Build(uint pid, uint otId = 0x56780042, ushort species = 155,
            uint ivs = 0, byte formByte = 0, bool partySized = true, byte status = 0, byte level = 5,
            ushort hp = 20, ushort maxHp = 20, ushort[]? stats = null)
        {
            byte[] plain = new byte[partySized ? 0xEC : 0x88];
            BitConverter.GetBytes(pid).CopyTo(plain, 0x00);

            // Block A
            BitConverter.GetBytes(species).CopyTo(plain, 0x08);
            BitConverter.GetBytes((ushort)0x00D3).CopyTo(plain, 0x0A);   // held item
            BitConverter.GetBytes(otId).CopyTo(plain, 0x0C);
            plain[0x15] = 66;                                            // ability

            // Block B
            BitConverter.GetBytes((ushort)33).CopyTo(plain, 0x28);
            BitConverter.GetBytes((ushort)43).CopyTo(plain, 0x2A);
            BitConverter.GetBytes((ushort)52).CopyTo(plain, 0x2C);
            BitConverter.GetBytes((ushort)108).CopyTo(plain, 0x2E);
            plain[0x30] = 35; plain[0x31] = 30; plain[0x32] = 25; plain[0x33] = 20;
            BitConverter.GetBytes(ivs).CopyTo(plain, 0x38);
            plain[0x40] = formByte;

            // Block C and D
            EncodeName("Sparky").CopyTo(plain, 0x48);
            EncodeName("Lyra").CopyTo(plain, 0x68);

            ushort checksum = 0;
            for (int i = 0x08; i < 0x88; i += 2) checksum += BitConverter.ToUInt16(plain, i);
            BitConverter.GetBytes(checksum).CopyTo(plain, 0x06);

            byte[] raw = new byte[plain.Length];
            Array.Copy(plain, raw, 0x08);

            void Encrypt(int from, int to, int length, ref uint seed)
            {
                for (int i = 0; i < length; i += 2)
                {
                    seed = seed * 0x41C64E6D + 0x6073;
                    ushort word = (ushort)(BitConverter.ToUInt16(plain, from + i) ^ (seed >> 16));
                    BitConverter.GetBytes(word).CopyTo(raw, to + i);
                }
            }

            string order = ReferenceBlockOrder[((pid & 0x3E000) >> 13) % 24];
            uint blockSeed = checksum;
            for (int position = 0; position < 4; position++)
            {
                int logical = order[position] - 'A';
                Encrypt(0x08 + logical * 0x20, 0x08 + position * 0x20, 0x20, ref blockSeed);
            }

            if (partySized)
            {
                plain[0x88] = status;
                plain[0x8C] = level;
                BitConverter.GetBytes(hp).CopyTo(plain, 0x8E);
                BitConverter.GetBytes(maxHp).CopyTo(plain, 0x90);
                ushort[] battle = stats ?? new ushort[] { 11, 12, 13, 14, 15 };
                for (int i = 0; i < 5; i++) BitConverter.GetBytes(battle[i]).CopyTo(plain, 0x92 + i * 2);

                uint battleSeed = pid;
                Encrypt(0x88, 0x88, 0x64, ref battleSeed);
            }
            return raw;
        }

        // Gen 4 character codes for ASCII letters, from the published table: A-Z at 0x012B,
        // a-z at 0x0145, terminated by 0xFFFF.
        private static byte[] EncodeName(string name)
        {
            byte[] bytes = new byte[(name.Length + 1) * 2];
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                ushort code = char.IsUpper(c) ? (ushort)(0x012B + c - 'A') : (ushort)(0x0145 + c - 'a');
                BitConverter.GetBytes(code).CopyTo(bytes, i * 2);
            }
            BitConverter.GetBytes((ushort)0xFFFF).CopyTo(bytes, name.Length * 2);
            return bytes;
        }

        private static PokemonMetadataService MetadataFor(params PokemonMetadata[] entries)
        {
            // The DAO is never touched: InitAsync is not called, the entries are seeded directly.
            var service = new PokemonMetadataService(null!);
            foreach (var entry in entries) service.Seed(entry);
            return service;
        }

        [Fact]
        public void RoundTrips_UnderEveryBlockOrder()
        {
            var parser = new HgssPokemonParser(MetadataFor(Cyndaquil));
            uint ivs = 1u | (2u << 5) | (3u << 10) | (4u << 15) | (5u << 20) | (6u << 25);

            // The shift field is five bits wide, so 24-31 also cover the % 24 wrap.
            for (uint value = 0; value < 32; value++)
            {
                uint pid = 0xA1B00000 | (value << 13) | 0x0155;
                string where = $"shift field {value} (order {value % 24})";

                var mon = parser.Parse(Build(pid, ivs: ivs))!;

                Assert.False(mon.IsCorrupt, $"{where} failed to decode.");
                Assert.True(mon.Species == 155, $"{where}: species {mon.Species}");
                Assert.True(mon.Name == "Cyndaquil", $"{where}: name {mon.Name}");
                Assert.True(mon.HeldItem == 0x00D3, $"{where}: held item {mon.HeldItem}");
                Assert.True(mon.Ability == 66, $"{where}: ability {mon.Ability}");
                Assert.True(mon.Move1 == 33 && mon.Move2 == 43 && mon.Move3 == 52 && mon.Move4 == 108,
                    $"{where}: moves {mon.Move1},{mon.Move2},{mon.Move3},{mon.Move4}");
                Assert.True(mon.Move1PP == 35 && mon.Move4PP == 20, $"{where}: PP {mon.Move1PP},{mon.Move4PP}");
                Assert.True(mon.IVs == ivs, $"{where}: IV word {mon.IVs:X8}");
                Assert.True(mon.Nickname == "Sparky", $"{where}: nickname {mon.Nickname}");
                Assert.True(mon.OTName == "Lyra", $"{where}: OT name {mon.OTName}");
            }
        }

        [Fact]
        public void ReadsTrainerIdsAndTheFieldsShininessDependsOn()
        {
            var parser = new HgssPokemonParser(MetadataFor(Cyndaquil));

            var mon = parser.Parse(Build(pid: 0xABCD1234, otId: 0x56780042))!;

            Assert.Equal(0xABCD1234u, mon.PID);
            Assert.Equal(0x56780042u, mon.OTId);
            Assert.Equal(0x0042, mon.TID);
            Assert.Equal(0x5678, mon.SID);
        }

        [Fact]
        public void ShinyPidIsRecognised()
        {
            // Shiny when TID ^ SID ^ PIDhigh ^ PIDlow < 8; this makes it exactly 0.
            const ushort tid = 0x1234, sid = 0x5678;
            uint otId = (uint)(sid << 16) | tid;
            uint pid = (uint)(tid ^ sid) << 16;
            var parser = new HgssPokemonParser(MetadataFor(Cyndaquil));

            var mon = parser.Parse(Build(pid, otId))!;

            Assert.True(mon.IsShiny, $"shiny value was {mon.ShinyValue}");
        }

        [Fact]
        public void ShinyValueOfEightIsNotShiny()
        {
            const ushort tid = 0x1234, sid = 0x5678;
            uint otId = (uint)(sid << 16) | tid;
            uint pid = ((uint)(tid ^ sid) << 16) | 8u;
            var parser = new HgssPokemonParser(MetadataFor(Cyndaquil));

            var mon = parser.Parse(Build(pid, otId))!;

            Assert.Equal(8, mon.ShinyValue);
            Assert.False(mon.IsShiny);
        }

        [Fact]
        public void IvWordUnpacksIntoSixStatsAndTheEggFlag()
        {
            // hp=1 atk=2 def=3 spe=4 spatk=5 spdef=6, packed five bits each, plus the egg bit 30.
            uint ivs = 1u | (2u << 5) | (3u << 10) | (4u << 15) | (5u << 20) | (6u << 25) | (1u << 30);
            var parser = new HgssPokemonParser(MetadataFor(Cyndaquil));

            var mon = parser.Parse(Build(0x2468ACE0, ivs: ivs))!;

            Assert.Equal(1, mon.IVHp);
            Assert.Equal(2, mon.IVAtk);
            Assert.Equal(3, mon.IVDef);
            Assert.Equal(4, mon.IVSpe);
            Assert.Equal(5, mon.IVSpAtk);
            Assert.Equal(6, mon.IVSpDef);
            Assert.True(mon.IsEgg);
        }

        [Fact]
        public void NatureAndGenderComeFromThePid()
        {
            // Bulbasaur's gender threshold is 31: a PID low byte at or above it is male.
            var parser = new HgssPokemonParser(MetadataFor(
                new PokemonMetadata { Id = 1, PokemonName = "Bulbasaur", PokeApiName = "bulbasaur" }));
            uint malePid = 0x1357_0040, femalePid = 0x1357_0010;

            var male = parser.Parse(Build(malePid, species: 1))!;
            var female = parser.Parse(Build(femalePid, species: 1))!;

            Assert.Equal((byte)(malePid % 25), male.Nature);
            Assert.Equal(1, male.Gender);
            Assert.Equal((byte)(femalePid % 25), female.Nature);
            Assert.Equal(2, female.Gender);
        }

        [Fact]
        public void UnownFormIndexComesFromBlockB()
        {
            var parser = new HgssPokemonParser(MetadataFor(
                new PokemonMetadata { Id = 201, PokemonName = "Unown", PokeApiName = "unown" }));

            var mon = parser.Parse(Build(0x0BADF00D, species: 201, formByte: 5 << 3))!;

            Assert.Equal(5, mon.FormIndex);
            Assert.Equal("unown-f", mon.FormCode);
        }

        [Fact]
        public void PartyBlockDecryptsBattleStatsWithThePidSeed()
        {
            var parser = new HgssPokemonParser(MetadataFor(Cyndaquil));

            var mon = parser.Parse(Build(0x7E57C0DE, status: 0x08, level: 17, hp: 30, maxHp: 41,
                stats: new ushort[] { 21, 22, 23, 24, 25 }))!;

            Assert.Equal(0x08, mon.Status);
            Assert.True(mon.IsPoisoned);
            Assert.Equal(17, mon.Level);
            Assert.Equal(30, mon.HP);
            Assert.Equal(41, mon.MaxHP);
            Assert.Equal(21, mon.Attack);
            Assert.Equal(22, mon.Defense);
            Assert.Equal(23, mon.Speed);
            Assert.Equal(24, mon.SpAttack);
            Assert.Equal(25, mon.SpDefense);
        }

        [Fact]
        public void BoxSizedBlockParsesWithoutBattleStats()
        {
            var parser = new HgssPokemonParser(MetadataFor(Cyndaquil));

            var mon = parser.Parse(Build(0x33334444, partySized: false))!;

            Assert.False(mon.IsCorrupt);
            Assert.Equal(155, mon.Species);
            Assert.Equal(0, mon.Level);   // not present in a box-sized block
            Assert.Equal(0, mon.HP);
            Assert.Equal(0, mon.MaxHP);
        }

        [Fact]
        public void CorruptedBytesAreReportedRatherThanReturnedAsData()
        {
            var parser = new HgssPokemonParser(MetadataFor(Cyndaquil));
            byte[] raw = Build(0x1111AAAA);
            raw[0x21] ^= 0xFF;   // flip a byte inside the encrypted blocks

            var mon = parser.Parse(raw);

            Assert.NotNull(mon);
            Assert.True(mon!.IsCorrupt);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(494)]
        public void OutOfRangeSpeciesWithValidChecksumIsCorrupt(int species)
        {
            var parser = new HgssPokemonParser(MetadataFor());

            var mon = parser.Parse(Build(0x22225555, species: (ushort)species))!;

            Assert.True(mon.IsCorrupt);
        }
    }
}
