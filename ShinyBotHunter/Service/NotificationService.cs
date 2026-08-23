using Microsoft.AspNetCore.SignalR;

namespace ShinyBotHunter.Service
{
    public class NotificationService : INotify<EncounterNotification, CacheLoadRequest, GameEncounterResponse>
    {
        IHubContext<ElectronHub, IElectronClient> hub;
        private PhaseInfoService phaseInfoService;
        public NotificationService(PokemonPersistenceService service, IHubContext<ElectronHub, IElectronClient> hub, PhaseInfoService phaseInfoService)
        {
            service.SubscribeToNotifications(this);
            this.hub = hub;
            this.phaseInfoService = phaseInfoService;
        }

        public void Init()
        {

        }

        public Task<CacheLoadRequest> BuildNotificationRequestAsync(EncounterNotification message)
        {
            var phaseInfo = phaseInfoService.GetPhaseInfo(message.game, message.phase);
            return Task.FromResult(new CacheLoadRequest(message.game, phaseInfo.Pokemon.Select(x => x.RequireSpecificForm ? x.FormCode : CacheLoadRequest.SpeciesKey(x.SpeciesId)).ToList(), new List<string>() { phaseInfo.Name }, new List<string>() { message.gameFileName }));
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
