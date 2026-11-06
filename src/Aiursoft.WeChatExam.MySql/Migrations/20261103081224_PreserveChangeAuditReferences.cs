using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.MySql.Migrations
{
    /// <inheritdoc />
    public partial class PreserveChangeAuditReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Changes_AspNetUsers_TriggerUserId",
                table: "Changes");

            migrationBuilder.DropForeignKey(
                name: "FK_Changes_Coupons_CouponId",
                table: "Changes");

            migrationBuilder.DropForeignKey(
                name: "FK_Changes_VipProducts_VipProductId",
                table: "Changes");

            migrationBuilder.AddForeignKey(
                name: "FK_Changes_AspNetUsers_TriggerUserId",
                table: "Changes",
                column: "TriggerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Changes_Coupons_CouponId",
                table: "Changes",
                column: "CouponId",
                principalTable: "Coupons",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Changes_VipProducts_VipProductId",
                table: "Changes",
                column: "VipProductId",
                principalTable: "VipProducts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Changes_AspNetUsers_TriggerUserId",
                table: "Changes");

            migrationBuilder.DropForeignKey(
                name: "FK_Changes_Coupons_CouponId",
                table: "Changes");

            migrationBuilder.DropForeignKey(
                name: "FK_Changes_VipProducts_VipProductId",
                table: "Changes");

            migrationBuilder.AddForeignKey(
                name: "FK_Changes_AspNetUsers_TriggerUserId",
                table: "Changes",
                column: "TriggerUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Changes_Coupons_CouponId",
                table: "Changes",
                column: "CouponId",
                principalTable: "Coupons",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Changes_VipProducts_VipProductId",
                table: "Changes",
                column: "VipProductId",
                principalTable: "VipProducts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
