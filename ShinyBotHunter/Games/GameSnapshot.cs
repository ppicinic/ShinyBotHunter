namespace ShinyBotHunter.Games
{
    public readonly record struct GameSnapshot(
        GameScreen Screen,
        BattleOutcome? Outcome, int? Script, int? Prompt, uint? TaskFunction);   // all null from the legacy reader
}
