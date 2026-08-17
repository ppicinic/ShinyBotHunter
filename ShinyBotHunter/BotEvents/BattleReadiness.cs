namespace ShinyBotHunter.BotEvents
{
    /// <summary>
    /// The one place the "too hurt to fight" boundary is defined. The battle loop and the overworld
    /// loop both need it and must agree exactly: if the overworld lets a pokemon start an encounter
    /// that the battle loop then flees from, the bot encounters and flees forever instead of walking
    /// to a pokemon center. That is what happened when one side used < and the other <=.
    /// </summary>
    public static class BattleReadiness
    {
        private const int FleeHpDivisor = 4;

        public static bool IsTooHurtToFight(int hp, int maxHp) => hp <= maxHp / FleeHpDivisor;
    }
}
