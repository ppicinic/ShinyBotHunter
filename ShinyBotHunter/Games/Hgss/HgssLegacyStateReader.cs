using ShinyBotHunter.Emulators;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Games.Hgss
{
    /// <summary>
    /// Classifies the screen from the three bytes the bot has always read: the battle indicator, the
    /// battle-state byte and the menu byte. It returns only FieldFree, BattleMenu, BattleTurn and
    /// Evolution. It never returns Transitioning, FieldTask, OtherApp, BattleStarting, BattleMoveList,
    /// BattleMessage, BattleScript, BattleExpPrompt, BattleMoveLearnPrompt or BattleOver, and the
    /// snapshot's Outcome, Script, Prompt and TaskFunction are always null. The chain reader in
    /// docs/specs/hgss-state-reader.md replaces it.
    /// </summary>
    public class HgssLegacyStateReader : IGameStateReader
    {
        private readonly IMemoryClient memory;
        private readonly HgssPointerService addresses;

        public HgssLegacyStateReader(IMemoryClient memory, HgssPointerService addresses)
        {
            this.memory = memory;
            this.addresses = addresses;
        }

        // The reads stop as soon as the screen is decided, so a poll on the field costs one request, as
        // the encounter loops' indicator read always has. The two bytes behind the anchor use the
        // ViaPointer reads: reading the anchor and then reading through it as two calls put a frame
        // between them, so the pointer could already have moved; the ViaPointer reads do both hops
        // inside one emulator frame.
        public async Task<GameSnapshot> ReadAsync(CancellationToken ct)
        {
            byte indicator = (await memory.ReadBytesAsync(addresses.BattleIndicator, 1, ct))[0];
            if (indicator is not (0x41 or 0x97 or 0xC0))
            {
                return Snapshot(GameScreen.FieldFree);
            }
            if (await memory.ReadByteViaPointerAsync(addresses.Anchor, addresses.BattleStateOffset) == 14)
            {
                return Snapshot(GameScreen.Evolution);
            }
            byte menu = (await memory.ReadBytesViaPointerAsync(addresses.Anchor, 1, ct, (int)addresses.BattleMenuState))[0];
            return Snapshot(menu == 1 ? GameScreen.BattleMenu : GameScreen.BattleTurn);
        }

        private static GameSnapshot Snapshot(GameScreen screen) => new(screen, null, null, null, null);
    }
}
