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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<PersistPokemonDataDao>()
                .HasIndex(x => new { x.Game, x.Milestone, x.GameFileName, x.Timestamp })
                .HasDatabaseName("encounters_game_milestone_file_time")
                .IsDescending(false, false, false, true)
                .IncludeProperties(x => new { x.IsShiny, x.FormCode, x.Species });
        }
    }
}
