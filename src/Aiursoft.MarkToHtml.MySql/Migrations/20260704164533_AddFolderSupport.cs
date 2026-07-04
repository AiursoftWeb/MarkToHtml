using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.MarkToHtml.MySql.Migrations
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
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MarkdownDocumentFolders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ParentFolderId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreateTime = table.Column<DateTime>(type: "datetime(6)", nullable: false)
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
                        name: "FK_MarkdownDocumentFolders_MarkdownDocumentFolders_ParentFolder~",
                        column: x => x.ParentFolderId,
                        principalTable: "MarkdownDocumentFolders",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

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
