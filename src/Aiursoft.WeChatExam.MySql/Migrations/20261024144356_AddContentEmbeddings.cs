using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aiursoft.WeChatExam.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddContentEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "Embedding",
                table: "Questions",
                type: "longblob",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastEmbeddedAt",
                table: "Questions",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUpdatedAt",
                table: "Questions",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<byte[]>(
                name: "Embedding",
                table: "KnowledgePoints",
                type: "longblob",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastEmbeddedAt",
                table: "KnowledgePoints",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUpdatedAt",
                table: "KnowledgePoints",
                type: "datetime(6)",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Embedding",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "LastEmbeddedAt",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "LastUpdatedAt",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "Embedding",
                table: "KnowledgePoints");

            migrationBuilder.DropColumn(
                name: "LastEmbeddedAt",
                table: "KnowledgePoints");

            migrationBuilder.DropColumn(
                name: "LastUpdatedAt",
                table: "KnowledgePoints");
        }
    }
}
