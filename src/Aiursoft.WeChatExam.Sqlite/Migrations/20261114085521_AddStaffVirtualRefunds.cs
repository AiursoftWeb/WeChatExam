using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffVirtualRefunds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Platform",
                table: "VirtualPaymentSnapshots",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VirtualPaymentRefundRequests",
                columns: table => new
                {
                    PaymentOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RefundOrderId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    AmountInFen = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    PlatformRefundId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    ErrorCode = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VirtualPaymentRefundRequests", x => x.PaymentOrderId);
                    table.ForeignKey(
                        name: "FK_VirtualPaymentRefundRequests_PaymentOrders_PaymentOrderId",
                        column: x => x.PaymentOrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VirtualPaymentRefundRequests_RefundOrderId",
                table: "VirtualPaymentRefundRequests",
                column: "RefundOrderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VirtualPaymentRefundRequests");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "VirtualPaymentSnapshots");
        }
    }
}
