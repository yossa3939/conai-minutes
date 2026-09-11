using System.Text;
using ConAI.Web.Data;

namespace ConAI.Web.Services;

public static class AllowedFileTypes
{
    private sealed record Entry(MeetingFileKind Kind, string[] MimeTypes);

    /// <summary>拡張子の並びは、そのまま添付欄の accept 属性と案内文の並びになる。
    /// Dictionary の列挙順は言語仕様が保証しないので、並びを守る側は配列で持つ。</summary>
    private static readonly (string Extension, Entry Entry)[] Entries =
    {
        (".mp3", new Entry(MeetingFileKind.Media, new[] { "audio/mpeg", "audio/mp3" })),
        (".m4a", new Entry(MeetingFileKind.Media, new[] { "audio/mp4", "audio/x-m4a", "audio/m4a" })),
        (".aac", new Entry(MeetingFileKind.Media, new[] { "audio/aac", "audio/x-aac" })),
        (".flac", new Entry(MeetingFileKind.Media, new[] { "audio/flac", "audio/x-flac" })),
        (".ogg", new Entry(MeetingFileKind.Media, new[] { "audio/ogg", "video/ogg", "application/ogg" })),
        (".wav", new Entry(MeetingFileKind.Media, new[] { "audio/wav", "audio/x-wav", "audio/wave", "audio/vnd.wave" })),
        (".mp4", new Entry(MeetingFileKind.Media, new[] { "video/mp4" })),
        (".mov", new Entry(MeetingFileKind.Media, new[] { "video/quicktime" })),
        (".webm", new Entry(MeetingFileKind.Media, new[] { "video/webm", "audio/webm" })),
        (".pdf", new Entry(MeetingFileKind.Reference, new[] { "application/pdf" })),
        (".txt", new Entry(MeetingFileKind.Reference, new[] { "text/plain", "application/octet-stream", "" })),
        (".md", new Entry(MeetingFileKind.Reference, new[] { "text/markdown", "text/x-markdown", "text/plain", "application/octet-stream", "" })),
        (".docx", new Entry(MeetingFileKind.Reference, new[] { "application/vnd.openxmlformats-officedocument.wordprocessingml.document" })),
        (".pptx", new Entry(MeetingFileKind.Reference, new[] { "application/vnd.openxmlformats-officedocument.presentationml.presentation" })),
        (".xlsx", new Entry(MeetingFileKind.Reference, new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" }))
    };

    private static readonly Dictionary<string, Entry> Map =
        Entries.ToDictionary(e => e.Extension, e => e.Entry, StringComparer.OrdinalIgnoreCase);

    /// <summary>Live 録音がアプリ自身から登録される拡張子。</summary>
    public static readonly string[] RecordingExtensions = { ".webm", ".mp4" };

    /// <summary>添付欄の accept 属性（.mp3,.m4a,…）の元になる、種類ごとの拡張子の一覧。
    /// Entries の宣言順で返す（編集画面の案内文と同じ並びにするため、宣言順は変えない）。</summary>
    public static IReadOnlyList<string> ExtensionsFor(MeetingFileKind kind) =>
        Entries.Where(entry => entry.Entry.Kind == kind)
            .Select(entry => entry.Extension)
            .ToList();

    public static bool TryGet(string extension, out MeetingFileKind kind, out IReadOnlyCollection<string> mimeTypes)
    {
        if (Map.TryGetValue(extension, out var entry))
        {
            kind = entry.Kind;
            mimeTypes = entry.MimeTypes;
            return true;
        }

        kind = default;
        mimeTypes = Array.Empty<string>();
        return false;
    }

    public static bool MatchesSignature(string extension, ReadOnlySpan<byte> head)
    {
        switch (extension.ToLowerInvariant())
        {
            case ".pdf":
                return StartsWith(head, "%PDF"u8);
            case ".docx":
            case ".pptx":
            case ".xlsx":
                return StartsWith(head, new byte[] { 0x50, 0x4B, 0x03, 0x04 });
            case ".webm":
                return StartsWith(head, new byte[] { 0x1A, 0x45, 0xDF, 0xA3 });
            case ".wav":
                return StartsWith(head, "RIFF"u8) && head.Length >= 12 && head.Slice(8, 4).SequenceEqual("WAVE"u8);
            case ".ogg":
                return StartsWith(head, "OggS"u8);
            case ".flac":
                return StartsWith(head, "fLaC"u8);
            case ".mp4":
            case ".mov":
            case ".m4a":
                return head.Length >= 8 && head.Slice(4, 4).SequenceEqual("ftyp"u8);
            default:
                // mp3 / aac / txt / md は先頭バイトで判別できないため、拡張子と MIME の一致で足りるとする（spec 8.2）
                return true;
        }
    }

    private static bool StartsWith(ReadOnlySpan<byte> head, ReadOnlySpan<byte> prefix) =>
        head.Length >= prefix.Length && head[..prefix.Length].SequenceEqual(prefix);
}
