namespace ShinyBotHunter.Games
{
    // What each screen means: the state table in docs/specs/hgss-state-reader.md.
    public enum GameScreen
    {
        Transitioning,
        FieldFree,
        FieldTask, OtherApp, BattleStarting, BattleMoveList, BattleMessage, BattleScript,
        BattleExpPrompt, BattleMoveLearnPrompt, BattleOver,
        BattleMenu,
        BattleTurn,
        Evolution,
    }
}
