using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionPeriodAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "QuestionCategoryId",
                table: "Questions",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_QuestionCategoryId",
                table: "Questions",
                column: "QuestionCategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Questions_QuestionCategories_QuestionCategoryId",
                table: "Questions",
                column: "QuestionCategoryId",
                principalTable: "QuestionCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Questions_QuestionCategories_QuestionCategoryId",
                table: "Questions");

            migrationBuilder.DropIndex(
                name: "IX_Questions_QuestionCategoryId",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "QuestionCategoryId",
                table: "Questions");
        }
    }
}
