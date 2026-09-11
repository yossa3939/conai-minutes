using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConAI.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMinutesTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MinutesTemplateId",
                table: "Meetings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MinutesTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Body = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MinutesTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Meetings_MinutesTemplateId",
                table: "Meetings",
                column: "MinutesTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_MinutesTemplates_OwnerId",
                table: "MinutesTemplates",
                column: "OwnerId",
                unique: true,
                filter: "\"IsDefault\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_MinutesTemplates_OwnerId_Name",
                table: "MinutesTemplates",
                columns: new[] { "OwnerId", "Name" });

            migrationBuilder.AddForeignKey(
                name: "FK_Meetings_MinutesTemplates_MinutesTemplateId",
                table: "Meetings",
                column: "MinutesTemplateId",
                principalTable: "MinutesTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Meetings_MinutesTemplates_MinutesTemplateId",
                table: "Meetings");

            migrationBuilder.DropTable(
                name: "MinutesTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Meetings_MinutesTemplateId",
                table: "Meetings");

            migrationBuilder.DropColumn(
                name: "MinutesTemplateId",
                table: "Meetings");
        }
    }
}
