using Microsoft.AspNetCore.SignalR;
using ShinyBotHunter.BotEvents.Target;
using ShinyBotHunter.Games;
using ShinyBotHunter.Milestones;
using ShinyBotHunter.Persistence.MongoDb;

namespace ShinyBotHunter.Service
{
    public class NotificationService : INotify<EncounterNotification, CacheLoadRequest, GameEncounterResponse>
    {
        IHubContext<ElectronHub, IElectronClient> hub;
        private PhaseInfoService phaseInfoService;
        // The three below are set once in the constructor and are thread-safe for concurrent use.
        private readonly RomInfoService romInfoService;
        private readonly GameProfiles profiles;
        private readonly MongoGameFileDao gameFileDao;
        public NotificationService(PokemonPersistenceService service, IHubContext<ElectronHub, IElectronClient> hub, PhaseInfoService phaseInfoService,
            RomInfoService romInfoService, GameProfiles profiles, MongoGameFileDao gameFileDao)
        {
            service.SubscribeToNotifications(this);
            this.hub = hub;
            this.phaseInfoService = phaseInfoService;
            this.romInfoService = romInfoService;
            this.profiles = profiles;
            this.gameFileDao = gameFileDao;
        }

        public void Init()
        {

        }

        public async Task<CacheLoadRequest> BuildNotificationRequestAsync(EncounterNotification message)
        {
            var phaseInfo = phaseInfoService.GetPhaseInfo(message.game, message.phase);
            if (phaseInfo is null)
            {
                // A generated milestone: the keys of its rows through the file's goal, read once per pushed batch.
                var milestone = PhaseTarget.FindMilestone(profiles.Get(message.game).Milestones, romInfoService.GetByHandle(message.gameFileName)?.Version, message.phase)
                    ?? throw new InvalidOperationException($"{message.game}:{message.phase} is neither a phase nor a milestone of {message.gameFileName}");
                var rows = MilestoneRows.Build(milestone, await gameFileDao.LoadAsync(message.gameFileName), []);
                return new CacheLoadRequest(message.game, rows.Select(r => r.Key).ToList(), [message.phase], [message.gameFileName]);
            }
            return new CacheLoadRequest(message.game, phaseInfo.Pokemon.Select(x => x.RequireSpecificForm ? x.FormCode : CacheLoadRequest.SpeciesKey(x.SpeciesId)).ToList(), new List<string>() { phaseInfo.Name }, new List<string>() { message.gameFileName });
        }

        public async Task NotifyAsync(GameEncounterResponse response, EncounterNotification message)
        {
            try
            {
                Console.WriteLine($"we get here {message.game.ToString()}_{message.phase}");
                await hub.Clients.Group($"{message.game.ToString()}_{message.phase}").RecieveEncounters(response);
            }catch(Exception e)
            {
                Console.WriteLine(e.ToString());
                throw;
            }
        }
    }
}
