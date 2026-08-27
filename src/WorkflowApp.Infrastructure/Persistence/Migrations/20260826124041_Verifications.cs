using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkflowApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Verifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "VerificationId",
                table: "Attachments",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Verifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VerificationNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Instructions = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ExpectedBehavior = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RequestId = table.Column<long>(type: "bigint", nullable: true),
                    TargetType = table.Column<int>(type: "int", nullable: false),
                    ModuleId = table.Column<long>(type: "bigint", nullable: true),
                    TargetName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TargetReference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RequestedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AssignedToUserId = table.Column<long>(type: "bigint", nullable: true),
                    AssignedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    AssignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Result = table.Column<int>(type: "int", nullable: true),
                    Findings = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Verifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Verifications_Modules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "Modules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Verifications_Requests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "Requests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Verifications_Users_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Verifications_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VerificationActivities",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VerificationId = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<long>(type: "bigint", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "varbinary(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VerificationActivities_Verifications_VerificationId",
                        column: x => x.VerificationId,
                        principalTable: "Verifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_VerificationId",
                table: "Attachments",
                column: "VerificationId");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationActivities_VerificationId_OccurredAt_Id",
                table: "VerificationActivities",
                columns: new[] { "VerificationId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Verifications_AssignedToUserId_Status",
                table: "Verifications",
                columns: new[] { "AssignedToUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Verifications_ModuleId",
                table: "Verifications",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Verifications_RequestedByUserId",
                table: "Verifications",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Verifications_RequestId",
                table: "Verifications",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_Verifications_Status",
                table: "Verifications",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Verifications_VerificationNumber",
                table: "Verifications",
                column: "VerificationNumber",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Attachments_Verifications_VerificationId",
                table: "Attachments",
                column: "VerificationId",
                principalTable: "Verifications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attachments_Verifications_VerificationId",
                table: "Attachments");

            migrationBuilder.DropTable(
                name: "VerificationActivities");

            migrationBuilder.DropTable(
                name: "Verifications");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_VerificationId",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "VerificationId",
                table: "Attachments");
        }
    }
}
