using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkflowApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RequestBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BatchId",
                table: "Requests",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrdinalInBatch",
                table: "Requests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "BatchId",
                table: "Attachments",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RequestBatches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ClientId = table.Column<long>(type: "bigint", nullable: true),
                    RequestedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequestBatches_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RequestBatches_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Requests_BatchId_OrdinalInBatch",
                table: "Requests",
                columns: new[] { "BatchId", "OrdinalInBatch" });

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_BatchId",
                table: "Attachments",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestBatches_BatchNumber",
                table: "RequestBatches",
                column: "BatchNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestBatches_ClientId",
                table: "RequestBatches",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestBatches_RequestedByUserId",
                table: "RequestBatches",
                column: "RequestedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Attachments_RequestBatches_BatchId",
                table: "Attachments",
                column: "BatchId",
                principalTable: "RequestBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Requests_RequestBatches_BatchId",
                table: "Requests",
                column: "BatchId",
                principalTable: "RequestBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attachments_RequestBatches_BatchId",
                table: "Attachments");

            migrationBuilder.DropForeignKey(
                name: "FK_Requests_RequestBatches_BatchId",
                table: "Requests");

            migrationBuilder.DropTable(
                name: "RequestBatches");

            migrationBuilder.DropIndex(
                name: "IX_Requests_BatchId_OrdinalInBatch",
                table: "Requests");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_BatchId",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "OrdinalInBatch",
                table: "Requests");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "Attachments");
        }
    }
}
