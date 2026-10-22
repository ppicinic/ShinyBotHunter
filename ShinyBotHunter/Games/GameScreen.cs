namespace ShinyBotHunter.Games
{
    public enum GameScreen
    {
        Transitioning,        // the legacy reader never returns this; reserved for the chain reader
        FieldFree,            // not in a battle (legacy: battle indicator not set)
        FieldTask, OtherApp, BattleStarting, BattleMoveList, BattleMessage, BattleScript,
        BattleExpPrompt, BattleMoveLearnPrompt, BattleOver,   // reserved for the chain reader; see hgss-state-reader.md
        BattleMenu,           // legacy: indicator set and menu byte == 1
        BattleTurn,           // legacy: indicator set and menu byte != 1
        Evolution,            // legacy: battle-state byte == 14
    }
}
