using ShinyBotHunter.Emulators;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Bot
{
    /// <summary>
    /// Ruby/Sapphire game-state reads.
    ///
    /// Simpler than the Gen 4 equivalent because Gen 3 does not relocate its save block: every
    /// address here is absolute, with no anchor to dereference first.
    ///
    /// Only what the starter hunt needs is mapped. The overworld reads that Gen 4 uses for pathfinding
    /// have no Gen 3 caller yet, and rather than guess at addresses that nothing exercises, they throw
    /// saying so -- a wrong coordinate would not fail loudly, it would just walk the player somewhere
    /// unexpected.
    /// </summary>
    public class RsDataReader : IDataReader
    {
        private readonly IMemoryClient memory;
        private readonly RsPointerService addresses;

        public RsDataReader(IMemoryClient memoryClient, RsPointerService addresses)
        {
            this.memory = memoryClient;
            this.addresses = addresses;
        }

        /// <summary>Non-zero once a battle is under way.</summary>
        public async Task<byte> GetBattleState()
        {
            uint flags = await memory.ReadUIntAsync(addresses.BattleMenuState);
            return (byte)(flags != 0 ? 1 : 0);
        }

        public Task<byte> GetBattleMenuState(CancellationToken ct = default) => throw new NotSupportedException(Unmapped(nameof(GetBattleMenuState)));

        public Task<byte> GetBattleValue(CancellationToken ct = default) => throw new NotSupportedException(Unmapped(nameof(GetBattleValue)));

        public Task<ushort> GetMapId() => throw new NotSupportedException(Unmapped(nameof(GetMapId)));

        public Task<short> GetTrainerXPos() => throw new NotSupportedException(Unmapped(nameof(GetTrainerXPos)));

        public Task<short> GetTrainerYPos() => throw new NotSupportedException(Unmapped(nameof(GetTrainerYPos)));

        public Task<byte> GetFacing() => throw new NotSupportedException(Unmapped(nameof(GetFacing)));

        private static string Unmapped(string member) =>
            $"{member} is not mapped for Ruby/Sapphire. Only the reads the starter hunt needs are " +
            "implemented; the overworld and battle-menu reads have no Gen 3 caller yet.";
    }
}
