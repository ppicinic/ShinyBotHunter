using System;

namespace ApiHawk
{
    /// <summary>
    /// The Gen 3 counterpart to <see cref="PokemonValidator"/>: decides whether a raw party-slot
    /// block is worth sending to the bot, so a torn read is retried on the next frame rather than
    /// travelling over HTTP to be second-guessed there.
    ///
    /// Gen 3 stores a pokemon quite differently from Gen 4. There is no LCRNG keystream: the four
    /// twelve-byte substructures are XORed with PID ^ OTID repeated, and their order is a permutation
    /// picked by PID % 24 rather than by bits of the PID. The checksum covers only those 48 bytes.
    ///
    /// As with Gen 4, the party-only fields past the substructures are protected by nothing, so a
    /// read landing mid-write decrypts into something that passes the checksum and still describes an
    /// impossible pokemon. Those get an independent sanity pass.
    ///
    /// IMPORTANT: this crypto duplicates ShinyBotHunter's Gen 3 parser. They cannot share code --
    /// this assembly targets net48 against BizHawk -- so a change to one has to be mirrored in the
    /// other. Only validation lives here; interpreting a pokemon is the parser's job alone.
    /// </summary>
    internal static class Gen3PokemonValidator
    {
        private const int PersonalityOffset = 0x00;
        private const int OtIdOffset = 0x04;
        private const int ChecksumOffset = 0x1C;
        private const int SubstructOffset = 0x20;
        private const int SubstructSize = 12;
        private const int SubstructTotal = SubstructSize * 4;

        // Party-only fields, which the checksum does not cover.
        private const int StatusOffset = 0x50;
        private const int LevelOffset = 0x54;
        private const int HpOffset = 0x56;
        private const int MaxHpOffset = 0x58;

        // A box-sized block stops at the substructures; a party block carries the rest.
        private const int BoxBlockSize = 0x50;
        private const int PartyBlockSize = 0x64;

        // Internal species indices, which are not national dex numbers -- the Hoenn additions sit
        // above the 251 Johto ones rather than at 252+. 411 is the last valid index.
        private const int MaxSpecies = 411;

        private const int MaxPossibleHp = 999;

        /// <summary>
        /// Substructure order by personality % 24. Index i of a row gives the stored slot that the
        /// i-th logical block (Growth, Attacks, EVs, Miscellaneous) occupies.
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

        public static bool IsUsable(byte[] raw, out string reason)
        {
            if (raw == null || raw.Length < BoxBlockSize)
            {
                reason = "block is " + (raw == null ? 0 : raw.Length) + " bytes, need at least " + BoxBlockSize;
                return false;
            }

            uint personality = BitConverter.ToUInt32(raw, PersonalityOffset);
            uint otId = BitConverter.ToUInt32(raw, OtIdOffset);
            ushort storedChecksum = BitConverter.ToUInt16(raw, ChecksumOffset);

            // An all-zero slot is an empty one rather than a torn read, but it is not a pokemon either.
            if (personality == 0 && otId == 0)
            {
                reason = "slot is empty";
                return false;
            }

            byte[] decrypted = Decrypt(raw, personality, otId);

            ushort computed = 0;
            for (int i = 0; i < SubstructTotal; i += 2)
            {
                computed += BitConverter.ToUInt16(decrypted, i);
            }
            if (computed != storedChecksum)
            {
                reason = string.Format("checksum {0:X4} != stored {1:X4}", computed, storedChecksum);
                return false;
            }

            // Growth is the first logical substructure, and species is its first field.
            ushort species = BitConverter.ToUInt16(decrypted, 0);
            if (species == 0 || species > MaxSpecies)
            {
                reason = "species " + species + " outside 1-" + MaxSpecies;
                return false;
            }

            // Box-sized blocks stop here: everything below is party-only.
            if (raw.Length < PartyBlockSize)
            {
                reason = string.Empty;
                return true;
            }

            byte level = raw[LevelOffset];
            if (level < 1 || level > 100)
            {
                reason = "level " + level + " outside 1-100";
                return false;
            }

            ushort hp = BitConverter.ToUInt16(raw, HpOffset);
            ushort maxHp = BitConverter.ToUInt16(raw, MaxHpOffset);
            if (maxHp < 1 || maxHp > MaxPossibleHp)
            {
                reason = "max HP " + maxHp + " outside 1-" + MaxPossibleHp;
                return false;
            }
            if (hp > maxHp)
            {
                reason = "HP " + hp + " exceeds max " + maxHp;
                return false;
            }

            uint status = BitConverter.ToUInt32(raw, StatusOffset);
            if (!IsValidStatus(status))
            {
                reason = string.Format("status 0x{0:X8} holds more than one condition", status);
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// Undoes the XOR and returns the substructures in logical order (Growth, Attacks, EVs,
        /// Miscellaneous) rather than the scrambled order they are stored in.
        /// </summary>
        private static byte[] Decrypt(byte[] raw, uint personality, uint otId)
        {
            uint key = personality ^ otId;
            byte[] plain = new byte[SubstructTotal];
            for (int i = 0; i < SubstructTotal; i += 4)
            {
                uint word = BitConverter.ToUInt32(raw, SubstructOffset + i) ^ key;
                BitConverter.GetBytes(word).CopyTo(plain, i);
            }

            int[] order = SubstructOrder[personality % 24];
            byte[] ordered = new byte[SubstructTotal];
            for (int logical = 0; logical < 4; logical++)
            {
                int stored = order[logical];
                Buffer.BlockCopy(plain, stored * SubstructSize, ordered, logical * SubstructSize, SubstructSize);
            }
            return ordered;
        }

        /// <summary>
        /// Sleep is a turn counter in the low bits; the rest are single flags. A sleep counter and a
        /// flag at once, or two flags at once, cannot happen in game and means the read caught a
        /// rewrite in progress.
        /// </summary>
        private static bool IsValidStatus(uint status)
        {
            if (status == 0) return true;

            uint sleepTurns = status & 0x7;
            uint others = status & ~0x7u;

            if (sleepTurns != 0 && others != 0) return false;

            // Poison, burn, freeze, paralysis, bad poison.
            const uint KnownFlags = 0x08 | 0x10 | 0x20 | 0x40 | 0x80;
            if ((others & ~KnownFlags) != 0) return false;

            int flagCount = 0;
            for (uint bit = 0x08; bit <= 0x80; bit <<= 1)
            {
                if ((others & bit) != 0) flagCount++;
            }
            return flagCount <= 1;
        }
    }
}
