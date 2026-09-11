using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ConAI.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMeetingSearchIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MeetingSearchDocuments",
                columns: table => new
                {
                    Rowid = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MeetingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    IndexedUpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TokenizerVersion = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeetingSearchDocuments", x => x.Rowid);
                    table.ForeignKey(
                        name: "FK_MeetingSearchDocuments_Meetings_MeetingId",
                        column: x => x.MeetingId,
                        principalTable: "Meetings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MeetingSearchDocuments_MeetingId",
                table: "MeetingSearchDocuments",
                column: "MeetingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MeetingSearchDocuments_OwnerId",
                table: "MeetingSearchDocuments",
                column: "OwnerId");

            // FTS5 は EF が扱えないため生 SQL で作る。content='' は本文を持たず索引だけを持つ形で、
            // contentless_delete=1 があると rowid 指定の DELETE ができる
            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE MeetingSearchIndex USING fts5(
                    Title,
                    Minutes,
                    Transcription,
                    TranslatedTranscription,
                    content='',
                    contentless_delete=1,
                    tokenize='unicode61');
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER MeetingSearchDocuments_ad AFTER DELETE ON MeetingSearchDocuments BEGIN
                    DELETE FROM MeetingSearchIndex WHERE rowid = old.Rowid;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 落とす順はトリガー、仮想テーブル、通常テーブル。トリガーが残ったまま索引テーブルを
            // 消すと、FTS5 の行だけが取り残される
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS MeetingSearchDocuments_ad;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS MeetingSearchIndex;");

            migrationBuilder.DropTable(
                name: "MeetingSearchDocuments");
        }
    }
}
