namespace ShinyBotHunter.Pokemon
{
    /// <summary>
    /// Decodes a Pokémon out of the raw bytes a game stores it in.
    ///
    /// Every generation encrypts and lays out that structure differently -- Gen 4 runs an LCRNG
    /// keystream over four 32-byte blocks shuffled by bits of the PID, Gen 3 XORs twelve-byte
    /// substructures with PID ^ OTID and orders them by PID % 24 -- so this exists to let a
    /// generation's decoder be selected by flavor rather than compiled in.
    /// </summary>
    public interface IPokemonParser
    {
        /// <summary>
        /// Returns null when the bytes cannot be a Pokémon at all, and a record with
        /// <see cref="PokemonData.IsCorrupt"/> set when they decode but fail validation. Callers
        /// generally retry rather than treat either as fatal: a read can land mid-write.
        /// </summary>
        PokemonData? Parse(byte[] raw);
    }
}
