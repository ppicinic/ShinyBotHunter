namespace ShinyBotHunter.Service
{
    /// <summary>
    /// Reads the trainer ID and the section walk out of a Gen 3 SaveRAM file.
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
        private const int PlayTimeOffset = 0x0E;
        private const int PokedexFlagOffset = 0x3A0;
        private const int PartyCountOffset = 0x234;
        private const int PartyOffset = 0x238;
        private const int PartySlotSize = 100;

        private static readonly int[] BlockOffsets = [0x0000, 0xE000];

        // Checksummed data bytes of each section, indexed by section id (pret/PKHeX Gen 3 layout).
        private static readonly int[] SectionDataSizes =
        [
            0xF2C,
            0xF80, 0xF80, 0xF80, 0xF08,
            0xF80, 0xF80, 0xF80, 0xF80, 0xF80, 0xF80, 0xF80, 0xF80,
            0x7D0,
        ];

        /// <summary>
        /// The 14 sections of the block at <paramref name="blockOffset"/> (0x0000 or 0xE000), in the
        /// order they sit on disk. Valid means the signature is present, the id is 0 to 13, and the
        /// stored checksum equals the computed one. A section with an id outside 0 to 13 -- a blank
        /// or half-written file -- gets computed checksum 0 and is not valid.
        /// </summary>
        public static (int Offset, ushort Id, uint Counter, ushort StoredChecksum, ushort ComputedChecksum, bool Valid)[] ReadSections(ReadOnlySpan<byte> save, int blockOffset)
        {
            var sections = new (int Offset, ushort Id, uint Counter, ushort StoredChecksum, ushort ComputedChecksum, bool Valid)[SectionCount];
            for (int i = 0; i < SectionCount; i++)
            {
                int start = blockOffset + i * SectionSize;
                ReadOnlySpan<byte> footer = save[(start + FooterOffset)..];
                ushort id = BitConverter.ToUInt16(footer);
                ushort stored = BitConverter.ToUInt16(footer[2..]);
                uint signature = BitConverter.ToUInt32(footer[4..]);
                uint counter = BitConverter.ToUInt32(footer[8..]);

                ushort computed = 0;
                if (id < SectionCount)
                {
                    uint sum = 0;
                    ReadOnlySpan<byte> data = save.Slice(start, SectionDataSizes[id]);
                    for (int w = 0; w < data.Length; w += 4)
                    {
                        sum += BitConverter.ToUInt32(data[w..]);
                    }
                    computed = (ushort)((sum >> 16) + (sum & 0xFFFF));
                }

                bool valid = signature == SectionSignature && id < SectionCount && stored == computed;
                sections[i] = (start, id, counter, stored, computed, valid);
            }
            return sections;
        }

        /// <summary>
        /// The offset of the live block: the one whose section 0 carries the higher counter, or null
        /// when neither block holds a signed section 0.
        /// </summary>
        public static int? LiveBlockOffset(ReadOnlySpan<byte> save)
        {
            int? bestBlock = null;
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
                    if (bestBlock is null || counter >= bestCounter)
                    {
                        bestBlock = block;
                        bestCounter = counter;
                    }
                }
            }
            return bestBlock;
        }

        /// <summary>
        /// The trainer ID of the live save, or null when the file holds no readable save -- a fresh
        /// or truncated file, or one the game never finished writing.
        /// </summary>
        public static ushort? ReadTrainerId(ReadOnlySpan<byte> save)
        {
            int? block = LiveBlockOffset(save);
            if (block is null)
            {
                return null;
            }
            return BitConverter.ToUInt16(save[(SectionOffset(save, block.Value, 0) + TrainerIdOffset)..]);
        }

        /// <summary>
        /// The Wishmaker seed: the stored checksum of section 0 in the block at 0x0000, whichever
        /// block is live.
        /// </summary>
        public static ushort ReadWishmakerSeed(ReadOnlySpan<byte> save)
        {
            return BitConverter.ToUInt16(save[(SectionOffset(save, BlockOffsets[0], 0) + FooterOffset + 2)..]);
        }

        /// <summary>The play time of the live save.</summary>
        public static (ushort Hours, byte Minutes, byte Seconds, byte Frames) ReadPlayTime(ReadOnlySpan<byte> save)
        {
            ReadOnlySpan<byte> time = save[(LiveSectionOffset(save, 0) + PlayTimeOffset)..];
            return (BitConverter.ToUInt16(time), time[2], time[3], time[4]);
        }

        /// <summary>Whether the live save has the Pokedex (FLAG_SYS_POKEDEX_GET, flag 0x801).</summary>
        public static bool HasPokedex(ReadOnlySpan<byte> save)
        {
            return (save[LiveSectionOffset(save, 2) + PokedexFlagOffset] & 0x02) != 0;
        }

        /// <summary>The number of party members in the live save.</summary>
        public static int ReadPartyCount(ReadOnlySpan<byte> save)
        {
            return save[LiveSectionOffset(save, 1) + PartyCountOffset];
        }

        /// <summary>The 100 encrypted bytes of a party slot in the live save, as Gen3PokemonParser takes them.</summary>
        public static byte[] ReadPartySlot(ReadOnlySpan<byte> save, int slot)
        {
            return save.Slice(LiveSectionOffset(save, 1) + PartyOffset + PartySlotSize * slot, PartySlotSize).ToArray();
        }

        /// <summary>
        /// The trainer ID in a SaveRAM file, or null when it holds no readable save. Unreadable is not
        /// an error here: files are inspected while saves are being created, so one that is missing or
        /// half-written is an ordinary outcome rather than a fault.
        /// </summary>
        public static ushort? ReadTrainerIdFromFile(string saveRamPath)
        {
            try
            {
                return File.Exists(saveRamPath) ? ReadTrainerId(File.ReadAllBytes(saveRamPath)) : null;
            }
            catch (IOException)
            {
                // Being written right now. The caller re-reads on its next attempt.
                return null;
            }
        }

        private static int LiveSectionOffset(ReadOnlySpan<byte> save, int id)
        {
            int block = LiveBlockOffset(save) ?? throw new InvalidOperationException("The save has no live block.");
            return SectionOffset(save, block, id);
        }

        /// <summary>
        /// The offset of the signed section with this id in the block. Sections rotate on disk, so it
        /// is found by footer id; a block without it is unreadable and throws.
        /// </summary>
        private static int SectionOffset(ReadOnlySpan<byte> save, int blockOffset, int id)
        {
            for (int i = 0; i < SectionCount; i++)
            {
                int start = blockOffset + i * SectionSize;
                if (start + SectionSize > save.Length)
                {
                    continue;
                }
                ReadOnlySpan<byte> footer = save[(start + FooterOffset)..];
                if (BitConverter.ToUInt32(footer[4..]) == SectionSignature && BitConverter.ToUInt16(footer) == id)
                {
                    return start;
                }
            }
            throw new InvalidOperationException($"Save block 0x{blockOffset:X4} has no section {id}.");
        }
    }
}
