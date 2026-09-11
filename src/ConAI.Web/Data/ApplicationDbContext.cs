using ConAI.Web.Services;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ConAI.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
    public DbSet<Meeting> Meetings => Set<Meeting>();

    public DbSet<MeetingFile> MeetingFiles => Set<MeetingFile>();

    public DbSet<MinutesTemplate> MinutesTemplates => Set<MinutesTemplate>();

    public DbSet<ChatTurn> ChatTurns => Set<ChatTurn>();

    public DbSet<ChatTurnSource> ChatTurnSources => Set<ChatTurnSource>();

    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();

    public DbSet<MeetingSearchDocument> MeetingSearchDocuments => Set<MeetingSearchDocument>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Meeting>(entity =>
        {
            entity.HasIndex(m => new { m.OwnerId, m.UpdatedAt });
            entity.Property(m => m.OwnerId).IsRequired().HasMaxLength(450);
            entity.Property(m => m.GenerationStatus).HasConversion<int>();
            entity.HasMany(m => m.Files)
                .WithOne(f => f.Meeting!)
                .HasForeignKey(f => f.MeetingId)
                .OnDelete(DeleteBehavior.Cascade);
            // テンプレートを消しても会議は残す。null は「既定を使う」に戻る
            entity.HasOne(m => m.MinutesTemplate)
                .WithMany()
                .HasForeignKey(m => m.MinutesTemplateId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<MeetingFile>(entity =>
        {
            entity.HasIndex(f => f.MeetingId);
            entity.Property(f => f.Kind).HasConversion<int>();
        });

        builder.Entity<MinutesTemplate>(entity =>
        {
            entity.HasIndex(t => new { t.OwnerId, t.Name });
            // 既定は利用者ごとに 1 件だけ。SQLite の部分索引で DB 側から守る
            entity.HasIndex(t => t.OwnerId)
                .HasFilter("\"IsDefault\" = 1")
                .IsUnique();
            entity.Property(t => t.OwnerId).IsRequired().HasMaxLength(450);
            entity.Property(t => t.Name).IsRequired().HasMaxLength(MinutesTemplateLimits.MaxNameChars);
            entity.Property(t => t.Body).IsRequired();
        });

        builder.Entity<ChatTurn>(entity =>
        {
            entity.HasIndex(t => new { t.OwnerId, t.CreatedAt });
            entity.Property(t => t.OwnerId).IsRequired().HasMaxLength(450);
            entity.Property(t => t.Question).IsRequired().HasMaxLength(ChatLimits.MaxQuestionChars);
            entity.Property(t => t.Answer).IsRequired();
            entity.HasMany(t => t.Sources)
                .WithOne(s => s.ChatTurn!)
                .HasForeignKey(s => s.ChatTurnId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ChatTurnSource>(entity =>
        {
            entity.HasIndex(s => new { s.ChatTurnId, s.Order });
            entity.Property(s => s.MeetingTitle).IsRequired().HasMaxLength(ChatLimits.MaxSourceTitleChars);
            // 会議を消しても根拠の行は残す。会議名の写しがあるので、答えの表示は壊れない。
            entity.HasOne(s => s.Meeting)
                .WithMany()
                .HasForeignKey(s => s.MeetingId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<WebhookEndpoint>(entity =>
        {
            entity.HasIndex(e => new { e.OwnerId, e.Name });
            // 同じ URL を 2 度登録すると、1 つの会議で同じチャンネルへ二重に届く。DB 側で弾く
            entity.HasIndex(e => new { e.OwnerId, e.UrlFingerprint }).IsUnique();
            entity.Property(e => e.OwnerId).IsRequired().HasMaxLength(450);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(WebhookLimits.MaxNameChars);
            entity.Property(e => e.ProtectedUrl).IsRequired();
            entity.Property(e => e.UrlHint).IsRequired().HasMaxLength(WebhookLimits.MaxHintChars);
            entity.Property(e => e.UrlFingerprint).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Kind).HasConversion<int>();
            entity.Property(e => e.LastStatus).HasConversion<int>();
            entity.Property(e => e.LastError).HasMaxLength(WebhookLimits.MaxErrorChars);
        });

        builder.Entity<MeetingSearchDocument>(entity =>
        {
            entity.HasKey(d => d.Rowid);
            entity.Property(d => d.Rowid).ValueGeneratedOnAdd();
            entity.HasIndex(d => d.MeetingId).IsUnique();
            entity.HasIndex(d => d.OwnerId);
            entity.Property(d => d.OwnerId).IsRequired().HasMaxLength(450);
            // 会議を消したら索引も消す。この連鎖がトリガーを呼び、FTS5 の行まで届く
            entity.HasOne<Meeting>()
                .WithMany()
                .HasForeignKey(d => d.MeetingId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
