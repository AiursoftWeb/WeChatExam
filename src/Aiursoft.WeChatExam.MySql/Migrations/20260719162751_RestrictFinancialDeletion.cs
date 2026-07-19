using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.MySql.Migrations
{
    /// <inheritdoc />
    public partial class RestrictFinancialDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentOrders_AspNetUsers_UserId",
                table: "PaymentOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_VipMemberships_AspNetUsers_UserId",
                table: "VipMemberships");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentOrders_AspNetUsers_UserId",
                table: "PaymentOrders",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VipMemberships_AspNetUsers_UserId",
                table: "VipMemberships",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentOrders_AspNetUsers_UserId",
                table: "PaymentOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_VipMemberships_AspNetUsers_UserId",
                table: "VipMemberships");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentOrders_AspNetUsers_UserId",
                table: "PaymentOrders",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VipMemberships_AspNetUsers_UserId",
                table: "VipMemberships",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
