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

        // Checksummed data bytes of each section, by id (pret/PKHeX Gen 3 layout), kept here so the
        // checksums below are computed independently of the reader under test.
        private static readonly int[] SectionDataSizes =
            [0xF2C, 0xF80, 0xF80, 0xF80, 0xF08, 0xF80, 0xF80, 0xF80, 0xF80, 0xF80, 0xF80, 0xF80, 0xF80, 0x7D0];

        /// <summary>
        /// A 128KB flash image the way a hunt save looks after two saves: both blocks hold all 14 signed
        /// sections with valid checksums, block A at counter 2 and block B (live) at 3, each block with
        /// the Pokedex flag set and one party member. Sections are rotated on disk and their data is a
        /// pattern that differs per block, so a wrong data size or a stale block gives a different answer.
        /// </summary>
        internal static byte[] CompleteSave()
        {
            var save = new byte[128 * 1024];
            foreach ((int block, uint counter, int rotation) in new[] { (0x0000, 2u, 5), (0xE000, 3u, 3) })
            {
                for (int i = 0; i < 14; i++)
                {
                    int start = block + i * SectionSize;
                    ushort id = (ushort)((i + rotation) % 14);
                    for (int j = 0; j < FooterOffset; j++)
                    {
                        save[start + j] = (byte)(j * 31 + id * 7 + block / SectionSize);
                    }
                    BitConverter.GetBytes(id).CopyTo(save, start + FooterOffset);
                    BitConverter.GetBytes(Signature).CopyTo(save, start + FooterOffset + 4);
                    BitConverter.GetBytes(counter).CopyTo(save, start + FooterOffset + 8);
                }
                save[Section(save, block, 2) + 0x3A0] |= 0x02;
                save[Section(save, block, 1) + 0x234] = 1;
                Resign(save, block);
            }
            return save;
        }

        /// <summary>The offset of the section with this footer id in the block.</summary>
        internal static int Section(byte[] save, int block, int id)
        {
            for (int i = 0; i < 14; i++)
            {
                int start = block + i * SectionSize;
                if (BitConverter.ToUInt16(save, start + FooterOffset) == id)
                {
                    return start;
                }
            }
            throw new InvalidOperationException($"No section {id} in block 0x{block:X4}.");
        }

        /// <summary>The Gen 3 checksum of the section at <paramref name="start"/>: its data words summed, the halves folded.</summary>
        private static ushort Checksum(byte[] save, int start)
        {
            int id = BitConverter.ToUInt16(save, start + FooterOffset);
            uint sum = 0;
            for (int w = 0; w < SectionDataSizes[id]; w += 4)
            {
                sum += BitConverter.ToUInt32(save, start + w);
            }
            return (ushort)((sum >> 16) + (sum & 0xFFFF));
        }

        /// <summary>Writes the checksum of every section of the block into its footer, after an edit.</summary>
        internal static void Resign(byte[] save, int block)
        {
            for (int i = 0; i < 14; i++)
            {
                int start = block + i * SectionSize;
                BitConverter.GetBytes(Checksum(save, start)).CopyTo(save, start + FooterOffset + 2);
            }
        }

        private static void WritePlayTime(byte[] save, int block, ushort hours, byte minutes, byte seconds, byte frames)
        {
            int section0 = Section(save, block, 0);
            BitConverter.GetBytes(hours).CopyTo(save, section0 + 0x0E);
            save[section0 + 0x10] = minutes;
            save[section0 + 0x11] = seconds;
            save[section0 + 0x12] = frames;
            Resign(save, block);
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
        public void MissingSaveFileIsNotAnError()
        {
            // Saves are inspected while they are being created, so absent is an ordinary answer.
            Assert.Null(Gen3SaveFile.ReadTrainerIdFromFile(Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid()}.SaveRAM")));
        }

        [Fact]
        public void ReadsATrainerIdFromAFileOnDisk()
        {
            string path = Path.Combine(Path.GetTempPath(), $"gen3-save-{Guid.NewGuid()}.SaveRAM");
            try
            {
                File.WriteAllBytes(path, Save((19231, 1), null));
                Assert.Equal((ushort)19231, Gen3SaveFile.ReadTrainerIdFromFile(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void FindsTheLiveBlockByTheHigherCounter()
        {
            Assert.Equal(0xE000, Gen3SaveFile.LiveBlockOffset(CompleteSave()));
        }

        [Fact]
        public void EverySectionOfACompleteSaveIsValid()
        {
            // Pins the data-size table and the checksum fold against checksums computed in this test.
            byte[] save = CompleteSave();
            foreach (int block in new[] { 0x0000, 0xE000 })
            {
                var sections = Gen3SaveFile.ReadSections(save, block);
                Assert.Equal(14, sections.Length);
                Assert.All(sections, s => Assert.True(s.Valid, $"block 0x{block:X4} section {s.Id}"));
                Assert.Equal(Enumerable.Range(0, 14), sections.Select(s => (int)s.Id).Order());
            }
        }

        [Fact]
        public void ReadsTheWishmakerSeedFromTheFirstBlock()
        {
            // Block B is live (counter 3 against 2), but the seed comes from the first block.
            byte[] save = CompleteSave();
            ushort firstBlock = Checksum(save, Section(save, 0x0000, 0));
            Assert.NotEqual(Checksum(save, Section(save, 0xE000, 0)), firstBlock);
            Assert.Equal(firstBlock, Gen3SaveFile.ReadWishmakerSeed(save));
        }

        [Fact]
        public void ReadsTheWishmakerSeedAsTheStoredChecksum()
        {
            // The disc seeds from the checksum the footer stores, not one computed from the data.
            byte[] save = CompleteSave();
            BitConverter.GetBytes((ushort)0xECDD).CopyTo(save, Section(save, 0x0000, 0) + FooterOffset + 2);
            Assert.Equal((ushort)0xECDD, Gen3SaveFile.ReadWishmakerSeed(save));
        }

        [Theory]
        [InlineData(0x353D, 0xBA7DF435u)]
        [InlineData(0x3D60, 0xEB0DA543u)]
        [InlineData(0x7236, 0x2EC1608Du)]
        [InlineData(0x7360, 0xBF98F1D5u)]
        [InlineData(0x9359, 0xBCFAF2B0u)]
        [InlineData(0xA030, 0x4633087Du)]
        [InlineData(0xCF37, 0x7B053548u)]
        [InlineData(0xECDD, 0x987ED636u)]
        [InlineData(0xF500, 0xC90E8744u)]
        public void PredictPidMatchesTheShinySeeds(int seed, uint pid)
        {
            Assert.Equal(pid, Gen3SaveFile.PredictWishmakerPid((ushort)seed));
            // Independent of the rule: each of the nine is shiny for the Wishmaker OT 20043, SID 0.
            Assert.True((20043 ^ 0 ^ (pid & 0xFFFF) ^ (pid >> 16)) < 8, $"{pid:X8} is not shiny for 20043/0");
        }

        [Fact]
        public void PredictPidFromASaveSeededEcdd()
        {
            byte[] save = CompleteSave();
            BitConverter.GetBytes((ushort)0xECDD).CopyTo(save, Section(save, 0x0000, 0) + FooterOffset + 2);
            Assert.Equal(0x987ED636u, Gen3SaveFile.PredictWishmakerPid(Gen3SaveFile.ReadWishmakerSeed(save)));
        }

        [Fact]
        public void ReadsTheSameSeedWithOrWithoutTheMgbaFooter()
        {
            byte[] flashOnly = CompleteSave();
            byte[] withFooter = [.. flashOnly, .. Enumerable.Repeat((byte)0xAB, 16)];
            Assert.Equal(Gen3SaveFile.ReadWishmakerSeed(flashOnly), Gen3SaveFile.ReadWishmakerSeed(withFooter));
        }

        [Fact]
        public void ReadsThePokedexFlagAndPartyCount()
        {
            byte[] save = CompleteSave();
            Assert.True(Gen3SaveFile.HasPokedex(save));
            Assert.Equal(1, Gen3SaveFile.ReadPartyCount(save));
        }

        [Fact]
        public void ReadsAPokedexFlagThatIsClear()
        {
            byte[] save = CompleteSave();
            save[Section(save, 0xE000, 2) + 0x3A0] &= unchecked((byte)~0x02);
            Assert.False(Gen3SaveFile.HasPokedex(save));
        }

        [Fact]
        public void ReadsAFullParty()
        {
            byte[] save = CompleteSave();
            save[Section(save, 0xE000, 1) + 0x234] = 6;
            Assert.Equal(6, Gen3SaveFile.ReadPartyCount(save));
        }

        [Fact]
        public void ReadsThePlayTimeOfTheLiveBlock()
        {
            byte[] save = CompleteSave();
            WritePlayTime(save, 0x0000, 0, 14, 51, 40);
            WritePlayTime(save, 0xE000, 0, 15, 13, 3);
            Assert.Equal(((ushort)0, (byte)15, (byte)13, (byte)3), Gen3SaveFile.ReadPlayTime(save));
        }

        [Fact]
        public void ACompleteSaveIsCompleteAfterItsOlderCounter()
        {
            byte[] save = CompleteSave();
            Assert.True(Gen3SaveFile.HasCompleteBlockAfter(save, 2));
            Assert.False(Gen3SaveFile.HasCompleteBlockAfter(save, 3));
        }

        [Fact]
        public void APartlyWrittenSaveIsNotComplete()
        {
            // The 8-of-14 case: six sections of block B still carry the old counter. The counter sits
            // outside the checksummed data, so every section stays valid and only the counter rule fails.
            byte[] save = CompleteSave();
            foreach (var section in Gen3SaveFile.ReadSections(save, 0xE000).Where(s => s.Id != 0).Take(6))
            {
                BitConverter.GetBytes(2u).CopyTo(save, section.Offset + FooterOffset + 8);
            }
            Assert.False(Gen3SaveFile.HasCompleteBlockAfter(save, 2));
            Assert.All(Gen3SaveFile.ReadSections(save, 0x0000).Concat(Gen3SaveFile.ReadSections(save, 0xE000)), s => Assert.True(s.Valid));
        }

        [Fact]
        public void ASectionWithABadChecksumIsNotComplete()
        {
            byte[] save = CompleteSave();
            int section = Gen3SaveFile.ReadSections(save, 0xE000)[5].Offset;
            save[section] ^= 0xFF;
            Assert.False(Gen3SaveFile.ReadSections(save, 0xE000)[5].Valid);
            Assert.False(Gen3SaveFile.HasCompleteBlockAfter(save, 2));
        }

        [Fact]
        public void ABlankFileHasNoCompleteBlock()
        {
            // A fresh SaveRAM: ids are 0 and every signature is missing.
            Assert.False(Gen3SaveFile.HasCompleteBlockAfter(new byte[131072], 0));
        }
    }
}
