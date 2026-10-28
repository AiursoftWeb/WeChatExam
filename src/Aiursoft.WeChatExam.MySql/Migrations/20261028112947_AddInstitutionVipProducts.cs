using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddInstitutionVipProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InstitutionId",
                table: "VipProducts",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_VipProducts_InstitutionId",
                table: "VipProducts",
                column: "InstitutionId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_VipProducts_InstitutionTarget",
                table: "VipProducts",
                sql: "(`Type` = 2 AND `InstitutionId` IS NOT NULL AND `TotalCategoryId` IS NULL) OR (`Type` <> 2 AND `InstitutionId` IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_VipProducts_Institutions_InstitutionId",
                table: "VipProducts",
                column: "InstitutionId",
                principalTable: "Institutions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VipProducts_Institutions_InstitutionId",
                table: "VipProducts");

            migrationBuilder.DropIndex(
                name: "IX_VipProducts_InstitutionId",
                table: "VipProducts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_VipProducts_InstitutionTarget",
                table: "VipProducts");

            migrationBuilder.DropColumn(
                name: "InstitutionId",
                table: "VipProducts");
        }
    }
}
