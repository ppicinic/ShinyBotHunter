using System.Buffers.Binary;
using System.Text;
using ShinyBotHunter.Emulators.Dolphin;

namespace ShinyBotHunter.Tests
{
    public class DolphinPadTests
    {
        private static bool CrcIsValid(byte[] packet)
        {
            byte[] copy = (byte[])packet.Clone();
            copy.AsSpan(8, 4).Clear();
            return DolphinPad.Crc32(copy) == BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8));
        }

        private static uint MessageType(byte[] packet) => BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(16));

        [Fact]
        public void Crc32MatchesTheStandardCheckValue()
        {
            Assert.Equal(0xCBF4_3926u, DolphinPad.Crc32(Encoding.ASCII.GetBytes("123456789")));
        }

        [Fact]
        public void PadFrameHasDsuHeaderAndValidCrc()
        {
            byte[] frame = DolphinPad.BuildPadFrame(0, 7, 0);

            Assert.Equal(100, frame.Length);
            Assert.Equal("DSUS", Encoding.ASCII.GetString(frame, 0, 4));
            Assert.Equal(1001, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4)));
            Assert.Equal(84, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(6)));
            Assert.Equal(0x10_0002u, MessageType(frame));
            Assert.True(CrcIsValid(frame));
        }

        [Fact]
        public void PadFrameCarriesButtonsAndCentredSticks()
        {
            byte[] idle = DolphinPad.BuildPadFrame(0, 7, 0);
            byte[] cross = DolphinPad.BuildPadFrame(DolphinPad.Cross, 7, 0);
            byte[] right = DolphinPad.BuildPadFrame(DolphinPad.PadE, 7, 0);

            // Slot 0 connected, then the frame's own connected byte and packet number.
            Assert.Equal(0, idle[20]);
            Assert.Equal(2, idle[21]);
            Assert.Equal(1, idle[31]);
            Assert.Equal(7u, BinaryPrimitives.ReadUInt32LittleEndian(idle.AsSpan(32)));
            Assert.Equal(new byte[] { 128, 128, 128, 128 }, idle[40..44]);
            Assert.All(idle[36..40], b => Assert.Equal(0, b));
            Assert.All(idle[44..56], b => Assert.Equal(0, b));

            Assert.Equal(0x00, cross[36]);
            Assert.Equal(0x40, cross[37]);
            Assert.Equal(255, cross[49]);

            Assert.Equal(0x20, right[36]);
            Assert.Equal(0x00, right[37]);
            Assert.Equal(255, right[46]);
        }

        [Fact]
        public void VersionAndPortInfoRepliesHaveValidCrc()
        {
            byte[] version = DolphinPad.BuildVersionReply();
            byte[] slot0 = DolphinPad.BuildPortInfoReply(0);
            byte[] slot1 = DolphinPad.BuildPortInfoReply(1);

            Assert.Equal(0x10_0000u, MessageType(version));
            Assert.Equal(1001, BinaryPrimitives.ReadUInt16LittleEndian(version.AsSpan(20)));
            Assert.True(CrcIsValid(version));

            Assert.Equal(0x10_0001u, MessageType(slot0));
            Assert.Equal(new byte[] { 0, 2, 2, 1, 0, 0, 0, 0, 0, 1, 5 }, slot0[20..31]);
            Assert.True(CrcIsValid(slot0));

            Assert.Equal(1, slot1[20]);
            Assert.Equal(0, slot1[21]);
            Assert.True(CrcIsValid(slot1));
        }
    }
}
