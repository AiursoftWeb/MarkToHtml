using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.MarkToHtml.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class RestrictMarkdownDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MarkdownDocumentFolders_AspNetUsers_UserId",
                table: "MarkdownDocumentFolders");

            migrationBuilder.DropForeignKey(
                name: "FK_MarkdownDocuments_AspNetUsers_UserId",
                table: "MarkdownDocuments");

            migrationBuilder.AddColumn<string>(
                name: "UserId1",
                table: "MarkdownDocuments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarkdownDocuments_UserId1",
                table: "MarkdownDocuments",
                column: "UserId1");

            migrationBuilder.AddForeignKey(
                name: "FK_MarkdownDocumentFolders_AspNetUsers_UserId",
                table: "MarkdownDocumentFolders",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MarkdownDocuments_AspNetUsers_UserId",
                table: "MarkdownDocuments",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MarkdownDocuments_AspNetUsers_UserId1",
                table: "MarkdownDocuments",
                column: "UserId1",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MarkdownDocumentFolders_AspNetUsers_UserId",
                table: "MarkdownDocumentFolders");

            migrationBuilder.DropForeignKey(
                name: "FK_MarkdownDocuments_AspNetUsers_UserId",
                table: "MarkdownDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_MarkdownDocuments_AspNetUsers_UserId1",
                table: "MarkdownDocuments");

            migrationBuilder.DropIndex(
                name: "IX_MarkdownDocuments_UserId1",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "UserId1",
                table: "MarkdownDocuments");

            migrationBuilder.AddForeignKey(
                name: "FK_MarkdownDocumentFolders_AspNetUsers_UserId",
                table: "MarkdownDocumentFolders",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

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
