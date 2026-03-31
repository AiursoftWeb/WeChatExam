using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.MySql.Migrations
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
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<int>(
                name: "DiscountInFen",
                table: "PaymentOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Coupons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Code = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DistributionChannelId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AmountInFen = table.Column<int>(type: "int", nullable: false),
                    IsSingleUse = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UsedByUserId = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UsedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CouponUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CouponId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PaymentOrderId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UsedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DiscountInFen = table.Column<int>(type: "int", nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CouponVipProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CouponId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    VipProductId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci")
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserClaimedCoupons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CouponId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    ClaimedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsUsed = table.Column<bool>(type: "tinyint(1)", nullable: false)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

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
                type: "varchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
