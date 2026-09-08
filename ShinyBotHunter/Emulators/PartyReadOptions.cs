namespace ShinyBotHunter.Emulators
{
    /// <summary>How the emulator reaches the party structure.</summary>
    public enum PartyAddressing : byte
    {
        /// <summary>
        /// The address is a pointer that has to be dereferenced first. Gen 4 relocates its save
        /// block, so its party can only be found through an anchor that moves.
        /// </summary>
        Anchor = 0x00,

        /// <summary>
        /// The address is the party itself. Gen 3 keeps it at a fixed location, so there is nothing
        /// to dereference and a dereference would read the party's own bytes as a pointer.
        /// </summary>
        Absolute = 0x01,
    }

    /// <summary>
    /// Which generation's stored-pokemon structure the emulator validates slots against. The two
    /// encrypt and lay out their data differently enough that using the wrong one rejects every slot.
    /// </summary>
    public enum PokemonScheme : byte
    {
        Gen4 = 0x00,
        Gen3 = 0x01,
    }
}
