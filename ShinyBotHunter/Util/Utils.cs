namespace ShinyBotHunter.Util
{
    public class Utils
    {
        public static short UIntToShort(uint u32)
        {
            return (short)(((u32 / 65536) + 32768) % 65536 - 32768);
        }
    }
}
