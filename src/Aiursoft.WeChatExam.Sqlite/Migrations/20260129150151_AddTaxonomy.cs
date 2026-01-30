using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddTaxonomy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TaxonomyId",
                table: "Tags",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Taxonomies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Taxonomies", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tags_TaxonomyId",
                table: "Tags",
                column: "TaxonomyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tags_Taxonomies_TaxonomyId",
                table: "Tags",
                column: "TaxonomyId",
                principalTable: "Taxonomies",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tags_Taxonomies_TaxonomyId",
                table: "Tags");

            migrationBuilder.DropTable(
                name: "Taxonomies");

            migrationBuilder.DropIndex(
                name: "IX_Tags_TaxonomyId",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "TaxonomyId",
                table: "Tags");
        }
    }
}
