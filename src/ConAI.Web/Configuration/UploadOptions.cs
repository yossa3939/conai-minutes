using System.ComponentModel.DataAnnotations;

namespace ConAI.Web.Configuration;

public sealed class UploadOptions
{
    public const string SectionName = "Upload";

    [Range(1, 4096)]
    public int MaxFileSizeMb { get; set; } = 200;

    /// <summary>参考資料から取り出すテキストの上限（文字数）。zip 爆弾で展開結果が膨張するのを止める。</summary>
    public int MaxExtractedTextChars { get; set; } = 2_000_000;

    public long MaxFileSizeBytes => (long)MaxFileSizeMb * 1024 * 1024;
}
