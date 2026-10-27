using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class SeparateKnowledgeCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Validate the whole old tree before copying. An unmapped root or cycle needs
            // a reviewed data decision; never silently discard its knowledge associations.
            migrationBuilder.Sql("""
                CREATE TEMPORARY TABLE KnowledgeMigrationGuard (InvalidCount INTEGER NOT NULL CHECK (InvalidCount = 0));
                INSERT INTO KnowledgeMigrationGuard
                WITH RECURSIVE directory_paths AS (
                    SELECT Id, ParentId, Id AS TotalCategoryId FROM Categories WHERE ParentId IS NULL
                    UNION ALL
                    SELECT c.Id, c.ParentId, p.TotalCategoryId FROM Categories c
                    INNER JOIN directory_paths p ON c.ParentId = p.Id
                )
                SELECT COUNT(*) FROM Categories c
                LEFT JOIN directory_paths p ON p.Id = c.Id
                LEFT JOIN TotalCategories t ON t.Id = p.TotalCategoryId
                WHERE p.Id IS NULL OR t.Id IS NULL;
                DROP TABLE KnowledgeMigrationGuard;
                """);

            migrationBuilder.CreateTable(
                name: "KnowledgeCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TotalCategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentKnowledgeCategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsFree = table.Column<bool>(type: "INTEGER", nullable: false),
                    OrderIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeCategories", x => x.Id);
                    table.UniqueConstraint("AK_KnowledgeCategories_Id_TotalCategoryId", x => new { x.Id, x.TotalCategoryId });
                    table.ForeignKey(
                        name: "FK_KnowledgeCategories_TotalCategories_TotalCategoryId",
                        column: x => x.TotalCategoryId,
                        principalTable: "TotalCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeCategoryKnowledgePoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    KnowledgeCategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    KnowledgePointId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrderIndex = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeCategoryKnowledgePoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeCategoryKnowledgePoints_KnowledgeCategories_KnowledgeCategoryId",
                        column: x => x.KnowledgeCategoryId,
                        principalTable: "KnowledgeCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KnowledgeCategoryKnowledgePoints_KnowledgePoints_KnowledgePointId",
                        column: x => x.KnowledgePointId,
                        principalTable: "KnowledgePoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeCategories_ParentKnowledgeCategoryId_TotalCategoryId",
                table: "KnowledgeCategories",
                columns: new[] { "ParentKnowledgeCategoryId", "TotalCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeCategories_TotalCategoryId",
                table: "KnowledgeCategories",
                column: "TotalCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeCategoryKnowledgePoints_KnowledgeCategoryId",
                table: "KnowledgeCategoryKnowledgePoints",
                column: "KnowledgeCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeCategoryKnowledgePoints_KnowledgePointId",
                table: "KnowledgeCategoryKnowledgePoints",
                column: "KnowledgePointId");
            migrationBuilder.Sql("""
                INSERT INTO KnowledgeCategories (Id, Title, TotalCategoryId, ParentKnowledgeCategoryId, IsFree, OrderIndex, CreationTime)
                WITH RECURSIVE directory_paths AS (
                    SELECT Id, ParentId, Id AS TotalCategoryId FROM Categories WHERE ParentId IS NULL
                    UNION ALL
                    SELECT c.Id, c.ParentId, p.TotalCategoryId FROM Categories c
                    INNER JOIN directory_paths p ON c.ParentId = p.Id
                )
                SELECT c.Id, c.Title, p.TotalCategoryId,
                    CASE WHEN parent.ParentId IS NULL AND NOT EXISTS (
                        SELECT 1 FROM CategoryKnowledgePoints root_link WHERE root_link.CategoryId = parent.Id)
                    THEN NULL ELSE c.ParentId END,
                    c.IsFree, c.OrderIndex, c.CreationTime
                FROM Categories c INNER JOIN directory_paths p ON p.Id = c.Id
                LEFT JOIN Categories parent ON parent.Id = c.ParentId
                WHERE c.ParentId IS NOT NULL OR EXISTS (
                    SELECT 1 FROM CategoryKnowledgePoints link WHERE link.CategoryId = c.Id);
                INSERT INTO KnowledgeCategoryKnowledgePoints (Id, KnowledgeCategoryId, KnowledgePointId, OrderIndex)
                    SELECT Id, CategoryId, KnowledgePointId, OrderIndex FROM CategoryKnowledgePoints;
                """);
            migrationBuilder.AddForeignKey(
                name: "FK_KnowledgeCategories_KnowledgeCategories_ParentKnowledgeCategoryId_TotalCategoryId", table: "KnowledgeCategories",
                columns: new[] { "ParentKnowledgeCategoryId", "TotalCategoryId" },
                principalTable: "KnowledgeCategories", principalColumns: new[] { "Id", "TotalCategoryId" },
                onDelete: ReferentialAction.Restrict);
            // The only writable knowledge association now points to the independent tree.
            migrationBuilder.DropTable(name: "CategoryKnowledgePoints");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO Categories (Id, Title, ParentId, IsFree, OrderIndex, CreationTime)
                    SELECT t.Id, t.Title, NULL, 0, t.OrderIndex, t.CreationTime FROM TotalCategories t
                    WHERE NOT EXISTS (SELECT 1 FROM Categories c WHERE c.Id = t.Id);
                INSERT INTO Categories (Id, Title, ParentId, IsFree, OrderIndex, CreationTime)
                    SELECT k.Id, k.Title, NULL, k.IsFree, k.OrderIndex, k.CreationTime FROM KnowledgeCategories k
                    WHERE NOT EXISTS (SELECT 1 FROM Categories c WHERE c.Id = k.Id);
                UPDATE Categories SET Title = k.Title, IsFree = k.IsFree, OrderIndex = k.OrderIndex,
                    ParentId = CASE WHEN k.Id = k.TotalCategoryId THEN NULL
                        ELSE COALESCE(k.ParentKnowledgeCategoryId, k.TotalCategoryId) END
                    FROM KnowledgeCategories k WHERE Categories.Id = k.Id;
                """);
            migrationBuilder.CreateTable(
                name: "CategoryKnowledgePoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    KnowledgePointId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrderIndex = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryKnowledgePoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CategoryKnowledgePoints_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CategoryKnowledgePoints_KnowledgePoints_KnowledgePointId",
                        column: x => x.KnowledgePointId,
                        principalTable: "KnowledgePoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CategoryKnowledgePoints_CategoryId",
                table: "CategoryKnowledgePoints",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryKnowledgePoints_KnowledgePointId",
                table: "CategoryKnowledgePoints",
                column: "KnowledgePointId");
            migrationBuilder.Sql("""
                INSERT INTO CategoryKnowledgePoints (Id, CategoryId, KnowledgePointId, OrderIndex)
                    SELECT Id, KnowledgeCategoryId, KnowledgePointId, OrderIndex FROM KnowledgeCategoryKnowledgePoints;
                """);
            migrationBuilder.DropTable(name: "KnowledgeCategoryKnowledgePoints");
            // SQLite enforces self-referencing RESTRICT even while dropping the table.
            // All parent relationships have already been restored to Categories above.
            migrationBuilder.Sql("UPDATE KnowledgeCategories SET ParentKnowledgeCategoryId = NULL;");
            migrationBuilder.DropTable(name: "KnowledgeCategories");
        }
    }
}
