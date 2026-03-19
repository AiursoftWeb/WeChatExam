using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.MySql.Migrations
{
    /// <inheritdoc />
    public partial class VipProductType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VipProducts_Categories_CategoryId",
                table: "VipProducts");

            migrationBuilder.AlterColumn<Guid>(
                name: "CategoryId",
                table: "VipProducts",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci",
                oldClrType: typeof(Guid),
                oldType: "char(36)")
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "VipProducts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_VipProducts_Type",
                table: "VipProducts",
                column: "Type");

            migrationBuilder.AddForeignKey(
                name: "FK_VipProducts_Categories_CategoryId",
                table: "VipProducts",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VipProducts_Categories_CategoryId",
                table: "VipProducts");

            migrationBuilder.DropIndex(
                name: "IX_VipProducts_Type",
                table: "VipProducts");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "VipProducts");

            migrationBuilder.AlterColumn<Guid>(
                name: "CategoryId",
                table: "VipProducts",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci",
                oldClrType: typeof(Guid),
                oldType: "char(36)",
                oldNullable: true)
                .OldAnnotation("Relational:Collation", "ascii_general_ci");

            migrationBuilder.AddForeignKey(
                name: "FK_VipProducts_Categories_CategoryId",
                table: "VipProducts",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
