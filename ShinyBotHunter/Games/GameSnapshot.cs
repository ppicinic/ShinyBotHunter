namespace ShinyBotHunter.Games
{
    public readonly record struct GameSnapshot(
        GameScreen Screen,
        BattleOutcome? Outcome, int? Script, int? Prompt, uint? TaskFunction);   // null unless Screen carries it: BattleOver, BattleScript, the prompts, Evolution, FieldTask
}
