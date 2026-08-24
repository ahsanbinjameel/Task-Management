using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkflowApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PauseCategoryAndAwayState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AwayState",
                table: "PauseReasons",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Category",
                table: "PauseReasons",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Rows seeded before these columns existed would otherwise all land on category 0
            // ("other work became urgent") with no away-state, which would mean the system stopped
            // recording that someone was at lunch. The seeder cannot fix them -- it only runs when
            // the table is empty -- so the backfill belongs here.
            //
            // Matched on the names the seeder used. Anything else a site added by hand keeps the
            // default, which is the safe reading: a reason we know nothing about does not move the
            // person anywhere.
            //
            // Category:      OtherWorkUrgent 0, WaitingForSomeone 1, WaitingForClient 2,
            //                CannotContinue 3, Meeting 4, Break 5, Lunch 6, EndOfShift 7, Other 8
            // WorkforceState: Break 4, Lunch 5, Meeting 6
            migrationBuilder.Sql(@"
UPDATE [PauseReasons] SET [Category] = 5, [AwayState] = 4 WHERE [Name] = N'Break';
UPDATE [PauseReasons] SET [Category] = 6, [AwayState] = 5 WHERE [Name] = N'Lunch';
UPDATE [PauseReasons] SET [Category] = 4, [AwayState] = 6 WHERE [Name] = N'Meeting';
UPDATE [PauseReasons] SET [Category] = 7 WHERE [Name] = N'End of shift';
UPDATE [PauseReasons] SET [Category] = 0 WHERE [Name] = N'Switched to higher priority task';
UPDATE [PauseReasons] SET [Category] = 2 WHERE [Name] = N'Waiting for client response';
UPDATE [PauseReasons] SET [Category] = 1 WHERE [Name] IN (N'Waiting for another team', N'Awaiting clarification');
UPDATE [PauseReasons] SET [Category] = 3 WHERE [Name] IN (N'Blocked by dependency', N'Environment or access issue');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AwayState",
                table: "PauseReasons");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "PauseReasons");
        }
    }
}
