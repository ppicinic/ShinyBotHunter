using ShinyBotHunter.Games.Hgss;

namespace ShinyBotHunter.Tests
{
    public class PrimoPasswordTests
    {
        [Fact]
        public void EncodeGivesThePasswordTheGameAcceptedForTrainer60647()
        {
            ushort[] words = PrimoPassword.Encode(60647, 10);

            // KLUTZ, TEACHER, DARK, STEADFAST
            Assert.Equal(new ushort[] { 1085, 1337, 981, 1062 }, words);
        }

        [Fact]
        public void EggDecodeReturnsTheEggAndWallpaperDecodeFailsForRandomTrainerIds()
        {
            Random random = new Random(241);

            for (int n = 0; n < 3000; n++)
            {
                ushort trainerId = (ushort)random.Next(0x10000);
                for (int egg = 8; egg <= 10; egg++)
                {
                    ushort[] words = PrimoPassword.Encode(trainerId, egg);

                    Assert.Equal(egg, DecodeEgg(trainerId, words));
                    Assert.Equal(-1, DecodeWallpaper(trainerId, words));
                }
            }
        }

        [Fact]
        public void DecodeEggAcceptsTheGamesPasswordForTrainer60647()
        {
            // KLUTZ, TEACHER, DARK, STEADFAST
            int egg = DecodeEgg(60647, new ushort[] { 1085, 1337, 981, 1062 });

            Assert.Equal(10, egg);
        }

        [Fact]
        public void BankHas351Words()
        {
            Assert.Equal(351, PrimoPassword.Bank.Length);
            Assert.Equal(351, PrimoPassword.Bank.Distinct().Count());
        }

        // Port of ov02_0224CBF8 (asm/overlay_02_02248728.s in pret/pokeheartgold): the egg check.
        // Returns the egg (8 to 10) or -1.
        private static int DecodeEgg(ushort trainerId, ushort[] words)
        {
            int count = PrimoPassword.Bank.Length;
            int[] indexes = words.Select(word => Array.IndexOf(PrimoPassword.Bank, word)).ToArray();
            byte[] bytes = new byte[4];
            for (int k = 0; k < 4; k++)
            {
                int cur = indexes[k];
                if (cur < 0)
                {
                    return -1;
                }
                if (k > 0)
                {
                    int prev = indexes[k - 1];
                    int diff = cur >= prev ? cur - prev : count - (prev - cur);
                    if (diff > 0xFF)
                    {
                        return -1;
                    }
                    bytes[k] = (byte)diff;
                }
                else
                {
                    if (cur > 0xFF)
                    {
                        return -1;
                    }
                    bytes[0] = (byte)cur;
                }
            }

            RotateRight(bytes, 4, 5);
            for (int k = 0; k < 3; k++)
            {
                bytes[k] ^= (byte)((bytes[3] & 0xF0) | (bytes[3] >> 4));
            }
            RotateRight(bytes, 3, bytes[3] & 0xF);

            int type = bytes[0] & 0xF;
            if (type < 8 || type >= 11)
            {
                return -1;
            }
            bytes[1] ^= bytes[0];
            bytes[2] ^= bytes[0];
            if (trainerId != (bytes[1] << 8 | bytes[2]))
            {
                return -1;
            }
            if ((bytes[0] & 0xF0) >> 4 != 6)
            {
                return -1;
            }
            if (bytes[3] != (((bytes[0] + bytes[1]) * bytes[2]) & 0xFF))
            {
                return -1;
            }
            return type;
        }

        // Port of ov02_0224CAB8 (asm/overlay_02_02248728.s in pret/pokeheartgold): the wallpaper check.
        // Returns the wallpaper (0 to 7) or -1.
        private static int DecodeWallpaper(ushort trainerId, ushort[] words)
        {
            int count = PrimoPassword.Bank.Length;
            int[] indexes = words.Select(word => Array.IndexOf(PrimoPassword.Bank, word)).ToArray();
            byte[] bytes = new byte[4];
            for (int k = 0; k < 4; k++)
            {
                int cur = indexes[k];
                if (cur < 0)
                {
                    return -1;
                }
                if (k > 0)
                {
                    int prev = indexes[k - 1];
                    int diff = cur >= prev ? cur - prev : count - (prev - cur);
                    if (diff > 0xFF)
                    {
                        return -1;
                    }
                    bytes[k] = (byte)diff;
                }
                else
                {
                    if (cur > 0xFF)
                    {
                        return -1;
                    }
                    bytes[0] = (byte)cur;
                }
            }

            RotateRight(bytes, 4, 5);
            for (int k = 0; k < 3; k++)
            {
                bytes[k] ^= (byte)((bytes[3] & 0xF0) | (bytes[3] >> 4));
            }
            RotateRight(bytes, 3, bytes[3] & 0xF);

            int type = bytes[0] & 0xF;
            if (type >= 8)
            {
                return -1;
            }
            bytes[1] ^= bytes[0];
            bytes[2] ^= bytes[0];
            if (trainerId != (bytes[1] << 8 | bytes[2]))
            {
                return -1;
            }
            if ((bytes[0] & 0xF0) >> 4 != 6)
            {
                return -1;
            }
            if (bytes[3] != (((bytes[0] + bytes[1]) * bytes[2]) & 0xFF))
            {
                return -1;
            }
            return type;
        }

        // Port of ov02_0224CA58: rotates the first length bytes, byte 0 most significant, right one
        // bit at a time, count times.
        private static void RotateRight(byte[] bytes, int length, int count)
        {
            for (int n = 0; n < count; n++)
            {
                int carry = bytes[length - 1] & 1;
                for (int j = length - 1; j > 0; j--)
                {
                    bytes[j] = (byte)((bytes[j] >> 1) | ((bytes[j - 1] & 1) << 7));
                }
                bytes[0] = (byte)((bytes[0] >> 1) | (carry << 7));
            }
        }
    }
}
