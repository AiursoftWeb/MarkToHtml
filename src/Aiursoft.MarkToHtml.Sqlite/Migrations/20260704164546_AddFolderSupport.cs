using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.MarkToHtml.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddFolderSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FolderId",
                table: "MarkdownDocuments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MarkdownDocumentFolders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ParentFolderId = table.Column<int>(type: "INTEGER", nullable: true),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarkdownDocumentFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarkdownDocumentFolders_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MarkdownDocumentFolders_MarkdownDocumentFolders_ParentFolderId",
                        column: x => x.ParentFolderId,
                        principalTable: "MarkdownDocumentFolders",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_MarkdownDocuments_FolderId",
                table: "MarkdownDocuments",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_MarkdownDocumentFolders_ParentFolderId_Name_UserId",
                table: "MarkdownDocumentFolders",
                columns: new[] { "ParentFolderId", "Name", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarkdownDocumentFolders_UserId",
                table: "MarkdownDocumentFolders",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_MarkdownDocuments_MarkdownDocumentFolders_FolderId",
                table: "MarkdownDocuments",
                column: "FolderId",
                principalTable: "MarkdownDocumentFolders",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MarkdownDocuments_MarkdownDocumentFolders_FolderId",
                table: "MarkdownDocuments");

            migrationBuilder.DropTable(
                name: "MarkdownDocumentFolders");

            migrationBuilder.DropIndex(
                name: "IX_MarkdownDocuments_FolderId",
                table: "MarkdownDocuments");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "MarkdownDocuments");
        }
    }
}
