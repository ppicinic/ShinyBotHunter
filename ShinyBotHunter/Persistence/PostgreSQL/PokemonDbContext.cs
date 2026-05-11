using Microsoft.EntityFrameworkCore;

namespace ShinyBotHunter.Persistence.PostgreSQL
{
    public class PokemonDbContext : DbContext
    {
        public DbSet<PersistPokemonDataDao> Encounters { get; set; }
        public PokemonDbContext(DbContextOptions<PokemonDbContext> options)
            : base(options)
        {
        }
    }
}
