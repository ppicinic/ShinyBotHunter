namespace ShinyBotHunter.Games
{
    public readonly record struct GameSnapshot(
        GameScreen Screen,
        BattleOutcome? Outcome, int? Script, int? Prompt, uint? TaskFunction, uint? Native = null);
    // null unless Screen carries it: Outcome on BattleOver; Script on BattleScript, and on FieldTask while a field script
    // runs (env.script); Prompt on the prompts and Evolution; TaskFunction on FieldTask; Native on FieldTask while the
    // field script's innermost live context waits in a native.
}
