using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddWechatpay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VipProducts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PriceInFen = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationDays = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VipProducts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VipProducts_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OutTradeNo = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    VipProductId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AmountInFen = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    WechatTransactionId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    PrepayId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiredAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentOrders_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PaymentOrders_VipProducts_VipProductId",
                        column: x => x.VipProductId,
                        principalTable: "VipProducts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PaymentLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PaymentOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    RawData = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentLogs_PaymentOrders_PaymentOrderId",
                        column: x => x.PaymentOrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VipMemberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    VipProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastPaymentOrderId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VipMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VipMemberships_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VipMemberships_PaymentOrders_LastPaymentOrderId",
                        column: x => x.LastPaymentOrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_VipMemberships_VipProducts_VipProductId",
                        column: x => x.VipProductId,
                        principalTable: "VipProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentLogs_PaymentOrderId",
                table: "PaymentLogs",
                column: "PaymentOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_OutTradeNo",
                table: "PaymentOrders",
                column: "OutTradeNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_Status",
                table: "PaymentOrders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_UserId",
                table: "PaymentOrders",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_VipProductId",
                table: "PaymentOrders",
                column: "VipProductId");

            migrationBuilder.CreateIndex(
                name: "IX_VipMemberships_LastPaymentOrderId",
                table: "VipMemberships",
                column: "LastPaymentOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_VipMemberships_UserId",
                table: "VipMemberships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_VipMemberships_UserId_VipProductId",
                table: "VipMemberships",
                columns: new[] { "UserId", "VipProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VipMemberships_VipProductId",
                table: "VipMemberships",
                column: "VipProductId");

            migrationBuilder.CreateIndex(
                name: "IX_VipProducts_CategoryId",
                table: "VipProducts",
                column: "CategoryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentLogs");

            migrationBuilder.DropTable(
                name: "VipMemberships");

            migrationBuilder.DropTable(
                name: "PaymentOrders");

            migrationBuilder.DropTable(
                name: "VipProducts");
        }
    }
}
