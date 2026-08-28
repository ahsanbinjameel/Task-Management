using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkflowApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequestRound : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default 1, not 0: every request that already exists was raised on its own, which is
            // round one. Leaving them at zero would make the very first thing anybody typed look
            // like a round that came before the beginning.
            migrationBuilder.AddColumn<int>(
                name: "Round",
                table: "Requests",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Round",
                table: "Requests");
        }
    }
}
