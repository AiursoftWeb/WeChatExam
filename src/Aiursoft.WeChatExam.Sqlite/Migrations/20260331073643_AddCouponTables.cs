using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddCouponTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Code",
                table: "DistributionChannels");

            migrationBuilder.AddColumn<Guid>(
                name: "CouponId",
                table: "PaymentOrders",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DiscountInFen",
                table: "PaymentOrders",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Coupons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DistributionChannelId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AmountInFen = table.Column<int>(type: "INTEGER", nullable: false),
                    IsSingleUse = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UsedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    UsedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Coupons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Coupons_DistributionChannels_DistributionChannelId",
                        column: x => x.DistributionChannelId,
                        principalTable: "DistributionChannels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CouponUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CouponId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    PaymentOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DiscountInFen = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CouponUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CouponUsages_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CouponUsages_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "Coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CouponUsages_PaymentOrders_PaymentOrderId",
                        column: x => x.PaymentOrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CouponVipProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CouponId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VipProductId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CouponVipProducts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CouponVipProducts_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "Coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CouponVipProducts_VipProducts_VipProductId",
                        column: x => x.VipProductId,
                        principalTable: "VipProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserClaimedCoupons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    CouponId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsUsed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserClaimedCoupons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserClaimedCoupons_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserClaimedCoupons_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "Coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_CouponId",
                table: "PaymentOrders",
                column: "CouponId");

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_Code",
                table: "Coupons",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_DistributionChannelId",
                table: "Coupons",
                column: "DistributionChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponUsages_CouponId",
                table: "CouponUsages",
                column: "CouponId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponUsages_PaymentOrderId",
                table: "CouponUsages",
                column: "PaymentOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponUsages_UserId",
                table: "CouponUsages",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponVipProducts_CouponId",
                table: "CouponVipProducts",
                column: "CouponId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponVipProducts_VipProductId",
                table: "CouponVipProducts",
                column: "VipProductId");

            migrationBuilder.CreateIndex(
                name: "IX_UserClaimedCoupons_CouponId",
                table: "UserClaimedCoupons",
                column: "CouponId");

            migrationBuilder.CreateIndex(
                name: "IX_UserClaimedCoupons_UserId",
                table: "UserClaimedCoupons",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentOrders_Coupons_CouponId",
                table: "PaymentOrders",
                column: "CouponId",
                principalTable: "Coupons",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentOrders_Coupons_CouponId",
                table: "PaymentOrders");

            migrationBuilder.DropTable(
                name: "CouponUsages");

            migrationBuilder.DropTable(
                name: "CouponVipProducts");

            migrationBuilder.DropTable(
                name: "UserClaimedCoupons");

            migrationBuilder.DropTable(
                name: "Coupons");

            migrationBuilder.DropIndex(
                name: "IX_PaymentOrders_CouponId",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "CouponId",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "DiscountInFen",
                table: "PaymentOrders");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "DistributionChannels",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "");
        }
    }
}
