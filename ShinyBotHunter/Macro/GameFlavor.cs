namespace ShinyBotHunter.Macro
{
    /// <summary>
    /// Append-only. Values are persisted (Postgres <c>encounters.Game</c>, Mongo documents), so
    /// inserting or reordering members rewrites the meaning of existing rows.
    /// </summary>
    public enum GameFlavor
    {
        HGSS,
        /// <summary>Ruby and Sapphire. Paired like HGSS: same macros, same RAM map.</summary>
        RS,
    }
}
