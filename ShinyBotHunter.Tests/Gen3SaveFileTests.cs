using ShinyBotHunter.Service;

namespace ShinyBotHunter.Tests
{
    /// <summary>
    /// Pins the SaveRAM parsing the hunt uses to refuse a duplicated trainer.
    ///
    /// Getting the live block wrong is the failure that matters: the game alternates between two
    /// save blocks, so reading the stale one reports the trainer a save used to have and lets a
    /// genuine duplicate through, or rejects a save that was fine.
    /// </summary>
    public class Gen3SaveFileTests
    {
        private const int SectionSize = 4096;
        private const int FooterOffset = 0xFF4;
        private const uint Signature = 0x08012025;
        private const int TrainerIdOffset = 0x0A;

        /// <summary>A 128KB flash image with section 0 written into each block at the given counters.</summary>
        private static byte[] Save((ushort trainerId, uint counter)? blockA, (ushort trainerId, uint counter)? blockB)
        {
            var save = new byte[128 * 1024];
            void WriteBlock(int offset, (ushort trainerId, uint counter) block)
            {
                // Section 0 deliberately is not the first section on disk: the game writes them in
                // rotation, so the parser has to find it by id rather than by position.
                int section = offset + 3 * SectionSize;
                BitConverter.GetBytes(block.trainerId).CopyTo(save, section + TrainerIdOffset);
                BitConverter.GetBytes((ushort)0).CopyTo(save, section + FooterOffset);
                BitConverter.GetBytes(Signature).CopyTo(save, section + FooterOffset + 4);
                BitConverter.GetBytes(block.counter).CopyTo(save, section + FooterOffset + 8);
            }
            if (blockA is not null) WriteBlock(0x0000, blockA.Value);
            if (blockB is not null) WriteBlock(0xE000, blockB.Value);
            return save;
        }

        [Fact]
        public void ReadsTheTrainerIdOfASaveWrittenOnce()
        {
            Assert.Equal((ushort)23592, Gen3SaveFile.ReadTrainerId(Save((23592, 1), null)));
        }

        [Fact]
        public void PrefersTheBlockWithTheHigherCounter()
        {
            // Second block is newer, so its trainer is the live one.
            Assert.Equal((ushort)19231, Gen3SaveFile.ReadTrainerId(Save((23592, 4), (19231, 5))));
            // ...and the same holds when the newer block is the first one.
            Assert.Equal((ushort)23592, Gen3SaveFile.ReadTrainerId(Save((23592, 9), (19231, 8))));
        }

        [Fact]
        public void ReadsASaveWhoseBlocksShareACounter()
        {
            // A save written once leaves both blocks at the same counter. Requiring a strictly higher
            // one would report a perfectly good save as unreadable and skip the duplicate check.
            Assert.Equal((ushort)32690, Gen3SaveFile.ReadTrainerId(Save((32690, 1), (32690, 1))));
        }

        [Fact]
        public void RejectsAFileWithNoSaveInIt()
        {
            Assert.Null(Gen3SaveFile.ReadTrainerId(new byte[128 * 1024]));
        }

        [Fact]
        public void RejectsAFileTooShortToHoldASection()
        {
            Assert.Null(Gen3SaveFile.ReadTrainerId(new byte[64]));
        }

        [Fact]
        public void RejectsSectionsWithoutTheSignature()
        {
            byte[] save = Save((23592, 1), null);
            // Corrupt the signature the way a half-written save would.
            BitConverter.GetBytes(0u).CopyTo(save, 3 * SectionSize + FooterOffset + 4);
            Assert.Null(Gen3SaveFile.ReadTrainerId(save));
        }

        [Fact]
        public void MissingSlotDirectoryIsNotAnError()
        {
            Assert.Null(Gen3SaveFile.ReadTrainerIdFromSlot(Path.Combine(Path.GetTempPath(), "no-such-slot-" + Guid.NewGuid())));
        }
    }
}
