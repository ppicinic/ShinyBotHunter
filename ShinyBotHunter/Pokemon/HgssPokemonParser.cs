using Microsoft.Extensions.Primitives;
using ShinyBotHunter.Service;

namespace ShinyBotHunter.Pokemon
{
    public class HgssPokemonParser
    {
        private PokemonMetadataService _metadataService;
        public HgssPokemonParser(PokemonMetadataService service)
        {
            this._metadataService = service;
        }
        private uint LCRNG(uint seed) => seed * 0x41C64E6D + 0x6073;
        Dictionary<int, byte[]> cache = new Dictionary<int, byte[]>();

        public PokemonData? Parse(byte[] raw)
        {
            byte[]? data = Decrypt(raw);
            if (data == null)
            {
                return new PokemonData() { IsCorrupt = true };
            }

            uint pid = BitConverter.ToUInt32(data, 0x00);
            ushort checksum = BitConverter.ToUInt16(data, 0x06);
            uint otId = BitConverter.ToUInt32(data, 0x0C);
            ushort tid = (ushort)(otId & 0xFFFF);
            ushort sid = (ushort)(otId >> 16);

            // Block A (0x08)
            ushort species = BitConverter.ToUInt16(data, 0x08);
            if(species <= 0 || species > 493)
            {
                return new PokemonData() { IsCorrupt = true };
            }
            ushort heldItem = BitConverter.ToUInt16(data, 0x0A);
            byte ability = data[0x15];

            // Block B (0x28)
            ushort move1 = BitConverter.ToUInt16(data, 0x28);
            ushort move2 = BitConverter.ToUInt16(data, 0x2A);
            ushort move3 = BitConverter.ToUInt16(data, 0x2C);
            ushort move4 = BitConverter.ToUInt16(data, 0x2E);
            uint ivs = BitConverter.ToUInt32(data, 0x38);
            int formIndex = data[0x40] >> 3;


            // Block C (0x48)
            string nickname = ReadString(data, 0x48);

            // Block D (0x68)
            string otName = ReadString(data, 0x68);

            byte gender = _metadataService.GetGender(pid, species);
            // Battle stats (0x88)
            byte level = data.Length > 136 ? data[0x8C] : (byte)0;
            ushort hp = data.Length > 136 ? BitConverter.ToUInt16(data, 0x8E) : (ushort)0;
            ushort maxHp = data.Length > 136 ? BitConverter.ToUInt16(data, 0x90) : (ushort)0;
            ushort atk = data.Length > 136 ? BitConverter.ToUInt16(data, 0x92) : (ushort)0;
            ushort def = data.Length > 136 ? BitConverter.ToUInt16(data, 0x94) : (ushort)0;
            ushort spe = data.Length > 136 ? BitConverter.ToUInt16(data, 0x96) : (ushort)0;
            ushort spatk = data.Length > 136 ? BitConverter.ToUInt16(data, 0x98) : (ushort)0;
            ushort spdef = data.Length > 136 ? BitConverter.ToUInt16(data, 0x9A) : (ushort)0;
            return new PokemonData
            {
                PID = pid,
                Checksum = checksum,
                Species = species,
                Name = _metadataService.GetPokemonMetadata(species).PokemonName,
                FormIndex = formIndex,
                Gender = gender,
                FormCode = _metadataService.GetFormCode(species, gender, formIndex),
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
            int[][] Substruct = {
                [1,2,3,4], [1,2,4,3], [1,3,2,4], [1,4,2,3],
                [1,3,4,2], [1,4,3,2], [2,1,3,4], [2,1,4,3],
                [3,1,2,4], [4,1,2,3], [3,1,4,2], [4,1,3,2],
                [2,3,1,4], [2,4,1,3], [3,2,1,4], [4,2,1,3],
                [3,4,1,2], [4,3,1,2], [2,3,4,1], [2,4,3,1],
                [3,2,4,1], [4,2,3,1], [3,4,2,1], [4,3,2,1],
            };

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
            if (raw.Length > 140)
            {
                result.AddRange(DecryptBlock(raw, 0x88, 0xDB, ref seed));

                // Gen 4: 16 blank ball seal bytes
                for (int i = 0; i < 0x10; i++)
                    result.Add(0x0);
            }
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

        private string ReadString(byte[] data, int offset)
        {
            // Define the table exactly as it appears in your Lua code
            string[] charTable = {
        " ", "ぁ", "あ", "ぃ", "い", "ぅ", "う", "ぇ", "え", "ぉ", "お", "か", "が", "き", "ぎ", "く", "ぐ", "け", "げ", "こ", "ご", "さ", "ざ", "し", "じ", "す", "ず", "せ", "ぜ", "そ", "ぞ", "た", "だ", "ち", "ぢ", "っ", "つ", "づ", "て", "で", "と", "ど", "な", "に", "ぬ", "ね", "の", "は", "ば", "ぱ", "ひ", "び", "ぴ", "ふ", "ぶ", "ぷ", "へ", "べ", "ぺ", "ほ", "ぼ", "ぽ", "ま", "み", "む", "め", "も", "ゃ", "や", "ゅ", "ゆ", "ょ", "よ", "ら", "り", "る", "れ", "ろ", "わ", "を", "ん", "ァ", "ア", "ィ", "イ", "ゥ", "ウ", "ェ", "エ", "ォ", "オ", "カ", "ガ", "キ", "ギ", "ク", "グ", "ケ", "ゲ", "コ", "ゴ", "サ", "ザ", "シ", "ジ", "ス", "ズ", "セ", "ぜ", "ソ", "ゾ", "タ", "ダ", "チ", "ヂ", "ッ", "ツ", "ヅ", "テ", "デ", "ト", "ド", "ナ", "ニ", "ヌ", "ネ", "ノ", "ハ", "バ", "パ", "ヒ", "ビ", "ピ", "フ", "ブ", "プ", "ヘ", "ベ", "ぺ", "ホ", "ボ", "ポ", "ま", "ミ", "ム", "メ", "モ", "ャ", "ヤ", "ゅ", "ゆ", "ょ", "よ", "ら", "り", "る", "れ", "ろ", "わ", "を", "ん", "０", "１", "２", "３", "４", "５", "６", "７", "８", "９", "Ａ", "Ｂ", "Ｃ", "Ｄ", "Ｅ", "Ｆ", "Ｇ", "Ｈ", "Ｉ", "Ｊ", "Ｋ", "Ｌ", "Ｍ", "Ｎ", "Ｏ", "Ｐ", "Ｑ", "Ｒ", "Ｓ", "Ｔ", "Ｕ", "Ｖ", "Ｗ", "Ｘ", "Ｙ", "Ｚ", "ａ", "ｂ", "ｃ", "ｄ", "ｅ", "ｆ", "ｇ", "ｈ", "ｉ", "ｊ", "ｋ", "ｌ", "ｍ", "ｎ", "ｏ", "ｐ", "ｑ", "ｒ", "ｓ", "ｔ", "ｕ", "ｖ", "ｗ", "ｘ", "ｙ", "ｚ", "", "！", "？", "、", "。", "…", "・", "／", "「", "」", "『", "』", "（", "）", "♂", "♀", "＋", "ー", "×", "÷", "＝", "～", "：", "；", "．", "，", "♠", "♣", "♥", "♦", "★", "◎", "○", "□", "△", "◇", "＠", "♪", "％", "☀", "☁", "☂", "☃", "😑", "☺", "☹", "😠", "⤴", "⤵", "💤", "円", "💰", "🗝", "💿", "✉", "💊", "🍓", "◓", "💥", "←", "↑", "↓", "→", "►", "＆", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z", "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z", "À", "Á", "Â", "Ã", "Ä", "Å", "Æ", "Ç", "È", "É", "Ê", "Ë", "Ì", "Í", "Î", "Ï", "Ð", "Ñ", "Ò", "Ó", "Ô", "Õ", "Ö", "×", "Ø", "Ù", "Ú", "Û", "Ü", "Ý", "Þ", "ß", "à", "á", "â", "ã", "ä", "å", "æ", "ç", "è", "é", "ê", "ë", "ì", "í", "î", "ï", "ð", "ñ", "ò", "ó", "ô", "õ", "ö", "÷", "ø", "ù", "ú", "û", "ü", "ý", "þ", "ÿ", "Œ", "œ", "Ş", "ş", "ª", "º", "er", "re", "r", "₽", "¡", "¿", "!", "?", ",", ".", "…", "･", "/", "‘", "’", "“", "”", "„", "«", "»", "(", ")", "♂", "♀", "+", "-", "*", "#", "=", "&", "~", ":", ";", "♠", "♣", "♥", "♦", "★", "◎", "○", "□", "△", "◇", "@", "♪", "%", "☀", "☁", "☂", "☃", "😑", "☺", "☹", "😠", "⤴", "⤵", "💤", " ", "e", "PK", "MN", " ", " ", " ", "", " ", " ", "°", "_", "＿", "․", "‥"
    };

            var sb = new System.Text.StringBuilder();

            // Loop until end of array or null terminator
            for (int i = offset; i + 1 < data.Length; i += 2)
            {
                // Equivalent to Lua: input[i] + bit.lshift(input[i+1], 8)
                ushort val = (ushort)(data[i] | (data[i + 1] << 8));

                // Null termination check
                if (val == 0x0000 || val == 0xFFFF)
                    break;

                int tableIndex = val - 1;

                if (tableIndex >= 0 && tableIndex < charTable.Length)
                {
                    sb.Append(charTable[tableIndex]);
                }
                else
                {
                    //Console.Write($"{val}:?, ");
                    sb.Append("?");
                }
            }

            string result = sb.ToString();
            //Console.WriteLine();
            //Console.WriteLine(result);
            return result;
        }
    }
}
