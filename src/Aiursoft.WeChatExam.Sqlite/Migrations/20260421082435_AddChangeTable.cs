using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddChangeTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Changes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    TriggerUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    TargetUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    VipProductId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CouponId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Details = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Changes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Changes_AspNetUsers_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Changes_AspNetUsers_TriggerUserId",
                        column: x => x.TriggerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Changes_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalTable: "Coupons",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Changes_VipProducts_VipProductId",
                        column: x => x.VipProductId,
                        principalTable: "VipProducts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Changes_CouponId",
                table: "Changes",
                column: "CouponId");

            migrationBuilder.CreateIndex(
                name: "IX_Changes_TargetUserId",
                table: "Changes",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Changes_TriggerUserId",
                table: "Changes",
                column: "TriggerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Changes_VipProductId",
                table: "Changes",
                column: "VipProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Changes");
        }
    }
}
