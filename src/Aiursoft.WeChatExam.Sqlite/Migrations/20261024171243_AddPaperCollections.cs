using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.Sqlite.Migrations
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
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TaxonomyId = table.Column<int>(type: "INTEGER", nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "PaperCollections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    OrderIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    IsVisible = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    Revision = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperCollections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LegacyPaperCollectionAliases",
                columns: table => new
                {
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    CollectionId = table.Column<Guid>(type: "TEXT", nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "LegacyPaperCollectionBindings",
                columns: table => new
                {
                    CollectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "PaperCollectionPapers",
                columns: table => new
                {
                    CollectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PaperId = table.Column<Guid>(type: "TEXT", nullable: false)
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
                });

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
