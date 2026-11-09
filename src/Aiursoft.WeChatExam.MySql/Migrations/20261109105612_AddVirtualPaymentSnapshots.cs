using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.MySql.Migrations
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
                    PaymentOrderId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AppId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OpenId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SignData = table.Column<string>(type: "varchar(4096)", maxLength: 4096, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VirtualPaymentSnapshots");
        }
    }
}
