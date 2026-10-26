using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddTotalCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TotalCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TotalCategories", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // Preserve the identities of existing courses, but do not copy their
            // knowledge point descendants or their unrelated free/paid flags.
            // This is a one-time backfill; subsequent edits use TotalCategories only.
            migrationBuilder.Sql("""
                INSERT INTO `TotalCategories` (`Id`, `Title`, `OrderIndex`, `CreationTime`)
                SELECT `Id`, `Title`, `OrderIndex`, `CreationTime`
                FROM `Categories`
                WHERE `ParentId` IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TotalCategories");
        }
    }
}
