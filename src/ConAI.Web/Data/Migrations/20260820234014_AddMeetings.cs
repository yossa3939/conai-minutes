using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConAI.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMeetings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Meetings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    HeldAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LiveMode = table.Column<bool>(type: "INTEGER", nullable: false),
                    TranslateMode = table.Column<bool>(type: "INTEGER", nullable: false),
                    TargetLanguage = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Transcription = table.Column<string>(type: "TEXT", nullable: false),
                    TranslatedTranscription = table.Column<string>(type: "TEXT", nullable: false),
                    Minutes = table.Column<string>(type: "TEXT", nullable: false),
                    GenerationStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    GenerationError = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Meetings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MeetingFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MeetingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Extension = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeetingFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MeetingFiles_Meetings_MeetingId",
                        column: x => x.MeetingId,
                        principalTable: "Meetings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MeetingFiles_MeetingId",
                table: "MeetingFiles",
                column: "MeetingId");

            migrationBuilder.CreateIndex(
                name: "IX_Meetings_OwnerId_UpdatedAt",
                table: "Meetings",
                columns: new[] { "OwnerId", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MeetingFiles");

            migrationBuilder.DropTable(
                name: "Meetings");
        }
    }
}
