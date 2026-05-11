using ShinyBotHunter.Bot;
using ShinyBotHunter.BotEvents.Factories;

namespace ShinyBotHunter.BotEvents
{
    public class RunEncounterEvent : AbstractBotEvent<bool>
    {
        public override EventState EventState
        {
            get
            {
                return EventState.Complete;
            }
        }

        private EncounterParams param;
        private IBot bot;
        private uint yAddr = 0x21DA6FC;
        private uint xAddr = 0x21DA6F4;
        private string? macro = null;

        public RunEncounterEvent(IBot bot, EncounterParams param)
        {
            this.bot = bot;
            this.param = param;
        }

        public override async Task<bool> ExecuteWithResultAsync(CancellationToken cancellationToken)
        {
            if(macro is null)
            {
                var x = Utils.UIntToShort(await bot.ReadUIntAsync(xAddr));
                var y = Utils.UIntToShort(await bot.ReadUIntAsync(yAddr));
                if(param.yRange > 0)
                {
                    macro = $"M.Up.{y - param.yRange}.{yAddr};M.Down.{y + param.yRange}.{yAddr}";
                }
                else
                {
                    macro = $"M.Right.{x + param.xRange}.{xAddr};M.Left.{x - param.xRange}.{xAddr}";
                }
            }

            bot.RunRepeatMacro(macro);
            uint battleIndicator = 0x21E76D2;
            var battleValue = await bot.ReadByteAsync(battleIndicator);
            while (!(battleValue == 0x41 || battleValue == 0x97 || battleValue == 0xC0) && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(166); // sleep 10 frames before re-polling memory -
                                       // this is semi dangerous we don't know how slow the memory read is and we want to read on ever 10 frames
                                       // regardless of the delay, so real implementition needs a better solution
                battleValue = await bot.ReadByteAsync(battleIndicator);
            }
            bot.ClearMacro();
            await bot.WaitForMacroAsync();
            Console.WriteLine("Found battle");
            return battleValue == 0x41 || battleValue == 0x97 || battleValue == 0xC0;
        }
    }
}
