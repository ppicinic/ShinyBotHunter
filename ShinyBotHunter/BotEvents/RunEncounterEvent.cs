using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents.Factories;
using ShinyBotHunter.Games;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Util;

namespace ShinyBotHunter.BotEvents
{
    public class RunEncounterEvent : AbstractBotEvent<GameState>
    {
        private EncounterParams param;
        private IBot bot;
        private readonly HgssPointerService addresses;
        private string? macro = null;

        public RunEncounterEvent(IBot bot, EncounterParams param)
        {
            this.bot = bot;
            this.addresses = (HgssPointerService)bot.Addresses;
            this.param = param;
        }

        public override async Task<GameState> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            if (!state.EncounterMacros.ContainsKey("Run"))
            {
                var x = Utils.UIntToShort(await bot.ReadUIntAsync(addresses.TrainerX));
                var y = Utils.UIntToShort(await bot.ReadUIntAsync(addresses.TrainerY));
                if (param.yRange > 0)
                {
                    state.EncounterMacros.Add("Run", $"M.Up.{y - param.yRange}.{addresses.TrainerY};M.Down.{y + param.yRange}.{addresses.TrainerY}");
                }
                else
                {
                    state.EncounterMacros.Add("Run", $"M.Right.{x + param.xRange}.{addresses.TrainerX};M.Left.{x - param.xRange}.{addresses.TrainerX}");
                }
            }

            bot.RunRepeatMacro(state.EncounterMacros["Run"]);
            try
            {
                await GameStateWait.UntilAsync(bot.State, "a battle",
                    s => s.Screen is not (GameScreen.Transitioning or GameScreen.FieldFree or GameScreen.FieldTask or GameScreen.OtherApp),
                    stale: null, cancellationToken);
            }
            finally
            {
                bot.ClearMacro();
            }
            await bot.WaitForMacroAsync();
            state.State.Pop();
            // TODO we should go into Waiting if its a non wild battle
            state.State.Push(EventState.BattleStart);
            return state;
        }
    }
}
