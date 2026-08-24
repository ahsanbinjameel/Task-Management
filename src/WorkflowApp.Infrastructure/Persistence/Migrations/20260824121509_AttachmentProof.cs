using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkflowApp.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AttachmentProof : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "Attachments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "QCReviewId",
                table: "Attachments",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_QCReviewId",
                table: "Attachments",
                column: "QCReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_TaskId_Kind",
                table: "Attachments",
                columns: new[] { "TaskId", "Kind" });

            migrationBuilder.AddForeignKey(
                name: "FK_Attachments_QCReviews_QCReviewId",
                table: "Attachments",
                column: "QCReviewId",
                principalTable: "QCReviews",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attachments_QCReviews_QCReviewId",
                table: "Attachments");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_QCReviewId",
                table: "Attachments");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_TaskId_Kind",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "QCReviewId",
                table: "Attachments");
        }
    }
}
