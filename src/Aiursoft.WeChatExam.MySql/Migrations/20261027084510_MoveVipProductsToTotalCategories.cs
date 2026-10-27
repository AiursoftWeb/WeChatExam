using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.MySql.Migrations
{
    /// <inheritdoc />
    public partial class MoveVipProductsToTotalCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Validate every retained course ID before removing the old constraint.
            // MySQL DDL is not transactional: an unmapped ID must leave the old schema intact.
            migrationBuilder.AddForeignKey(
                name: "FK_VipProducts_TotalCategories_TotalCategoryId",
                table: "VipProducts",
                column: "CategoryId",
                principalTable: "TotalCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Refuse rollback if a newly created course has no legacy counterpart.
            migrationBuilder.AddForeignKey(
                name: "FK_VipProducts_Categories_CategoryId",
                table: "VipProducts",
                column: "TotalCategoryId",
                principalTable: "Categories",
                principalColumn: "Id");

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
        }
    }
}
