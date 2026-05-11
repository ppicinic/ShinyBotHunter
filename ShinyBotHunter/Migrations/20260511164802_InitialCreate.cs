using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShinyBotHunter.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PID = table.Column<long>(type: "bigint", nullable: false),
                    Species = table.Column<int>(type: "integer", nullable: false),
                    Gender = table.Column<byte>(type: "smallint", nullable: false),
                    FormCode = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    TID = table.Column<int>(type: "integer", nullable: false),
                    SID = table.Column<int>(type: "integer", nullable: false),
                    Ability = table.Column<byte>(type: "smallint", nullable: false),
                    IVs = table.Column<long>(type: "bigint", nullable: false),
                    Nature = table.Column<byte>(type: "smallint", nullable: false),
                    Level = table.Column<byte>(type: "smallint", nullable: false),
                    ShinyValue = table.Column<int>(type: "integer", nullable: false),
                    IsShiny = table.Column<bool>(type: "boolean", nullable: false),
                    Game = table.Column<int>(type: "integer", nullable: false),
                    Milestone = table.Column<string>(type: "text", nullable: false),
                    GameFileName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_encounters", x => new { x.Id, x.Timestamp });
                });

            migrationBuilder.Sql(@"
                SELECT create_hypertable('public.""encounters""', 'Timestamp', chunk_time_interval => INTERVAL '1 day');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "encounters");
        }
    }
}
