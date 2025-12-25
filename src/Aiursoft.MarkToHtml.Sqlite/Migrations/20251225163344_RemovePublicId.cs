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
                name: "AllowAnonymousView",
                table: "MarkdownDocuments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowAnonymousView",
                table: "MarkdownDocuments");

            migrationBuilder.AddColumn<Guid>(
                name: "PublicId",
                table: "MarkdownDocuments",
                type: "TEXT",
                nullable: true);
        }
    }
}
