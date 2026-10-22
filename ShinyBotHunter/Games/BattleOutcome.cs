namespace ShinyBotHunter.Games
{
    /// <summary>How a battle ended, numbered as the HGSS outcome byte numbers it (hgss-state-reader.md, OutcomeOffset).</summary>
    public enum BattleOutcome
    {
        Won = 1,
        Lost = 2,
        Draw = 3,
        Caught = 4,
        Fled = 5,
    }
}
