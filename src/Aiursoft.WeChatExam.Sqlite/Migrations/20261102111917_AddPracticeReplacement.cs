using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddPracticeReplacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PracticeSessionItems_SessionId_Position",
                table: "PracticeSessionItems");

            migrationBuilder.AddColumn<int>(
                name: "ReplacementVersion",
                table: "PracticeSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReplacedAt",
                table: "PracticeSessionItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReplacementReason",
                table: "PracticeSessionItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSessionItems_SessionId_Position",
                table: "PracticeSessionItems",
                columns: new[] { "SessionId", "Position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous schema permits one item per position. Keep the active version
            // and its answer; only replacement history/feedback is removed on rollback.
            migrationBuilder.Sql("DELETE FROM PracticeSessionItems WHERE ReplacedAt IS NOT NULL");

            migrationBuilder.DropIndex(
                name: "IX_PracticeSessionItems_SessionId_Position",
                table: "PracticeSessionItems");

            migrationBuilder.DropColumn(
                name: "ReplacementVersion",
                table: "PracticeSessions");

            migrationBuilder.DropColumn(
                name: "ReplacedAt",
                table: "PracticeSessionItems");

            migrationBuilder.DropColumn(
                name: "ReplacementReason",
                table: "PracticeSessionItems");

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSessionItems_SessionId_Position",
                table: "PracticeSessionItems",
                columns: new[] { "SessionId", "Position" },
                unique: true);
        }
    }
}
