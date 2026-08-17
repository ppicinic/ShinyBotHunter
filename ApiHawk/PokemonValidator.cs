using System;

namespace ApiHawk
{
    /// <summary>
    /// Decides whether a raw party-slot block is worth sending to the bot, so a bad read is retried
    /// on the next frame instead of travelling over HTTP and being second-guessed there.
    ///
    /// Two independent things are checked:
    ///
    ///   1. The stored data (0x08-0x87) via its checksum, plus a species range check. This mirrors
    ///      what HgssPokemonParser reports as IsCorrupt.
    ///   2. The battle stats (0x88-0xDB) for internal consistency. Nothing in the structure protects
    ///      that region -- the checksum stops at 0x88 -- so a read landing while the game rewrites it
    ///      decrypts into garbage that passes every checksum test. HP above max, a level outside
    ///      1-100, or a status byte holding a sleep counter and poison at once all mean exactly that.
    ///
    /// IMPORTANT: the crypto here duplicates ShinyBotHunter's HgssPokemonParser (Decrypt,
    /// VerifyChecksum, and the battle-stat field offsets). They cannot share code -- this assembly
    /// targets net48 against BizHawk -- so a change to the parser's crypto has to be mirrored here.
    /// Only validation lives here; interpreting a pokemon is still the parser's job alone.
    /// </summary>
    internal static class PokemonValidator
    {
        // Offsets within the decrypted battle stats block.
        private const int StatusOffset = 0x88;
        private const int LevelOffset = 0x8C;
        private const int HpOffset = 0x8E;
        private const int MaxHpOffset = 0x90;

        // A party block has to carry the battle stats for them to be worth checking. The parser uses
        // the same bound before it reads any of those fields.
        private const int BattleStatsMinLength = 141;

        private const int MaxSpecies = 493;

        // Blissey at level 100 tops out at 714, so anything past this is a torn read.
        private const int MaxPossibleHp = 999;

        /// <summary>
        /// Where each of the four 0x20 stored blocks sits, selected by bits 13-17 of the PID. Copied
        /// from HgssPokemonParser.Decrypt; only the first entry of the chosen row is used here, to
        /// find the block that holds species.
        /// </summary>
        private static readonly int[][] Substruct =
        {
            new[]{1,2,3,4}, new[]{1,2,4,3}, new[]{1,3,2,4}, new[]{1,4,2,3},
            new[]{1,3,4,2}, new[]{1,4,3,2}, new[]{2,1,3,4}, new[]{2,1,4,3},
            new[]{3,1,2,4}, new[]{4,1,2,3}, new[]{3,1,4,2}, new[]{4,1,3,2},
            new[]{2,3,1,4}, new[]{2,4,1,3}, new[]{3,2,1,4}, new[]{4,2,1,3},
            new[]{3,4,1,2}, new[]{4,3,1,2}, new[]{2,3,4,1}, new[]{2,4,3,1},
            new[]{3,2,4,1}, new[]{4,2,3,1}, new[]{3,4,2,1}, new[]{4,3,2,1},
        };

        private static uint Lcrng(uint seed) => seed * 0x41C64E6D + 0x6073;

        /// <summary>
        /// True when the block decrypts cleanly and its live stats are self-consistent.
        /// <paramref name="reason"/> names the first failure, for the server log.
        /// </summary>
        public static bool IsUsable(byte[] raw, out string reason)
        {
            if (raw == null || raw.Length < 0x88)
            {
                reason = "block too short";
                return false;
            }

            uint pid = ReadU32(raw, 0x00);
            ushort checksum = ReadU16(raw, 0x06);

            // Decrypt the four stored blocks in sequence; the seed carries forward across them.
            uint seed = checksum;
            var blocks = new byte[4][];
            for (int index = 0; index < 4; index++)
            {
                int start = 0x08 + index * 0x20;
                blocks[index] = DecryptRange(raw, start, start + 0x1F, ref seed);
            }

            // The checksum is a sum over all four blocks, and addition doesn't care what order they
            // were stored in -- so this needs no unshuffle, unlike the parser which also has to read
            // the other fields.
            ushort sum = 0;
            for (int index = 0; index < 4; index++)
            {
                for (int i = 0; i < blocks[index].Length; i += 2)
                {
                    sum += ReadU16(blocks[index], i);
                }
            }
            if (sum != checksum)
            {
                reason = "checksum mismatch";
                return false;
            }

            // Species is the first field of block A, which is whichever stored block the PID's
            // shuffle put first.
            int shift = (int)(((pid & 0x3E000) >> 13) % 24);
            byte[] blockA = blocks[Substruct[shift][0] - 1];
            ushort species = ReadU16(blockA, 0);
            if (species == 0 || species > MaxSpecies)
            {
                reason = "species " + species + " out of range";
                return false;
            }

            if (raw.Length < BattleStatsMinLength)
            {
                // A box-sized block has no battle stats to check, and the parser zeroes those fields
                // rather than reading them.
                reason = string.Empty;
                return true;
            }

            // Battle stats use the PID as the seed and are not shuffled.
            seed = pid;
            byte[] battle = DecryptRange(raw, 0x88, 0xDB, ref seed);
            byte status = battle[StatusOffset - 0x88];
            byte level = battle[LevelOffset - 0x88];
            ushort hp = ReadU16(battle, HpOffset - 0x88);
            ushort maxHp = ReadU16(battle, MaxHpOffset - 0x88);

            if (level < 1 || level > 100)
            {
                reason = "level " + level;
                return false;
            }
            if (maxHp < 1 || maxHp > MaxPossibleHp)
            {
                reason = "maxHp " + maxHp;
                return false;
            }
            if (hp > maxHp)
            {
                reason = "hp " + hp + " > maxHp " + maxHp;
                return false;
            }
            if (!IsValidStatus(status))
            {
                reason = "status 0x" + status.ToString("X2");
                return false;
            }

            reason = string.Empty;
            return true;
        }

        // Only one major status at a time: the byte is either clear, a sleep counter (bits 0-2), or
        // exactly one of the single-status bits.
        private static bool IsValidStatus(byte status)
        {
            if (status == 0) return true;
            if ((status & 0x07) != 0) return (status & 0xF8) == 0;
            return status == 0x08 || status == 0x10 || status == 0x20 || status == 0x40 || status == 0x80;
        }

        private static byte[] DecryptRange(byte[] raw, int start, int end, ref uint seed)
        {
            var data = new byte[end - start + 1];
            int at = 0;
            for (int i = start; i <= end; i += 2)
            {
                seed = Lcrng(seed);
                ushort keystream = (ushort)(seed >> 16);
                ushort word = (ushort)(ReadU16(raw, i) ^ keystream);
                data[at++] = (byte)(word & 0xFF);
                data[at++] = (byte)(word >> 8);
            }
            return data;
        }

        private static ushort ReadU16(byte[] buf, int offset)
        {
            return (ushort)(buf[offset] | (buf[offset + 1] << 8));
        }

        private static uint ReadU32(byte[] buf, int offset)
        {
            return (uint)(buf[offset]
                | (buf[offset + 1] << 8)
                | (buf[offset + 2] << 16)
                | (buf[offset + 3] << 24));
        }
    }
}
