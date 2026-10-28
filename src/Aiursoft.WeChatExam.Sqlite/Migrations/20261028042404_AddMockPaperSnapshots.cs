using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddMockPaperSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MockPaperId",
                table: "PaperSnapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaperSnapshots_MockPaperId_Version",
                table: "PaperSnapshots",
                columns: new[] { "MockPaperId", "Version" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaperSnapshot_SingleSource",
                table: "PaperSnapshots",
                sql: "MockPaperId IS NULL OR PaperId IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_PaperSnapshots_MockPapers_MockPaperId",
                table: "PaperSnapshots",
                column: "MockPaperId",
                principalTable: "MockPapers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaperSnapshots_MockPapers_MockPaperId",
                table: "PaperSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_PaperSnapshots_MockPaperId_Version",
                table: "PaperSnapshots");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaperSnapshot_SingleSource",
                table: "PaperSnapshots");

            migrationBuilder.DropColumn(
                name: "MockPaperId",
                table: "PaperSnapshots");
        }
    }
}
