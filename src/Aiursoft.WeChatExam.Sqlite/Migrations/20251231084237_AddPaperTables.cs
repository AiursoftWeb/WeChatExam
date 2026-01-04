using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddPaperTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Papers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TimeLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    IsFree = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Papers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaperQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PaperId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaperQuestions_Papers_PaperId",
                        column: x => x.PaperId,
                        principalTable: "Papers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaperQuestions_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaperSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PaperId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TimeLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    IsFree = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaperSnapshots_Papers_PaperId",
                        column: x => x.PaperId,
                        principalTable: "Papers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuestionSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PaperSnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginalQuestionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    Score = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    QuestionType = table.Column<int>(type: "INTEGER", nullable: false),
                    GradingStrategy = table.Column<int>(type: "INTEGER", nullable: false),
                    Metadata = table.Column<string>(type: "TEXT", maxLength: 5000, nullable: false),
                    StandardAnswer = table.Column<string>(type: "TEXT", maxLength: 5000, nullable: false),
                    Explanation = table.Column<string>(type: "TEXT", maxLength: 3000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionSnapshots_PaperSnapshots_PaperSnapshotId",
                        column: x => x.PaperSnapshotId,
                        principalTable: "PaperSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaperQuestions_PaperId",
                table: "PaperQuestions",
                column: "PaperId");

            migrationBuilder.CreateIndex(
                name: "IX_PaperQuestions_QuestionId",
                table: "PaperQuestions",
                column: "QuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_PaperSnapshots_PaperId",
                table: "PaperSnapshots",
                column: "PaperId");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionSnapshots_PaperSnapshotId",
                table: "QuestionSnapshots",
                column: "PaperSnapshotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaperQuestions");

            migrationBuilder.DropTable(
                name: "QuestionSnapshots");

            migrationBuilder.DropTable(
                name: "PaperSnapshots");

            migrationBuilder.DropTable(
                name: "Papers");
        }
    }
}
