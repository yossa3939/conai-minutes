using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConAI.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookEndpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WebhookEndpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    ProtectedUrl = table.Column<string>(type: "TEXT", nullable: false),
                    UrlHint = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    UrlFingerprint = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    NotifyOnSuccess = table.Column<bool>(type: "INTEGER", nullable: false),
                    NotifyOnFailure = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    LastAttemptedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookEndpoints", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEndpoints_OwnerId_Name",
                table: "WebhookEndpoints",
                columns: new[] { "OwnerId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEndpoints_OwnerId_UrlFingerprint",
                table: "WebhookEndpoints",
                columns: new[] { "OwnerId", "UrlFingerprint" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebhookEndpoints");
        }
    }
}
