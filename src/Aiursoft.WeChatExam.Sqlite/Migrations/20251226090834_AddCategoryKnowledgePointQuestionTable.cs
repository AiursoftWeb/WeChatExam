using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryKnowledgePointQuestionTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CategoryKnowledgePoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    KnowledgePointId = table.Column<Guid>(type: "TEXT", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "KnowledgePointQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    KnowledgePointId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgePointQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgePointQuestions_KnowledgePoints_KnowledgePointId",
                        column: x => x.KnowledgePointId,
                        principalTable: "KnowledgePoints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KnowledgePointQuestions_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalTable: "Questions",
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

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgePointQuestions_KnowledgePointId",
                table: "KnowledgePointQuestions",
                column: "KnowledgePointId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgePointQuestions_QuestionId",
                table: "KnowledgePointQuestions",
                column: "QuestionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CategoryKnowledgePoints");

            migrationBuilder.DropTable(
                name: "KnowledgePointQuestions");
        }
    }
}
