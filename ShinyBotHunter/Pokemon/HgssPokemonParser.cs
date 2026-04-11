namespace ShinyBotHunter.Pokemon
{
    public class HgssPokemonParser
    {
        private uint LCRNG(uint seed) => seed * 0x41C64E6D + 0x6073;

        private readonly int[][] Substruct = {
            [1,2,3,4], [1,2,4,3], [1,3,2,4], [1,4,2,3],
            [1,3,4,2], [1,4,3,2], [2,1,3,4], [2,1,4,3],
            [3,1,2,4], [4,1,2,3], [3,1,4,2], [4,1,3,2],
            [2,3,1,4], [2,4,1,3], [3,2,1,4], [4,2,1,3],
            [3,4,1,2], [4,3,1,2], [2,3,4,1], [2,4,3,1],
            [3,2,4,1], [4,2,3,1], [3,4,2,1], [4,3,2,1],
        };

        public PokemonData? Parse(byte[] raw)
        {
            byte[]? data = Decrypt(raw);
            if (data == null) return null;

            uint pid = BitConverter.ToUInt32(data, 0x00);
            ushort checksum = BitConverter.ToUInt16(data, 0x06);
            uint otId = BitConverter.ToUInt32(data, 0x0C);
            ushort tid = (ushort)(otId & 0xFFFF);
            ushort sid = (ushort)(otId >> 16);

            // Block A (0x08)
            ushort species = BitConverter.ToUInt16(data, 0x08);
            ushort heldItem = BitConverter.ToUInt16(data, 0x0A);
            byte ability = data[0x15];

            // Block B (0x28)
            ushort move1 = BitConverter.ToUInt16(data, 0x28);
            ushort move2 = BitConverter.ToUInt16(data, 0x2A);
            ushort move3 = BitConverter.ToUInt16(data, 0x2C);
            ushort move4 = BitConverter.ToUInt16(data, 0x2E);
            uint ivs = BitConverter.ToUInt32(data, 0x38);

            // Block C (0x48)
            string nickname = ReadString(data, 0x48, 11);

            // Block D (0x68)
            string otName = ReadString(data, 0x68, 8);

            // Battle stats (0x88)
            byte level = data[0x8C];
            ushort hp = BitConverter.ToUInt16(data, 0x8E);
            ushort maxHp = BitConverter.ToUInt16(data, 0x90);
            ushort atk = BitConverter.ToUInt16(data, 0x92);
            ushort def = BitConverter.ToUInt16(data, 0x94);
            ushort spe = BitConverter.ToUInt16(data, 0x96);
            ushort spatk = BitConverter.ToUInt16(data, 0x98);
            ushort spdef = BitConverter.ToUInt16(data, 0x9A);

            return new PokemonData
            {
                PID = pid,
                Checksum = checksum,
                Species = species,
                HeldItem = heldItem,
                OTId = otId,
                TID = tid,
                SID = sid,
                Ability = ability,
                Move1 = move1,
                Move2 = move2,
                Move3 = move3,
                Move4 = move4,
                IVs = ivs,
                Nickname = nickname,
                OTName = otName,
                Nature = (byte)(pid % 25),
                AbilitySlot = (byte)(pid & 1),
                Level = level,
                HP = hp,
                MaxHP = maxHp,
                Attack = atk,
                Defense = def,
                Speed = spe,
                SpAttack = spatk,
                SpDefense = spdef,
            };
        }

        private byte[]? Decrypt(byte[] raw)
        {
            uint pid = BitConverter.ToUInt32(raw, 0);
            ushort checksum = BitConverter.ToUInt16(raw, 6);

            var result = new List<byte>();

            // Unencrypted header: PID + unused + checksum
            result.AddRange(raw[0..4]);
            result.Add(0x0);
            result.Add(0x0);
            result.Add(raw[6]);
            result.Add(raw[7]);

            // Block order: (pid & 0x3E000) >> 13 % 24
            int shift = (int)(((pid & 0x3E000) >> 13) % 24);
            int[] blockOrder = Substruct[shift];

            // Decrypt all 4 blocks in sequence (seed carries forward)
            uint seed = checksum;
            var blocks = new List<byte>[4];

            for (int index = 0; index < 4; index++)
            {
                int start = 0x08 + index * 0x20;
                int end = 0x27 + index * 0x20;
                blocks[index] = DecryptBlock(raw, start, end, ref seed);
            }

            // Rearrange blocks according to block order (1-indexed)
            foreach (int index in blockOrder)
                result.AddRange(blocks[index - 1]);

            // Verify checksum before continuing
            if (!VerifyChecksum(result.ToArray(), checksum))
                return null;

            // Battle stats (0x88–0xDB) encrypted with PID as seed
            seed = pid;
            result.AddRange(DecryptBlock(raw, 0x88, 0xDB, ref seed));

            // Gen 4: 16 blank ball seal bytes
            for (int i = 0; i < 0x10; i++)
                result.Add(0x0);

            return result.ToArray();
        }

        private List<byte> DecryptBlock(byte[] raw, int start, int end, ref uint seed)
        {
            var data = new List<byte>();

            for (int i = start; i <= end; i += 2)
            {
                seed = LCRNG(seed);
                ushort keystream = (ushort)(seed >> 16);
                ushort word = BitConverter.ToUInt16(raw, i);
                word ^= keystream;

                data.Add((byte)(word & 0xFF));
                data.Add((byte)(word >> 8));
            }

            return data;
        }

        private bool VerifyChecksum(byte[] data, ushort checksum)
        {
            ushort sum = 0;
            for (int i = 8; i < 0x88; i += 2)
                sum += BitConverter.ToUInt16(data, i);
            return sum == checksum;
        }

        private string ReadString(byte[] data, int offset, int maxChars)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < maxChars; i++)
            {
                ushort ch = BitConverter.ToUInt16(data, offset + i * 2);
                if (ch == 0xFFFF) break;
                sb.Append((char)ch);
            }
            return sb.ToString();
        }
    }
}
