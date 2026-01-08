using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddExamTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaperSnapshots_Papers_PaperId",
                table: "PaperSnapshots");

            migrationBuilder.AlterColumn<Guid>(
                name: "PaperId",
                table: "PaperSnapshots",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci",
                oldClrType: typeof(Guid),
                oldType: "char(36)")
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.CreateTable(
                name: "Exams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PaperId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    PaperSnapshotId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    StartTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EndTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    AllowedAttempts = table.Column<int>(type: "int", nullable: false),
                    IsPublic = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ShowAnswerAfter = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    AllowReview = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Exams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Exams_PaperSnapshots_PaperSnapshotId",
                        column: x => x.PaperSnapshotId,
                        principalTable: "PaperSnapshots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Exams_Papers_PaperId",
                        column: x => x.PaperId,
                        principalTable: "Papers",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ExamRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ExamId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PaperSnapshotId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AttemptIndex = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SubmitTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    TotalScore = table.Column<int>(type: "int", nullable: false),
                    TeacherComment = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamRecords_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExamRecords_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExamRecords_PaperSnapshots_PaperSnapshotId",
                        column: x => x.PaperSnapshotId,
                        principalTable: "PaperSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AnswerRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ExamRecordId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    QuestionSnapshotId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserAnswer = table.Column<string>(type: "varchar(5000)", maxLength: 5000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Score = table.Column<int>(type: "int", nullable: false),
                    IsMarked = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    GradingResult = table.Column<string>(type: "varchar(5000)", maxLength: 5000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnswerRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnswerRecords_ExamRecords_ExamRecordId",
                        column: x => x.ExamRecordId,
                        principalTable: "ExamRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AnswerRecords_QuestionSnapshots_QuestionSnapshotId",
                        column: x => x.QuestionSnapshotId,
                        principalTable: "QuestionSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AnswerRecords_ExamRecordId",
                table: "AnswerRecords",
                column: "ExamRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_AnswerRecords_QuestionSnapshotId",
                table: "AnswerRecords",
                column: "QuestionSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamRecords_ExamId",
                table: "ExamRecords",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamRecords_PaperSnapshotId",
                table: "ExamRecords",
                column: "PaperSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamRecords_UserId",
                table: "ExamRecords",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_PaperId",
                table: "Exams",
                column: "PaperId");

            migrationBuilder.CreateIndex(
                name: "IX_Exams_PaperSnapshotId",
                table: "Exams",
                column: "PaperSnapshotId");

            migrationBuilder.AddForeignKey(
                name: "FK_PaperSnapshots_Papers_PaperId",
                table: "PaperSnapshots",
                column: "PaperId",
                principalTable: "Papers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaperSnapshots_Papers_PaperId",
                table: "PaperSnapshots");

            migrationBuilder.DropTable(
                name: "AnswerRecords");

            migrationBuilder.DropTable(
                name: "ExamRecords");

            migrationBuilder.DropTable(
                name: "Exams");

            migrationBuilder.AlterColumn<Guid>(
                name: "PaperId",
                table: "PaperSnapshots",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci",
                oldClrType: typeof(Guid),
                oldType: "char(36)",
                oldNullable: true)
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AddForeignKey(
                name: "FK_PaperSnapshots_Papers_PaperId",
                table: "PaperSnapshots",
                column: "PaperId",
                principalTable: "Papers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
