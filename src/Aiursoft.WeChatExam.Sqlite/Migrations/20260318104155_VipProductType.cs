using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.Sqlite.Migrations
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
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "VipProducts",
                type: "INTEGER",
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
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

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
