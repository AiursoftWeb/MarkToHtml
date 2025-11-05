using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.MarkToHtml.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class RenameDbContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MarkdownDocuments_AspNetUsers_UserId",
                table: "MarkdownDocuments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_MarkdownDocuments",
                table: "MarkdownDocuments");

            migrationBuilder.RenameTable(
                name: "MarkdownDocuments",
                newName: "Documents");

            migrationBuilder.RenameIndex(
                name: "IX_MarkdownDocuments_UserId",
                table: "Documents",
                newName: "IX_Documents_UserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Documents",
                table: "Documents",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_AspNetUsers_UserId",
                table: "Documents",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_AspNetUsers_UserId",
                table: "Documents");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Documents",
                table: "Documents");

            migrationBuilder.RenameTable(
                name: "Documents",
                newName: "MarkdownDocuments");

            migrationBuilder.RenameIndex(
                name: "IX_Documents_UserId",
                table: "MarkdownDocuments",
                newName: "IX_MarkdownDocuments_UserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_MarkdownDocuments",
                table: "MarkdownDocuments",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_MarkdownDocuments_AspNetUsers_UserId",
                table: "MarkdownDocuments",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
