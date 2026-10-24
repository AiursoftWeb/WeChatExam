using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddPaperCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LegacyPaperCollectionCatalogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TaxonomyId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyPaperCollectionCatalogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegacyPaperCollectionCatalogs_Taxonomies_TaxonomyId",
                        column: x => x.TaxonomyId,
                        principalTable: "Taxonomies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PaperCollections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    IsVisible = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsArchived = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Revision = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperCollections", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LegacyPaperCollectionAliases",
                columns: table => new
                {
                    NormalizedName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CollectionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyPaperCollectionAliases", x => x.NormalizedName);
                    table.ForeignKey(
                        name: "FK_LegacyPaperCollectionAliases_PaperCollections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "PaperCollections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LegacyPaperCollectionBindings",
                columns: table => new
                {
                    CollectionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    TagId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyPaperCollectionBindings", x => x.CollectionId);
                    table.ForeignKey(
                        name: "FK_LegacyPaperCollectionBindings_PaperCollections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "PaperCollections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LegacyPaperCollectionBindings_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PaperCollectionPapers",
                columns: table => new
                {
                    CollectionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    PaperId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperCollectionPapers", x => new { x.CollectionId, x.PaperId });
                    table.ForeignKey(
                        name: "FK_PaperCollectionPapers_PaperCollections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "PaperCollections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaperCollectionPapers_Papers_PaperId",
                        column: x => x.PaperId,
                        principalTable: "Papers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyPaperCollectionAliases_CollectionId",
                table: "LegacyPaperCollectionAliases",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyPaperCollectionBindings_TagId",
                table: "LegacyPaperCollectionBindings",
                column: "TagId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegacyPaperCollectionCatalogs_TaxonomyId",
                table: "LegacyPaperCollectionCatalogs",
                column: "TaxonomyId");

            migrationBuilder.CreateIndex(
                name: "IX_PaperCollectionPapers_PaperId",
                table: "PaperCollectionPapers",
                column: "PaperId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LegacyPaperCollectionAliases");

            migrationBuilder.DropTable(
                name: "LegacyPaperCollectionBindings");

            migrationBuilder.DropTable(
                name: "LegacyPaperCollectionCatalogs");

            migrationBuilder.DropTable(
                name: "PaperCollectionPapers");

            migrationBuilder.DropTable(
                name: "PaperCollections");
        }
    }
}
