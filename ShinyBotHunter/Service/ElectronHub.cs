using Microsoft.AspNetCore.SignalR;
using ShinyBotHunter.Macro;
using ShinyBotHunter.Pokemon;

namespace ShinyBotHunter.Service
{
    public class ElectronHub : Hub<IElectronClient>
    {
        private AdminService adminService;
        public ElectronHub(AdminService adminService)
        {
            this.adminService = adminService;
        }

        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();
            Console.WriteLine("SignalR Client Connected.");
            await adminService.UpdateDashboardsAsync();
            Console.WriteLine("Updated Dashboards Completed.");
        }

        public async Task SendPokemonEncounterMessageAsync(GameEncounterResponse pokemon, GameFlavor game, string phase)
        {
            await Clients.Group(BuildGroupName(game, phase)).RecieveEncounters(pokemon);
        }

        public async Task SubscribeToGamePhase(string game, string phase)
        {
            try
            {
                var groupname = BuildGroupName(game, phase);
                Console.WriteLine(groupname);
                await Groups.AddToGroupAsync(Context.ConnectionId, groupname);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                throw;
            }
        }

        private string BuildGroupName(GameFlavor game, string phase) => BuildGroupName(game.ToString(), phase);
        private string BuildGroupName(string game, string phase) => $"{game}_{phase}";
    }
}
