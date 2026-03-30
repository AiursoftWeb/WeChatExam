using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryOrderIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OrderIndex",
                table: "Categories",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrderIndex",
                table: "Categories");
        }
    }
}
