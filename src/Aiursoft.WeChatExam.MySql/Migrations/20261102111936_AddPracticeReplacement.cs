using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddPracticeReplacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Replace the index atomically: it also supports the SessionId foreign key.
            migrationBuilder.Sql("ALTER TABLE PracticeSessionItems DROP INDEX IX_PracticeSessionItems_SessionId_Position, ADD INDEX IX_PracticeSessionItems_SessionId_Position (SessionId, Position)");

            migrationBuilder.AddColumn<int>(
                name: "ReplacementVersion",
                table: "PracticeSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReplacedAt",
                table: "PracticeSessionItems",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReplacementReason",
                table: "PracticeSessionItems",
                type: "int",
                nullable: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The previous schema permits one item per position. Keep the active version
            // and its answer; only replacement history/feedback is removed on rollback.
            migrationBuilder.Sql("DELETE FROM PracticeSessionItems WHERE ReplacedAt IS NOT NULL");

            // Replace the index atomically: it also supports the SessionId foreign key.
            migrationBuilder.Sql("ALTER TABLE PracticeSessionItems DROP INDEX IX_PracticeSessionItems_SessionId_Position, ADD UNIQUE INDEX IX_PracticeSessionItems_SessionId_Position (SessionId, Position)");

            migrationBuilder.DropColumn(
                name: "ReplacementVersion",
                table: "PracticeSessions");

            migrationBuilder.DropColumn(
                name: "ReplacedAt",
                table: "PracticeSessionItems");

            migrationBuilder.DropColumn(
                name: "ReplacementReason",
                table: "PracticeSessionItems");

        }
    }
}
