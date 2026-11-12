using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.MySql.Migrations
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
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "VirtualGoodsRevision",
                table: "VipProducts",
                type: "char(36)",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                collation: "ascii_general_ci");

            migrationBuilder.CreateTable(
                name: "VirtualPaymentGoods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    SourceProductId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    VipProductId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    AppId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Environment = table.Column<int>(type: "int", nullable: false),
                    OfferId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Fingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GoodsId = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PriceInFen = table.Column<int>(type: "int", nullable: false),
                    DurationDays = table.Column<int>(type: "int", nullable: false),
                    Remark = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PictureUrl = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    State = table.Column<int>(type: "int", nullable: false),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    Failures = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "VirtualPaymentGoodsLeases",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Owner = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Until = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VirtualPaymentGoodsLeases", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

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
                name: "IX_VirtualPaymentGoods_AppId_Environment_OfferId_SourceProductI~",
                table: "VirtualPaymentGoods",
                columns: new[] { "AppId", "Environment", "OfferId", "SourceProductId", "Fingerprint", "Attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VirtualPaymentGoods_AppId_Environment_OfferId_State_NextAtte~",
                table: "VirtualPaymentGoods",
                columns: new[] { "AppId", "Environment", "OfferId", "State", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VirtualPaymentGoods_VipProductId",
                table: "VirtualPaymentGoods",
                column: "VipProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_VirtualPaymentSnapshots_VirtualPaymentGoods_VirtualPaymentGo~",
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
                name: "FK_VirtualPaymentSnapshots_VirtualPaymentGoods_VirtualPaymentGo~",
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
