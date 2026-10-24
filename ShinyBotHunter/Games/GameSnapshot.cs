namespace ShinyBotHunter.Games
{
    public readonly record struct GameSnapshot(
        GameScreen Screen,
        BattleOutcome? Outcome, int? Script, int? Prompt, uint? TaskFunction, uint? Native = null, int? SummaryState = null,
        int? TextPrinter = null)
    {
        // RenderText's waits for A or B at the arrow (pointer-discovery.md, "Text printer state (2026-10-24)"); 3, 7 and 8
        // are decomp only.
        public bool AtTextArrow() => TextPrinter is 2 or 3 or 7 or 8;
    }
    // null unless Screen carries it: Outcome on BattleOver; Script on BattleScript, and on FieldTask while a field script
    // runs (env.script); Prompt on the prompts and Evolution; TaskFunction on FieldTask; Native on FieldTask while the
    // field script's innermost live context waits in a native; SummaryState on Evolution at evo.state 23 (the summary
    // app's proc_state); TextPrinter on Evolution and on FieldTask while a field script runs, pr.state of the screen's
    // printer when its slot (evo.printer, env.printer) is 0 and not NULL.
}
