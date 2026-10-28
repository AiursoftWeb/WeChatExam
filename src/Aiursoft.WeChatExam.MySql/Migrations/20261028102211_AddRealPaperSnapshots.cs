using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddRealPaperSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PaperSnapshot_SingleSource",
                table: "PaperSnapshots");

            migrationBuilder.AddColumn<Guid>(
                name: "RealPaperId",
                table: "PaperSnapshots",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_PaperSnapshots_RealPaperId_Version",
                table: "PaperSnapshots",
                columns: new[] { "RealPaperId", "Version" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaperSnapshot_SingleSource",
                table: "PaperSnapshots",
                sql: "(MockPaperId IS NULL OR PaperId IS NULL) AND (RealPaperId IS NULL OR (PaperId IS NULL AND MockPaperId IS NULL))");

            migrationBuilder.AddForeignKey(
                name: "FK_PaperSnapshots_RealPapers_RealPaperId",
                table: "PaperSnapshots",
                column: "RealPaperId",
                principalTable: "RealPapers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaperSnapshots_RealPapers_RealPaperId",
                table: "PaperSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_PaperSnapshots_RealPaperId_Version",
                table: "PaperSnapshots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaperSnapshot_SingleSource",
                table: "PaperSnapshots");

            migrationBuilder.DropColumn(
                name: "RealPaperId",
                table: "PaperSnapshots");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaperSnapshot_SingleSource",
                table: "PaperSnapshots",
                sql: "MockPaperId IS NULL OR PaperId IS NULL");
        }
    }
}
