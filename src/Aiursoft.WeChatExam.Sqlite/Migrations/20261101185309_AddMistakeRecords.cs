using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddMistakeRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MistakeRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    PracticeType = table.Column<int>(type: "INTEGER", nullable: true),
                    SourceQuestionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceQuestionSnapshotId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourcePaperId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourcePaperSnapshotId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourcePaperVersion = table.Column<int>(type: "INTEGER", nullable: true),
                    SourcePaperTitle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Content = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    QuestionType = table.Column<int>(type: "INTEGER", nullable: false),
                    Metadata = table.Column<string>(type: "text", maxLength: 5000, nullable: false),
                    UserAnswer = table.Column<string>(type: "TEXT", nullable: false),
                    StandardAnswer = table.Column<string>(type: "TEXT", maxLength: 5000, nullable: false),
                    Explanation = table.Column<string>(type: "TEXT", maxLength: 3000, nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxScore = table.Column<int>(type: "INTEGER", nullable: false),
                    GradingComment = table.Column<string>(type: "text", maxLength: 4000, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MistakeRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MistakeRecords_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MistakeRecords_UserId_CreationTime_Id",
                table: "MistakeRecords",
                columns: new[] { "UserId", "CreationTime", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MistakeRecords");
        }
    }
}
