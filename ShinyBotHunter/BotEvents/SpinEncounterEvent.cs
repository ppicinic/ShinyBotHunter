using DnsClient;
using ShinyBotHunter.Bot;
using System.Text;

namespace ShinyBotHunter.BotEvents
{
    public class SpinEncounterEvent : AbstractBotEvent<bool>
    {
        private IBot bot;
        Queue<byte> directions = new Queue<byte>();
        Dictionary<byte, string> cachedMacros = new Dictionary<byte, string>();

        public SpinEncounterEvent(IBot bot)
        {
            this.bot = bot; 
            directions.Enqueue(0);
            directions.Enqueue(2);
            directions.Enqueue(1);
            directions.Enqueue(3);
        }
        public override EventState EventState
        {
            get
            {
                return EventState.Complete;
            }
        }

        public override async Task<bool> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            uint anchor = await bot.ReadUIntAsync(0x21D4158);
            uint facingPtr = anchor + 0x1DC4;
            byte facing = await bot.ReadByteAsync(facingPtr);
            string macro = GetSpinMacro(facing);
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

        private string GetSpinMacro(byte facing)
        {
            if (cachedMacros.ContainsKey(facing))
            {
                return cachedMacros[facing];
            }
            else
            {
                while (facing != directions.Peek())
                {
                    directions.Enqueue(directions.Dequeue());
                }
                directions.Enqueue(directions.Dequeue());
                StringBuilder sb = new StringBuilder();

                for (int i = 0; i < 4; i++)
                {
                    if (i != 0)
                    {
                        sb.Append(";");
                    }
                    sb.Append("T.");
                    byte dir = directions.Dequeue();
                    if (dir == 0)
                    {
                        sb.Append("Up");
                    }
                    else if (dir == 1)
                    {
                        sb.Append("Down");
                    }
                    else if (dir == 2)
                    {
                        sb.Append("Left");
                    }
                    else if (dir == 3)
                    {
                        sb.Append("Right");
                    }
                    sb.Append(";W.5");
                    directions.Enqueue(dir);
                }
                string macro = sb.ToString();
                cachedMacros.Add(facing, macro);
                return macro;
            }
        }
    }
}
