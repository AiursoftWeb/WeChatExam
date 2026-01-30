using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.MySql.Migrations
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
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Taxonomies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreationTime = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Taxonomies", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

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
