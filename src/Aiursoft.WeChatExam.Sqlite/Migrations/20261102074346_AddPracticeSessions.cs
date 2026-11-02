using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddPracticeSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PracticeSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceMistakeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PracticeSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PracticeSessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PracticeSessionItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceQuestionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    QuestionType = table.Column<int>(type: "INTEGER", nullable: false),
                    GradingStrategy = table.Column<int>(type: "INTEGER", nullable: false),
                    Metadata = table.Column<string>(type: "text", maxLength: 5000, nullable: false),
                    StandardAnswer = table.Column<string>(type: "text", maxLength: 5000, nullable: false),
                    Explanation = table.Column<string>(type: "text", maxLength: 3000, nullable: false),
                    UserAnswer = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsCorrect = table.Column<bool>(type: "INTEGER", nullable: true),
                    Score = table.Column<int>(type: "INTEGER", nullable: true),
                    GradingComment = table.Column<string>(type: "text", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PracticeSessionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PracticeSessionItems_PracticeSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "PracticeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSessionItems_SessionId_Position",
                table: "PracticeSessionItems",
                columns: new[] { "SessionId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PracticeSessions_UserId_CreationTime_Id",
                table: "PracticeSessions",
                columns: new[] { "UserId", "CreationTime", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PracticeSessionItems");

            migrationBuilder.DropTable(
                name: "PracticeSessions");
        }
    }
}
