using ShinyBotHunter.Emulators;
using ShinyBotHunter.Games;
using ShinyBotHunter.Service.Pointers;
using ShinyBotHunter.Util;
using System.Diagnostics;

namespace ShinyBotHunter.BotEvents
{
    /// <summary>
    /// Soft-reset hunt for a shiny from Primo's egg in HGSS (docs/specs/primo-egg-hunt.md,
    /// docs/research/primo-slugma-egg.md): the password from the trainer ID, Primo's two easy-chat prompts, the walk
    /// to the pacing line, the shared hatch step, then save on a shiny or soft reset and go again.
    /// </summary>
    public class HgssPrimoEggEvent
    {
        // ScriptEnvironment's special variables from 0x8000; 0x800C, the script's result, is index 12.
        private const int SpecialVarCount = 14;
        private const int ResultVar = 12;

        // -- Steps, internal for the tests --------------------------------------------------------

        // The visible trainer ID, [[FieldSystem + 0x0C] + 0x84] (read table R31).
        internal static async Task<ushort> ReadTrainerIdAsync(IMemoryClient memory, HgssPointerService addresses, CancellationToken ct)
        {
            ChainBlockResult read = (await memory.ReadChainBlocksAsync(
                new[] { new ChainBlockRequest(addresses.Anchor, new[] { addresses.SaveDataOffset, addresses.TrainerIdOffset }, 2) }, ct: ct))[0];
            if (read.Status != ChainBlockStatus.Ok)
                throw new InvalidOperationException($"[PrimoEgg] the trainer ID read failed: {read.Status} at hop {read.Hop}");
            return BitConverter.ToUInt16(read.Bytes, 0);
        }

        // The script's special variables 0x8000 to 0x800D, from the running script's environment (read table R33).
        internal static async Task<ushort[]> ReadSpecialVarsAsync(IMemoryClient memory, HgssPointerService addresses, CancellationToken ct)
        {
            ChainBlockResult read = (await memory.ReadChainBlocksAsync(new[]
            {
                new ChainBlockRequest(addresses.Anchor,
                    new[] { addresses.TaskManagerOffset, addresses.ScriptEnvironmentOffset, addresses.ScriptSpecialVarsOffset }, 2 * SpecialVarCount),
            }, ct: ct))[0];
            if (read.Status != ChainBlockStatus.Ok)
                throw new InvalidOperationException($"[PrimoEgg] the script's special variables read failed: {read.Status} at hop {read.Hop}");
            ushort[] vars = new ushort[SpecialVarCount];
            for (int i = 0; i < SpecialVarCount; i++) vars[i] = BitConverter.ToUInt16(read.Bytes, 2 * i);
            return vars;
        }

        // Prompt 1 writes 0x8000 and 0x8001, prompt 2 writes 0x8002 and 0x8003; both must hold the words entered.
        internal static void CheckWords(ushort[] vars, int prompt, ushort[] password)
        {
            int first = 2 * (prompt - 1);
            if (vars[first] == password[first] && vars[first + 1] == password[first + 1]) return;
            throw new InvalidOperationException(
                $"after prompt {prompt}, 0x{0x8000 + first:X4} and 0x{0x8001 + first:X4} read {vars[first]}, {vars[first + 1]}; " +
                $"entered {password[first]}, {password[first + 1]}");
        }

        // 0x800C after the password check (scr_seq_0860_T22PC0101.s): 1 to 5 is the party count after the egg; anything
        // else is why there is no egg. The already-received branch leaves the egg type in 0x800C, so it is the config's egg.
        internal static string? Refusal(ushort result, int egg)
        {
            if (result is >= 1 and <= 5) return null;
            if (result is 255 or 0) return "Primo refused the password";
            if (result == egg) return "this save already received the egg (FLAG_GOT_SLUGMA_EGG_FROM_PRIMO is set)";
            if (result == 6) return "the party is full";
            return $"Primo's script ended with result {result}";
        }

        // Where Primo's text ends: the easy-chat app has replaced the field.
        internal static bool EasyChatOpened(GameSnapshot snapshot) => snapshot.Screen == GameScreen.OtherApp;

        // Where the text after a prompt is stopped: the next message, before any press. The variables are only certain to
        // be written once the script has moved on, and on the egg-given path 0x800C holds the party count only until
        // CloseMsg after message 18, the first message after prompt 2.
        internal static Func<GameSnapshot, bool> AtMessage(HgssPointerService addresses) => s => s.Native == addresses.PrintingNative;

        // Null on the config's start tile and facing, otherwise where the player is.
        internal static async Task<string?> WrongPlaceAsync(IOverworldReader overworld, PrimoEggConfig config, CancellationToken ct)
        {
            ushort map = await overworld.GetMapIdAsync(ct);
            (short x, short y) = await overworld.GetPositionAsync(ct);
            Direction facing = await overworld.GetFacingAsync(ct);
            if (map == config.StartMapId && x == config.StartX && y == config.StartY && facing == config.StartFacingDirection) return null;
            return $"map {map}, tile ({x}, {y}), facing {facing.ToString().ToLowerInvariant()}";
        }

        // FR-2's sentence, from the config.
        internal static string StandFacingPrimo(PrimoEggConfig config) =>
            $"Stand facing Primo in the Violet City Pokémon Center: map {config.StartMapId}, tile ({config.StartX}, {config.StartY}), " +
            $"facing {config.StartFacingDirection.ToString().ToLowerInvariant()}.";

        // One route leg as an M command, the pace macro's move: hold the direction until the coordinate reads the value.
        internal static string LegMacro((char Axis, short Value) leg, short x, short y, HgssPointerService addresses)
        {
            if (leg.Axis == 'x') return $"M.{(leg.Value < x ? "Left" : "Right")}.{leg.Value}.{addresses.TrainerX}";
            return $"M.{(leg.Value < y ? "Up" : "Down")}.{leg.Value}.{addresses.TrainerY}";
        }

        // Runs one leg and confirms it by position. Throws, after clearing the macro, when the map and tile have not
        // changed for stall.
        internal static async Task WalkLegAsync(
            IOverworldReader overworld, ICommandClient commands, HgssPointerService addresses, (char Axis, short Value) leg, TimeSpan stall,
            CancellationToken ct)
        {
            ushort map = await overworld.GetMapIdAsync(ct);
            (short x, short y) = await overworld.GetPositionAsync(ct);
            commands.RunMacroString(LegMacro(leg, x, y, addresses));
            Stopwatch still = Stopwatch.StartNew();
            while ((leg.Axis == 'x' ? x : y) != leg.Value)
            {
                if (still.Elapsed >= stall)
                {
                    commands.ClearMacro();
                    throw new InvalidOperationException($"Route leg {leg.Axis}:{leg.Value} stopped at map {map}, tile ({x}, {y})");
                }
                await Task.Delay(Constant.PollIntervalMs, ct);
                ushort nextMap = await overworld.GetMapIdAsync(ct);
                (short nextX, short nextY) = await overworld.GetPositionAsync(ct);
                if ((nextMap, nextX, nextY) != (map, x, y)) still.Restart();
                (map, x, y) = (nextMap, nextX, nextY);
            }
            await commands.WaitForMacroAsync(ct);
            Console.WriteLine($"[PrimoEgg] leg {leg.Axis}:{leg.Value} reached at map {map} ({x}, {y})");
        }
    }
}
