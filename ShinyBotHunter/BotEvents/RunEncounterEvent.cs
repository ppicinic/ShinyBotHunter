using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents.Factories;
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
            uint battleIndicator = addresses.BattleIndicator;
            var battleValue = await bot.ReadByteAsync(battleIndicator);
            while (!(battleValue == 0x41 || battleValue == 0x97 || battleValue == 0xC0) && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(Constant.PollIntervalMs);
                battleValue = await bot.ReadByteAsync(battleIndicator);
            }
            bot.ClearMacro();
            await bot.WaitForMacroAsync();
            state.State.Pop();
            // TODO we should go into Waiting if its a non wild battle
            state.State.Push(EventState.BattleStart);
            return state;
        }
    }
}
