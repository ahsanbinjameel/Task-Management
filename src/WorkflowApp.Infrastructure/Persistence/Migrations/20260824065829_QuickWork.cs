using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkflowApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuickWork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuickWork",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ClientId = table.Column<long>(type: "bigint", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    InterruptedTaskId = table.Column<long>(type: "bigint", nullable: true),
                    PromotedToRequestId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuickWork", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuickWork_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_QuickWork_Requests_PromotedToRequestId",
                        column: x => x.PromotedToRequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuickWork_Tasks_InterruptedTaskId",
                        column: x => x.InterruptedTaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QuickWork_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuickWork_ClientId",
                table: "QuickWork",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_QuickWork_InterruptedTaskId",
                table: "QuickWork",
                column: "InterruptedTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_QuickWork_PromotedToRequestId",
                table: "QuickWork",
                column: "PromotedToRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_QuickWork_UserId_StartedAt",
                table: "QuickWork",
                columns: new[] { "UserId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_QuickWork_OneActivePerUser",
                table: "QuickWork",
                column: "UserId",
                unique: true,
                filter: "[Status] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuickWork");
        }
    }
}
