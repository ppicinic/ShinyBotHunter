using Microsoft.Extensions.Options;
using System.Text;

namespace ShinyBotHunter.Emulators
{
    public class BizHawkLuaLoader
    {
        private string luaDirectory;
        public BizHawkLuaLoader(IOptions<ApplicationSettings> appSettings)
        {
            luaDirectory = appSettings.Value.LuaBaseDirectory;
        }
       
        public async Task InitAsync()
        {
            string path = AppContext.BaseDirectory;
            if (!File.Exists($"{path}/Loader.lua"))
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"dofile(\"{luaDirectory}CommandServer.lua\")");
                sb.AppendLine($"dofile(\"{luaDirectory}MemoryServer.lua\")");
                await File.WriteAllTextAsync($"{path}/Loader.lua", sb.ToString());
            }
        }
    }
}
