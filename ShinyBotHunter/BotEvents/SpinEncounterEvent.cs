using DnsClient;
using ShinyBotHunter.Bot;
using ShinyBotHunter.Util;
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

        public override async Task<bool> ExecuteWithResultAsync(GameState state, CancellationToken cancellationToken)
        {
            // Spinning resumes straight after a battle, when the field is still fading in and the
            // facing byte has not settled. Reading it early picks a rotation that starts from the
            // wrong direction, so wait for the overworld the same way navigation does.
            if (!await Overworld.WaitForReadyAsync(bot, cancellationToken))
            {
                Console.WriteLine("[Spin] Overworld coordinates never settled; spinning from the current facing anyway.");
            }

            byte facing = await bot.GetFacing();
            string macro = GetSpinMacro(facing);
            Console.WriteLine($"[Spin] facing={facing} macro={macro}");
            bot.RunRepeatMacro(macro);

            uint battleIndicator = 0x21E76D2;
            var battleValue = await bot.ReadByteAsync(battleIndicator);
            while (!(battleValue == 0x41 || battleValue == 0x97 || battleValue == 0xC0) && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(Constant.PollIntervalMs);
                battleValue = await bot.ReadByteAsync(battleIndicator);
            }
            bot.ClearMacro();
            await bot.WaitForMacroAsync();

            bool inBattle = battleValue == 0x41 || battleValue == 0x97 || battleValue == 0xC0;
            if (inBattle)
            {
                Console.WriteLine("Found battle");
                // The orchestrator expects the encounter event to advance the state itself, the same
                // way RunEncounterEvent does. Returning the flag alone left Encountering on top of
                // the stack, so the next loop spun again on top of the battle instead of fighting it.
                state.State.Pop();
                // TODO we should go into Waiting if its a non wild battle
                state.State.Push(EventState.BattleStart);
            }
            return inBattle;
        }

        private string GetSpinMacro(byte facing)
        {
            if (cachedMacros.ContainsKey(facing))
            {
                return cachedMacros[facing];
            }
            else
            {
                // Align the cycle to the direction we're actually facing. Bounded by the queue length:
                // a facing value outside 0-3 -- a read that caught the field mid-load -- would never
                // match and used to spin here forever, hanging the run with nothing in the log.
                for (int rotations = 0; rotations < directions.Count && facing != directions.Peek(); rotations++)
                {
                    directions.Enqueue(directions.Dequeue());
                }
                if (facing != directions.Peek())
                {
                    Console.WriteLine($"[Spin] Unexpected facing value {facing}; starting the rotation at {directions.Peek()}.");
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
