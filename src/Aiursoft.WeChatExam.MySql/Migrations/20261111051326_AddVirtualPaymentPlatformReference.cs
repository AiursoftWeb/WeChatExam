using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddVirtualPaymentPlatformReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlatformOrderId",
                table: "VirtualPaymentSnapshots",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_VirtualPaymentSnapshots_AppId_PlatformOrderId",
                table: "VirtualPaymentSnapshots",
                columns: new[] { "AppId", "PlatformOrderId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VirtualPaymentSnapshots_AppId_PlatformOrderId",
                table: "VirtualPaymentSnapshots");

            migrationBuilder.DropColumn(
                name: "PlatformOrderId",
                table: "VirtualPaymentSnapshots");
        }
    }
}
