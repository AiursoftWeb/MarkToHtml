using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.MarkToHtml.MySql.Migrations
{
    /// <inheritdoc />
    public partial class UpdateDocumentTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DocumentType",
                table: "MarkdownDocuments",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocumentType",
                table: "MarkdownDocuments");
        }
    }
}
