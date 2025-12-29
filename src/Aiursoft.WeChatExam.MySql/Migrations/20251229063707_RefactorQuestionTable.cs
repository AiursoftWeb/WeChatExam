using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.MySql.Migrations
{
    /// <inheritdoc />
    public partial class RefactorQuestionTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SingleCorrect",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Questions");

            migrationBuilder.RenameColumn(
                name: "Text",
                table: "Questions",
                newName: "Content");

            migrationBuilder.RenameColumn(
                name: "List",
                table: "Questions",
                newName: "StandardAnswer");

            migrationBuilder.RenameColumn(
                name: "FillInCorrect",
                table: "Questions",
                newName: "Metadata");

            migrationBuilder.AddColumn<int>(
                name: "GradingStrategy",
                table: "Questions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "QuestionType",
                table: "Questions",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GradingStrategy",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "QuestionType",
                table: "Questions");

            migrationBuilder.RenameColumn(
                name: "StandardAnswer",
                table: "Questions",
                newName: "List");

            migrationBuilder.RenameColumn(
                name: "Metadata",
                table: "Questions",
                newName: "FillInCorrect");

            migrationBuilder.RenameColumn(
                name: "Content",
                table: "Questions",
                newName: "Text");

            migrationBuilder.AddColumn<string>(
                name: "SingleCorrect",
                table: "Questions",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Questions",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
