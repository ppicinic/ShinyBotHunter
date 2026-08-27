using ShinyBotHunter.Service;

namespace ShinyBotHunter.Pokemon
{
    /// <summary>
    /// Decodes the Gen 3 stored-pokemon structure (Ruby/Sapphire).
    ///
    /// Shares almost nothing with Gen 4 beyond the idea. There is no LCRNG keystream: the four
    /// twelve-byte substructures are XORed with PID ^ OTID and their order is a permutation chosen by
    /// PID % 24, rather than by bits of the PID. Text uses a one-byte character map instead of the
    /// two-byte one, and species are stored as internal indices that are not National Dex numbers.
    ///
    /// The IV word and the shiny formula happen to be identical to Gen 4, so
    /// <see cref="PokemonData"/> needs no generation-specific handling for either.
    ///
    /// IMPORTANT: the crypto here is mirrored by ApiHawk's Gen3PokemonValidator, which cannot share
    /// this code because it targets net48 against BizHawk. A change here has to be mirrored there.
    /// </summary>
    public class Gen3PokemonParser : IPokemonParser
    {
        private readonly PokemonMetadataService metadataService;

        public Gen3PokemonParser(PokemonMetadataService metadataService)
        {
            this.metadataService = metadataService;
        }

        // Unencrypted header.
        private const int PersonalityOffset = 0x00;
        private const int OtIdOffset = 0x04;
        private const int NicknameOffset = 0x08;
        private const int NicknameLength = 10;
        private const int OtNameOffset = 0x14;
        private const int OtNameLength = 7;
        private const int ChecksumOffset = 0x1C;

        // Encrypted substructures.
        private const int SubstructOffset = 0x20;
        private const int SubstructSize = 12;
        private const int SubstructTotal = SubstructSize * 4;

        // Party-only fields, past the checksummed region.
        private const int LevelOffset = 0x54;
        private const int HpOffset = 0x56;
        private const int MaxHpOffset = 0x58;
        private const int AttackOffset = 0x5A;
        private const int DefenseOffset = 0x5C;
        private const int SpeedOffset = 0x5E;
        private const int SpAttackOffset = 0x60;
        private const int SpDefenseOffset = 0x62;

        public const int BoxBlockSize = 0x50;
        public const int PartyBlockSize = 0x64;

        /// <summary>Highest valid internal species index.</summary>
        private const int MaxInternalSpecies = 411;

        /// <summary>
        /// Substructure order by personality % 24. Index i names the stored slot holding the i-th
        /// logical block, in the order Growth, Attacks, EVs, Miscellaneous.
        /// </summary>
        private static readonly int[][] SubstructOrder =
        {
            new[] { 0, 1, 2, 3 }, new[] { 0, 1, 3, 2 }, new[] { 0, 2, 1, 3 }, new[] { 0, 2, 3, 1 },
            new[] { 0, 3, 1, 2 }, new[] { 0, 3, 2, 1 }, new[] { 1, 0, 2, 3 }, new[] { 1, 0, 3, 2 },
            new[] { 1, 2, 0, 3 }, new[] { 1, 2, 3, 0 }, new[] { 1, 3, 0, 2 }, new[] { 1, 3, 2, 0 },
            new[] { 2, 0, 1, 3 }, new[] { 2, 0, 3, 1 }, new[] { 2, 1, 0, 3 }, new[] { 2, 1, 3, 0 },
            new[] { 2, 3, 0, 1 }, new[] { 2, 3, 1, 0 }, new[] { 3, 0, 1, 2 }, new[] { 3, 0, 2, 1 },
            new[] { 3, 1, 0, 2 }, new[] { 3, 1, 2, 0 }, new[] { 3, 2, 0, 1 }, new[] { 3, 2, 1, 0 },
        };

        public PokemonData? Parse(byte[] raw)
        {
            PokemonData? data = Decode(raw);
            if (data is null || data.IsCorrupt) return data;

            // Enrichment, not decoding: everything above comes out of the bytes, while these need the
            // species tables. Kept separate so the structure decoding stays a pure function.
            data.Gender = metadataService.GetGender(data.PID, data.Species);
            data.Name = metadataService.TryGetPokemonMetadata(data.Species)?.PokemonName ?? string.Empty;
            return data;
        }

        /// <summary>
        /// Decodes the structure with no outside dependencies, so the crypto can be exercised on its
        /// own. Returns null when the block is too short, and a record with IsCorrupt set when it
        /// decodes but fails validation.
        /// </summary>
        public static PokemonData? Decode(byte[] raw)
        {
            if (raw is null || raw.Length < BoxBlockSize)
            {
                return null;
            }

            uint pid = BitConverter.ToUInt32(raw, PersonalityOffset);
            uint otId = BitConverter.ToUInt32(raw, OtIdOffset);
            ushort storedChecksum = BitConverter.ToUInt16(raw, ChecksumOffset);

            byte[] decrypted = Decrypt(raw, pid, otId);

            ushort computed = 0;
            for (int i = 0; i < SubstructTotal; i += 2)
            {
                computed += BitConverter.ToUInt16(decrypted, i);
            }
            if (computed != storedChecksum)
            {
                return new PokemonData { IsCorrupt = true };
            }

            // Growth block, first after reordering.
            ushort internalSpecies = BitConverter.ToUInt16(decrypted, 0);
            if (internalSpecies == 0 || internalSpecies > MaxInternalSpecies)
            {
                return new PokemonData { IsCorrupt = true };
            }
            ushort species = ToNationalDex(internalSpecies);
            if (species == 0)
            {
                // An index inside the unused block between Celebi and Treecko: structurally valid,
                // but not a pokemon that exists.
                return new PokemonData { IsCorrupt = true };
            }

            // Miscellaneous block holds the IV word, whose bit layout matches Gen 4 exactly.
            uint ivs = BitConverter.ToUInt32(decrypted, (SubstructSize * 3) + 4);

            PokemonData data = new()
            {
                RawBytes = raw,
                PID = pid,
                Checksum = storedChecksum,
                Species = species,
                OTId = otId,
                TID = (ushort)(otId & 0xFFFF),
                SID = (ushort)(otId >> 16),
                HeldItem = BitConverter.ToUInt16(decrypted, 2),
                IVs = ivs,
                Nature = (byte)(pid % 25),
                Nickname = ReadString(raw, NicknameOffset, NicknameLength),
                OTName = ReadString(raw, OtNameOffset, OtNameLength),

                // Attacks block.
                Move1 = BitConverter.ToUInt16(decrypted, SubstructSize + 0),
                Move2 = BitConverter.ToUInt16(decrypted, SubstructSize + 2),
                Move3 = BitConverter.ToUInt16(decrypted, SubstructSize + 4),
                Move4 = BitConverter.ToUInt16(decrypted, SubstructSize + 6),

                // EVs block.
                EVHP = decrypted[(SubstructSize * 2) + 0],
                EVAtk = decrypted[(SubstructSize * 2) + 1],
                EVDef = decrypted[(SubstructSize * 2) + 2],
                EVSpeed = decrypted[(SubstructSize * 2) + 3],
                EVSpAtk = decrypted[(SubstructSize * 2) + 4],
                EVSpDef = decrypted[(SubstructSize * 2) + 5],

                // Gen 3 stores which of the species' two ability slots is used, not an ability id.
                Ability = (byte)((ivs >> 31) & 1),
            };

            if (raw.Length >= PartyBlockSize)
            {
                data.Level = raw[LevelOffset];
                data.HP = BitConverter.ToUInt16(raw, HpOffset);
                data.MaxHP = BitConverter.ToUInt16(raw, MaxHpOffset);
                data.Attack = BitConverter.ToUInt16(raw, AttackOffset);
                data.Defense = BitConverter.ToUInt16(raw, DefenseOffset);
                data.Speed = BitConverter.ToUInt16(raw, SpeedOffset);
                data.SpAttack = BitConverter.ToUInt16(raw, SpAttackOffset);
                data.SpDefense = BitConverter.ToUInt16(raw, SpDefenseOffset);
            }

            return data;
        }

        /// <summary>
        /// Converts a Gen 3 internal species index to a National Dex number.
        ///
        /// Kanto and Johto keep their dex numbers. Indices 252-276 are placeholders occupied only by
        /// sprites. The 135 Hoenn species then follow, and would be a flat -25 shift except that
        /// Chimecho was added last and sits at the very end of the index space, after Deoxys --
        /// pushing everything from Absol onward down by one relative to a flat shift.
        /// </summary>
        /// <returns>0 when the index falls in the unused block and is not a real species.</returns>
        public static ushort ToNationalDex(ushort internalIndex)
        {
            const ushort LastJohto = 251;
            const ushort FirstHoennIndex = 277;
            const ushort ChimechoIndex = 411;
            const ushort ChimechoNational = 358;

            if (internalIndex <= LastJohto) return internalIndex;
            if (internalIndex < FirstHoennIndex) return 0;
            if (internalIndex == ChimechoIndex) return ChimechoNational;

            // Treecko (277) through Duskull (382) map to 252-357.
            ushort national = (ushort)(internalIndex - 25);
            // Absol onward sit one slot earlier than a flat shift, because Chimecho is not among them.
            if (national >= ChimechoNational) national++;
            return national;
        }

        private static byte[] Decrypt(byte[] raw, uint pid, uint otId)
        {
            uint key = pid ^ otId;
            byte[] plain = new byte[SubstructTotal];
            for (int i = 0; i < SubstructTotal; i += 4)
            {
                uint word = BitConverter.ToUInt32(raw, SubstructOffset + i) ^ key;
                BitConverter.GetBytes(word).CopyTo(plain, i);
            }

            int[] order = SubstructOrder[pid % 24];
            byte[] ordered = new byte[SubstructTotal];
            for (int logical = 0; logical < 4; logical++)
            {
                Buffer.BlockCopy(plain, order[logical] * SubstructSize, ordered, logical * SubstructSize, SubstructSize);
            }
            return ordered;
        }

        /// <summary>
        /// Gen 3 text: one byte per character, terminated by 0xFF. Unmapped bytes become '?' so a
        /// name with an unusual character is still readable rather than throwing.
        /// </summary>
        public static string ReadString(byte[] data, int offset, int length)
        {
            System.Text.StringBuilder sb = new();
            for (int i = 0; i < length && offset + i < data.Length; i++)
            {
                byte b = data[offset + i];
                if (b == 0xFF) break;
                sb.Append(CharMap.TryGetValue(b, out char c) ? c : '?');
            }
            return sb.ToString();
        }

        /// <summary>
        /// The printable subset of the Gen 3 English character map. Accented and Japanese ranges are
        /// deliberately absent: nothing the bot names or reads uses them, and mapping them wrongly
        /// would be worse than showing '?'.
        /// </summary>
        private static readonly Dictionary<byte, char> CharMap = BuildCharMap();

        private static Dictionary<byte, char> BuildCharMap()
        {
            Dictionary<byte, char> map = new() { [0x00] = ' ' };
            for (byte i = 0; i < 10; i++) map[(byte)(0xA1 + i)] = (char)('0' + i);
            map[0xAB] = '!';
            map[0xAC] = '?';
            map[0xAD] = '.';
            map[0xAE] = '-';
            map[0xB8] = ',';
            map[0xBA] = '/';
            for (byte i = 0; i < 26; i++) map[(byte)(0xBB + i)] = (char)('A' + i);
            for (byte i = 0; i < 26; i++) map[(byte)(0xD5 + i)] = (char)('a' + i);
            return map;
        }
    }
}
