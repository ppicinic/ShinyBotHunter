namespace ShinyBotHunter.Context
{
    /// <summary>
    /// Append-only, same persistence reasoning as <see cref="Macro.GameFlavor"/>. The name is also
    /// the key into the per-console settings dictionaries (MaxEmulators, DisplayWidth, ...).
    /// </summary>
    public enum ConsoleType
    {
        DS,
        GBA
    }
}
