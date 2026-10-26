using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class RemoveKnowledgePointHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KnowledgePoints_KnowledgePoints_ParentId",
                table: "KnowledgePoints");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgePoints_ParentId",
                table: "KnowledgePoints");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "KnowledgePoints");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentId",
                table: "KnowledgePoints",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgePoints_ParentId",
                table: "KnowledgePoints",
                column: "ParentId");

            migrationBuilder.AddForeignKey(
                name: "FK_KnowledgePoints_KnowledgePoints_ParentId",
                table: "KnowledgePoints",
                column: "ParentId",
                principalTable: "KnowledgePoints",
                principalColumn: "Id");
        }
    }
}
