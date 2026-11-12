using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddVirtualPaymentGoodsSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VirtualPaymentGoodsId",
                table: "VirtualPaymentSnapshots",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VirtualGoodsRevision",
                table: "VipProducts",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "VirtualPaymentGoods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VipProductId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AppId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Environment = table.Column<int>(type: "INTEGER", nullable: false),
                    OfferId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    GoodsId = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PriceInFen = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationDays = table.Column<int>(type: "INTEGER", nullable: false),
                    Remark = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    PictureUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    Failures = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VirtualPaymentGoods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VirtualPaymentGoods_VipProducts_VipProductId",
                        column: x => x.VipProductId,
                        principalTable: "VipProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "VirtualPaymentGoodsLeases",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Owner = table.Column<Guid>(type: "TEXT", nullable: false),
                    Until = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VirtualPaymentGoodsLeases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VirtualPaymentSnapshots_VirtualPaymentGoodsId",
                table: "VirtualPaymentSnapshots",
                column: "VirtualPaymentGoodsId");

            migrationBuilder.CreateIndex(
                name: "IX_VirtualPaymentGoods_AppId_Environment_GoodsId",
                table: "VirtualPaymentGoods",
                columns: new[] { "AppId", "Environment", "GoodsId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VirtualPaymentGoods_AppId_Environment_OfferId_SourceProductId_Fingerprint_Attempt",
                table: "VirtualPaymentGoods",
                columns: new[] { "AppId", "Environment", "OfferId", "SourceProductId", "Fingerprint", "Attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VirtualPaymentGoods_AppId_Environment_OfferId_State_NextAttemptAt",
                table: "VirtualPaymentGoods",
                columns: new[] { "AppId", "Environment", "OfferId", "State", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VirtualPaymentGoods_VipProductId",
                table: "VirtualPaymentGoods",
                column: "VipProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_VirtualPaymentSnapshots_VirtualPaymentGoods_VirtualPaymentGoodsId",
                table: "VirtualPaymentSnapshots",
                column: "VirtualPaymentGoodsId",
                principalTable: "VirtualPaymentGoods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VirtualPaymentSnapshots_VirtualPaymentGoods_VirtualPaymentGoodsId",
                table: "VirtualPaymentSnapshots");

            migrationBuilder.DropTable(
                name: "VirtualPaymentGoods");

            migrationBuilder.DropTable(
                name: "VirtualPaymentGoodsLeases");

            migrationBuilder.DropIndex(
                name: "IX_VirtualPaymentSnapshots_VirtualPaymentGoodsId",
                table: "VirtualPaymentSnapshots");

            migrationBuilder.DropColumn(
                name: "VirtualPaymentGoodsId",
                table: "VirtualPaymentSnapshots");

            migrationBuilder.DropColumn(
                name: "VirtualGoodsRevision",
                table: "VipProducts");
        }
    }
}
