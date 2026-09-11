using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ConAI.Web.Services;

/// <summary>翻訳モードのときだけ言語名を入れる。null なら文字起こしの主要言語に任せる。
/// MinutesTemplate は議事録の出力フォーマットの見本で、解決済みの本文が入る。</summary>
public sealed record PromptContext(
    string Title,
    DateTimeOffset? HeldAt,
    string? OutputLanguage,
    bool IncludeTranslatedTranscription,
    string Transcription,
    IReadOnlyList<string> References,
    string MinutesTemplate);

/// <summary>チャットの 1 往復。プロンプトの HISTORY 区画に整形して差し込む。</summary>
public sealed record ChatHistoryEntry(string Question, string Answer);

/// <summary>1 段目に渡す文脈。</summary>
public sealed record SelectionPromptContext(
    IReadOnlyList<MeetingDigest> Digests,
    IReadOnlyList<string> RecentQuestions,
    string Question);

/// <summary>2 段目に渡す議事録 1 件。Number は選抜結果を 1 から振り直した番号である。
/// TranscriptExcerpt は検索から選ばれた会議だけが持つ、質問に当たる文字起こしの窓である。</summary>
public sealed record ChatSourceContext(
    int Number, string Title, DateTime? HeldAt, string Minutes, string TranscriptExcerpt = "");

public sealed record ChatPromptContext(
    IReadOnlyList<ChatSourceContext> Sources,
    IReadOnlyList<ChatHistoryEntry> History,
    string Question);

public interface IPromptService
{
    string BuildSystemInstruction(PromptContext context);

    string BuildTranscribeAndMinutesPrompt(PromptContext context);

    string BuildMinutesFromTranscriptPrompt(PromptContext context);

    string BuildSelectionSystemInstruction();

    string BuildSelectionPrompt(SelectionPromptContext context);

    string BuildChatSystemInstruction();

    string BuildChatPrompt(ChatPromptContext context);
}

public sealed class PromptService : IPromptService
{
    private const string TranslationInstruction =
        "- translatedTranscription には、文字起こし全文を {{OUTPUT_LANGUAGE}} へ訳したものを入れます。原文の行構成を保ちます。";

    private readonly string _promptDirectory;
    private readonly ConcurrentDictionary<string, string> _cache = new();

    public PromptService(IWebHostEnvironment environment)
    {
        var candidate = Path.Combine(environment.ContentRootPath, "Prompts");
        _promptDirectory = Directory.Exists(candidate)
            ? candidate
            : Path.Combine(AppContext.BaseDirectory, "Prompts");
    }

    public string BuildSystemInstruction(PromptContext context)
    {
        var template = Read("system.md");

        if (context.OutputLanguage is { Length: > 0 } language)
        {
            template = $"{template}{Environment.NewLine}- 出力言語: {language}";
        }

        if (context.IncludeTranslatedTranscription)
        {
            template = $"{template}{Environment.NewLine}{TranslationInstruction}";
        }

        return Fill(template, BuildValues(context));
    }

    public string BuildTranscribeAndMinutesPrompt(PromptContext context) =>
        Fill(Read("transcribe-and-minutes.md"), BuildValues(context));

    public string BuildMinutesFromTranscriptPrompt(PromptContext context) =>
        Fill(Read("minutes-from-transcript.md"), BuildValues(context));

    public string BuildChatSystemInstruction() => Read("chat-system.md");

    public string BuildSelectionSystemInstruction() => Read("chat-select-system.md");

    public string BuildSelectionPrompt(SelectionPromptContext context)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{{MEETINGS}}"] = FormatDigests(context.Digests),
            ["{{RECENT_QUESTIONS}}"] = FormatRecentQuestions(context.RecentQuestions),
            ["{{QUESTION}}"] = Neutralize(context.Question)
        };

        return Fill(Read("chat-select.md"), values);
    }

    /// <summary>
    /// 見出し行を行頭に置き、抜粋を 3 文字ぶん字下げする。
    /// 字下げは飾りではない。行頭の数字だけで見出し行を見分けられるようにするための約束である。
    /// </summary>
    private static string FormatDigests(IReadOnlyList<MeetingDigest> digests)
    {
        if (digests.Count == 0)
        {
            return "（なし）";
        }

        var blocks = digests.Select(digest =>
        {
            var header = DigestFormats.Header(digest.Number, Neutralize(digest.Title), digest.HeldAt);
            var excerpt = string.Join(
                Environment.NewLine,
                Neutralize(digest.Excerpt)
                    .Replace("\r\n", "\n")
                    .Split('\n')
                    .Select(line => $"   {line}"));

            return $"{header}{Environment.NewLine}{excerpt}";
        });

        return string.Join($"{Environment.NewLine}{Environment.NewLine}", blocks);
    }

    private static string FormatRecentQuestions(IReadOnlyList<string> questions)
    {
        if (questions.Count == 0)
        {
            return "（なし）";
        }

        return string.Join(
            Environment.NewLine,
            questions.Select(question => $"- {Neutralize(question)}"));
    }

    public string BuildChatPrompt(ChatPromptContext context)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{{MINUTES}}"] = FormatSources(context.Sources),
            ["{{CHAT_HISTORY}}"] = FormatHistory(context.History),
            ["{{QUESTION}}"] = Neutralize(context.Question)
        };

        return Fill(Read("chat.md"), values);
    }

    /// <summary>
    /// 議事録を「## 議事録 N: 会議名（開催日時）」の見出しで区切って積む。
    /// 番号で区切るのは、2 段目に usedMeetingNumbers を番号で返させるためである。
    /// </summary>
    private static string FormatSources(IReadOnlyList<ChatSourceContext> sources)
    {
        if (sources.Count == 0)
        {
            return "（なし）";
        }

        var blocks = sources.Select(source =>
        {
            // 暦はカルチャで変わる。グレゴリオ暦で固定しないと、プロンプトに載る年がずれる。
            var heldAt = source.HeldAt?.ToString("yyyy年M月d日 HH:mm", CultureInfo.InvariantCulture) ?? "（未設定）";

            var block = $"## 議事録 {source.Number}: {Neutralize(source.Title)}（{heldAt}）"
                + $"{Environment.NewLine}{Environment.NewLine}{Neutralize(source.Minutes)}";

            if (source.TranscriptExcerpt.Length == 0)
            {
                return block;
            }

            // 抜粋であることを見出しで示す。伝えておかないと、窓の途切れを
            // 「そこで話が終わった」と読む
            return block
                + $"{Environment.NewLine}{Environment.NewLine}"
                + $"### 議事録 {source.Number} の文字起こし抜粋（前後は省略されています）"
                + $"{Environment.NewLine}{Environment.NewLine}{Neutralize(source.TranscriptExcerpt)}";
        });

        return string.Join($"{Environment.NewLine}{Environment.NewLine}", blocks);
    }

    private static string FormatHistory(IReadOnlyList<ChatHistoryEntry> history)
    {
        if (history.Count == 0)
        {
            return "（なし）";
        }

        var blocks = history.Select(entry =>
            $"質問: {Neutralize(entry.Question)}{Environment.NewLine}答え: {Neutralize(entry.Answer)}");

        return string.Join($"{Environment.NewLine}{Environment.NewLine}", blocks);
    }

    private string Read(string fileName) =>
        _cache.GetOrAdd(fileName, name => File.ReadAllText(Path.Combine(_promptDirectory, name)));

    private static readonly string[] Delimiters =
    [
        "<<<REFERENCES", "REFERENCES>>>",
        "<<<TRANSCRIPT", "TRANSCRIPT>>>",
        "<<<TEMPLATE", "TEMPLATE>>>",
        "<<<DIGESTS", "DIGESTS>>>",
        "<<<MINUTES", "MINUTES>>>",
        "<<<HISTORY", "HISTORY>>>",
        "<<<QUESTION", "QUESTION>>>",
    ];

    /// <summary>データの中に区切り記号があると囲みを閉じられてしまうので、埋める前に潰す。</summary>
    private static string Neutralize(string value)
    {
        foreach (var delimiter in Delimiters)
        {
            value = value.Replace(delimiter, "[区切り記号を除去しました]", StringComparison.Ordinal);
        }

        return value;
    }

    private static readonly Regex PlaceholderPattern = new(@"\{\{[A-Z_]+\}\}", RegexOptions.Compiled);

    private static Dictionary<string, string> BuildValues(PromptContext context) => new(StringComparer.Ordinal)
    {
        ["{{OUTPUT_LANGUAGE}}"] = context.OutputLanguage ?? string.Empty,
        ["{{TITLE}}"] = string.IsNullOrWhiteSpace(context.Title)
            ? "（未設定）"
            : Neutralize(context.Title),
        ["{{HELD_AT}}"] = context.HeldAt?.ToString("yyyy年M月d日 HH:mm", CultureInfo.InvariantCulture) ?? "（未設定）",
        ["{{TRANSCRIPTION}}"] = Neutralize(context.Transcription),
        ["{{MINUTES_TEMPLATE}}"] = Neutralize(context.MinutesTemplate),
        ["{{REFERENCES}}"] = context.References.Count == 0
            ? "（なし）"
            : Neutralize(string.Join($"{Environment.NewLine}{Environment.NewLine}", context.References))
    };

    /// <summary>
    /// 1 回の走査ですべてのプレースホルダを置換する。
    /// Replace を鎖にすると、先に埋めた値の中の {{...}} が後段の置換対象になり、
    /// 文字起こしから別の差し込みを起こせてしまう。
    /// </summary>
    private static string Fill(string template, IReadOnlyDictionary<string, string> values) =>
        PlaceholderPattern.Replace(
            template,
            match => values.TryGetValue(match.Value, out var value) ? value : match.Value);
}
