using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShinyBotHunter.Persistence.PostgreSQL;

#nullable disable

namespace ShinyBotHunter.Migrations
{
    [DbContext(typeof(PokemonDbContext))]
    [Migration("20261021120000_EncountersCountsIndex")]
    public partial class EncountersCountsIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "encounters_game_milestone_file_time",
                table: "encounters",
                columns: new[] { "Game", "Milestone", "GameFileName", "Timestamp" },
                descending: new[] { false, false, false, true })
                .Annotation("Npgsql:IndexInclude", new[] { "IsShiny", "FormCode", "Species" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "encounters_game_milestone_file_time",
                table: "encounters");
        }
    }
}
