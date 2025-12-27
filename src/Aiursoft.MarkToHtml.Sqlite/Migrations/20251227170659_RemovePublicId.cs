using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.MarkToHtml.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class RemovePublicId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublicId",
                table: "MarkdownDocuments");

            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "MarkdownDocuments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "MarkdownDocuments");

            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                table: "MarkdownDocuments",
                type: "TEXT",
                nullable: true);
        }
    }
}
