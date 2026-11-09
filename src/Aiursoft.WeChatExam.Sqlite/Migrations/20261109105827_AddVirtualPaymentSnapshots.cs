using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddVirtualPaymentSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VirtualPaymentSnapshots",
                columns: table => new
                {
                    PaymentOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AppId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OpenId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SignData = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VirtualPaymentSnapshots", x => x.PaymentOrderId);
                    table.ForeignKey(
                        name: "FK_VirtualPaymentSnapshots_PaymentOrders_PaymentOrderId",
                        column: x => x.PaymentOrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VirtualPaymentSnapshots");
        }
    }
}
