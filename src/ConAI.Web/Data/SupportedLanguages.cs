namespace ConAI.Web.Data;

public sealed record LanguageOption(string Code, string Name);

public static class SupportedLanguages
{
    public const string Default = "ja";

    public static IReadOnlyList<LanguageOption> All { get; } = new[]
    {
        new LanguageOption("ja", "日本語"),
        new LanguageOption("en", "English"),
        new LanguageOption("zh", "中文（簡体）"),
        new LanguageOption("zh-TW", "中文（繁体）"),
        new LanguageOption("ko", "한국어"),
        new LanguageOption("fr", "Français"),
        new LanguageOption("de", "Deutsch"),
        new LanguageOption("es", "Español"),
        new LanguageOption("pt", "Português"),
        new LanguageOption("vi", "Tiếng Việt"),
        new LanguageOption("th", "ไทย")
    };

    public static bool IsSupported(string? code) =>
        code is not null && All.Any(l => string.Equals(l.Code, code, StringComparison.Ordinal));

    public static string DisplayName(string code) =>
        All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.Ordinal))?.Name ?? code;
}
