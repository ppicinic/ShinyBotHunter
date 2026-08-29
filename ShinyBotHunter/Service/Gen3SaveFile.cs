namespace ShinyBotHunter.Service
{
    /// <summary>
    /// Reads the trainer ID out of a Gen 3 SaveRAM file.
    ///
    /// The 128KB flash holds two save blocks, at 0x0000 and 0xE000, each fourteen 4096-byte sections.
    /// Every section ends with a footer: id, checksum, a fixed signature, and a counter. The game
    /// alternates blocks on each save, so the live one is whichever copy of section 0 -- the block
    /// holding trainer data -- carries the higher counter. The trainer ID sits at offset 0x0A there.
    ///
    /// Only the trainer ID is read, never the secret ID beside it. A player sees their trainer ID on
    /// the trainer card and could act on it; the secret ID is invisible, and deciding anything on it
    /// would be deciding on memory no one can see. Nothing is lost by the restriction: Gen 3 draws
    /// both from consecutive RNG calls at one moment, so two saves created on the same frame share
    /// both, and a trainer ID collision is what a duplicated save actually looks like.
    /// </summary>
    public static class Gen3SaveFile
    {
        private const int SectionSize = 4096;
        private const int SectionCount = 14;
        private const int FooterOffset = 0xFF4;
        private const uint SectionSignature = 0x08012025;
        private const int TrainerIdOffset = 0x0A;

        private static readonly int[] BlockOffsets = [0x0000, 0xE000];

        /// <summary>
        /// The trainer ID of the live save, or null when the file holds no readable save -- a fresh
        /// or truncated file, or one the game never finished writing.
        /// </summary>
        public static ushort? ReadTrainerId(ReadOnlySpan<byte> save)
        {
            int bestSection = -1;
            uint bestCounter = 0;

            foreach (int block in BlockOffsets)
            {
                for (int i = 0; i < SectionCount; i++)
                {
                    int start = block + i * SectionSize;
                    if (start + SectionSize > save.Length)
                    {
                        continue;
                    }

                    ReadOnlySpan<byte> footer = save[(start + FooterOffset)..];
                    ushort sectionId = BitConverter.ToUInt16(footer);
                    uint signature = BitConverter.ToUInt32(footer[4..]);
                    uint counter = BitConverter.ToUInt32(footer[8..]);

                    if (signature != SectionSignature || sectionId != 0)
                    {
                        continue;
                    }
                    // >= rather than >: a save written once leaves both blocks at the same counter,
                    // and taking neither would report a perfectly good save as unreadable.
                    if (bestSection < 0 || counter >= bestCounter)
                    {
                        bestSection = start;
                        bestCounter = counter;
                    }
                }
            }

            if (bestSection < 0)
            {
                return null;
            }
            return BitConverter.ToUInt16(save[(bestSection + TrainerIdOffset)..]);
        }

        /// <summary>
        /// The trainer ID of the save in a slot directory, or null when the slot holds no readable
        /// save. Unreadable is not an error here: a slot is inspected while saves are being created,
        /// so a file that is missing or half-written is an ordinary outcome.
        /// </summary>
        public static ushort? ReadTrainerIdFromSlot(string slotDirectory)
        {
            if (!Directory.Exists(slotDirectory))
            {
                return null;
            }

            foreach (string file in Directory.EnumerateFiles(slotDirectory, "*.SaveRAM"))
            {
                try
                {
                    ushort? id = ReadTrainerId(File.ReadAllBytes(file));
                    if (id is not null)
                    {
                        return id;
                    }
                }
                catch (IOException)
                {
                    // Being written right now. The caller re-reads on its next attempt.
                }
            }
            return null;
        }
    }
}
