namespace ShinyBotHunter.Service
{
    /// <summary>
    /// Makes a per-instance ROM copy get its own save file.
    ///
    /// BizHawk names a save after the game, not the file: it hashes the ROM, looks the hash up in its
    /// game database, and uses the canonical name it finds. Copying a ROM per emulator therefore does
    /// not give each one its own save on GBA -- every byte-identical copy resolves to the same entry
    /// and they all write to one SaveRAM file, silently overwriting each other. NDS copies escape this
    /// only because those ROMs are not in the database, so BizHawk already falls back to the filename.
    ///
    /// Stamping a byte the game never reads puts the copy outside the database too, which gets the
    /// same filename fallback. The stamp goes in the ROM's trailing padding -- over a megabyte of 0xFF
    /// on a Pokémon cartridge -- which is past the game's own data and well outside the header
    /// checksum's range, so nothing about how the game runs changes. Only the disposable copies are
    /// ever touched; the ROM they were made from is left exactly as it was.
    /// </summary>
    public static class RomSaveIsolation
    {
        /// <summary>Bytes of padding required before anything is written.</summary>
        private const int RequiredPadding = 1024;

        /// <summary>
        /// Stamps <paramref name="instanceId"/> into the end of a ROM copy so it hashes differently
        /// from its siblings.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The file does not end in padding. Thrown rather than written to, because without a run of
        /// unused bytes there is nowhere to stamp that is guaranteed not to be game data.
        /// </exception>
        public static void Stamp(string romPath, int instanceId)
        {
            using FileStream file = new(romPath, FileMode.Open, FileAccess.ReadWrite);

            if (file.Length < RequiredPadding)
            {
                throw new InvalidOperationException($"{romPath} is too small to be a ROM.");
            }

            byte[] tail = new byte[RequiredPadding];
            file.Seek(-RequiredPadding, SeekOrigin.End);
            file.ReadExactly(tail);

            foreach (byte b in tail)
            {
                if (b != 0xFF)
                {
                    throw new InvalidOperationException(
                        $"{romPath} does not end in unused padding, so there is nowhere safe to mark it. " +
                        "Without a distinct hash this copy would share its save file with every other copy.");
                }
            }

            // Four bytes at the very end: far past anything the game reads, and enough to keep every
            // instance distinct.
            file.Seek(-4, SeekOrigin.End);
            file.Write(BitConverter.GetBytes(instanceId));
            file.Flush();
        }

        /// <summary>Whether a ROM has already been stamped, so re-running does not rewrite it.</summary>
        public static bool IsStamped(string romPath)
        {
            using FileStream file = new(romPath, FileMode.Open, FileAccess.Read);
            if (file.Length < 4) return false;

            byte[] last = new byte[4];
            file.Seek(-4, SeekOrigin.End);
            file.ReadExactly(last);
            return BitConverter.ToUInt32(last) != 0xFFFFFFFF;
        }
    }
}
