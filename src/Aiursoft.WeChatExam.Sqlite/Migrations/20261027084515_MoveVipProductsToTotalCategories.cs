using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class MoveVipProductsToTotalCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VipProducts_Categories_CategoryId",
                table: "VipProducts");

            migrationBuilder.RenameColumn(
                name: "CategoryId",
                table: "VipProducts",
                newName: "TotalCategoryId");

            migrationBuilder.RenameIndex(
                name: "IX_VipProducts_CategoryId",
                table: "VipProducts",
                newName: "IX_VipProducts_TotalCategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_VipProducts_TotalCategories_TotalCategoryId",
                table: "VipProducts",
                column: "TotalCategoryId",
                principalTable: "TotalCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VipProducts_TotalCategories_TotalCategoryId",
                table: "VipProducts");

            migrationBuilder.RenameColumn(
                name: "TotalCategoryId",
                table: "VipProducts",
                newName: "CategoryId");

            migrationBuilder.RenameIndex(
                name: "IX_VipProducts_TotalCategoryId",
                table: "VipProducts",
                newName: "IX_VipProducts_CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_VipProducts_Categories_CategoryId",
                table: "VipProducts",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id");
        }
    }
}
