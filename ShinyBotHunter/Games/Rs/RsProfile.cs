using ShinyBotHunter.BotEvents;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;
using ShinyBotHunter.Service;
using ShinyBotHunter.Service.Pointers;

namespace ShinyBotHunter.Games.Rs
{
    public static class RsProfile
    {
        private static readonly RsPointerService Table = new();

        private static IPointerService Addresses(string? gameCode, int revision) => revision is 1 or 2
            ? RsPointerService.For(gameCode)
            : throw new NotSupportedException($"RS revision {revision}: no address table. Hunts run on revisions 1 and 2.");

        public static GameProfile Create(PokemonMetadataService metadata) => new(
            GameFlavor.RS,
            new GameCapabilities(
                HasOverworldNavigation: false,
                HasWildEncounters: true,
                HasTraining: false,
                HasEggs: false,
                CanReadBoxes: true,
                HasSoftResetStarterHunt: true,
                HasShinyStarterHunt: false,
                HasWishmakerJirachi: true),
            revisions: [1, 2],
            Addresses,
            new Gen3PokemonParser(metadata),
            battleParser: null,
            createOverworldReader: (memory, table) => new RsOverworldReader(memory, (RsPointerService)table),
            createStateReader: (memory, table) => new RsStateReader(memory, (RsPointerService)table),
            createBattleEvent: provider => new RsBattleEvent(provider.GetRequiredService<PokemonPersistenceService>(), provider.GetRequiredService<ShinyAlertService>(), provider.GetRequiredService<LegalityService>(),
                metadata, p => provider.GetRequiredService<SmogonDamageService>().PickBestMove(p, 3),
                (p, m) => provider.GetRequiredService<DiscordBotService>().SendNewMoveDecisionAndWaitAsync(p, m)),
            saveGame: (provider, bot) =>
            {
                // The starter hunt's save step and its values (FR-13); loaded per save, as the hunt loads it per start.
                StarterHuntConfig config = StarterHuntConfig.Load();
                return provider.GetRequiredService<RsStartMenuService>().SaveViaStartMenuAsync(bot, Table,
                    config.SaveMenuActionId, config.SaveConfirmMacro, config.BattleWaitTimeoutMs, config.BattlePollIntervalMs, CancellationToken.None);
            },
            // Above both wild transitions from the lock to the battle bit: the short one, about 1.4 s
            // (docs/research/rs-wild-encounter.md, "What memory shows through one cycle"; 1.6 to 2.1 s in the QA logs), and
            // the long one a lead at or below the wild level gets, 152 frames, about 2.7 to 3.2 s with polling ("Reaching 304
            // an hour"). A trainer's speech waits unpressed from 148 to 180 frames on (docs/research/rs-navigation-and-training.md,
            // "A trainer's intro speech"), so the later read costs nothing. A transition that ever crossed the hand-off costs
            // one one-frame read, false because the script status is 2, and the battle then goes on as normal.
            seekLockHandOff: TimeSpan.FromSeconds(4));
    }
}
