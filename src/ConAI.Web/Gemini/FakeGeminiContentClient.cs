using System.Globalization;
using System.Text.RegularExpressions;

namespace ConAI.Web.Gemini;

public sealed class FakeGeminiContentClient : IGeminiContentClient
{
    public const string DefaultTranscription = "話者A: テスト文字起こし本文です。";

    public const string DefaultMinutes = """
        ## 会議名
        テスト会議

        ## 日時
        2026年8月17日 10:30

        ## 出席者
        話者A

        ## 議題ごとの要点

        ### テスト議題
        - テストの要点

        ## 決定事項
        - テストの決定

        ## 宿題
        特になし
        """;

    public const string DefaultTranslatedTranscription = "Speaker A: This is a fake transcription.";

    /// <summary>テストから応答を差し替えるためのフック。null なら既定の応答を返す。</summary>
    public Func<GenerationRequest, CancellationToken, Task<GenerationResult>>? Handler { get; set; }

    public GenerationRequest? LastRequest { get; private set; }

    public int CallCount { get; private set; }

    public async Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        CallCount++;

        if (Handler is not null)
        {
            return await Handler(request, cancellationToken);
        }

        return new GenerationResult(
            request.IncludeTranscription ? DefaultTranscription : string.Empty,
            DefaultMinutes,
            request.IncludeTranslatedTranscription ? DefaultTranslatedTranscription : null);
    }

    public const string DefaultAnswerPrefix = "テスト回答: ";

    public Func<SelectionRequest, CancellationToken, Task<SelectionResult>>? SelectHandler { get; set; }

    public SelectionRequest? LastSelectionRequest { get; private set; }

    public int SelectCallCount { get; private set; }

    public async Task<SelectionResult> SelectAsync(SelectionRequest request, CancellationToken cancellationToken)
    {
        LastSelectionRequest = request;
        SelectCallCount++;

        if (SelectHandler is not null)
        {
            return await SelectHandler(request, cancellationToken);
        }

        return new SelectionResult(SelectByTitle(request.Prompt));
    }

    /// <summary>
    /// 会議一覧の「1. 会議名（日時）」の行を拾い、質問文に会議名が含まれる連番を返す。
    /// 本物は主題の一致を見るが、偽実装は経路とプロンプトの形が保たれているかだけを見る。
    /// </summary>
    private static IReadOnlyList<int> SelectByTitle(string prompt)
    {
        var question = Slice(prompt, "<<<QUESTION", "QUESTION>>>");
        var digests = Slice(prompt, "<<<DIGESTS", "DIGESTS>>>");
        var numbers = new List<int>();

        foreach (Match match in DigestHeadingPattern.Matches(digests))
        {
            var title = match.Groups["title"].Value.Trim();
            if (title.Length > 0 && question.Contains(title, StringComparison.Ordinal))
            {
                numbers.Add(int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture));
            }
        }

        return numbers;
    }

    /// <summary>2 段目のプロンプトに載った議事録の番号を、載った順に全件返す。</summary>
    private static IReadOnlyList<int> UsedNumbers(string prompt)
    {
        var minutes = Slice(prompt, "<<<MINUTES", "MINUTES>>>");

        return [.. AnswerHeadingPattern.Matches(minutes)
            .Select(m => int.Parse(m.Groups["number"].Value, CultureInfo.InvariantCulture))];
    }

    // 見出し行は行頭から始まる。ダイジェストの抜粋は字下げしてあるので、抜粋の中の数字は拾わない。
    private static readonly Regex DigestHeadingPattern =
        new(@"^(?<number>\d+)\.\s*(?<title>[^（\r\n]+)", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex AnswerHeadingPattern =
        new(@"^## 議事録 (?<number>\d+):", RegexOptions.Multiline | RegexOptions.Compiled);

    private static string Slice(string text, string open, string close)
    {
        var start = text.IndexOf(open, StringComparison.Ordinal);
        var end = text.IndexOf(close, StringComparison.Ordinal);

        // 印が無ければ全体を返す。既存の呼び出し元がこの形に依存している。
        return start < 0 || end <= start ? text : text[(start + open.Length)..end].Trim();
    }

    public Func<ChatRequest, CancellationToken, Task<ChatResult>>? AskHandler { get; set; }

    public ChatRequest? LastChatRequest { get; private set; }

    public int AskCallCount { get; private set; }

    public async Task<ChatResult> AskAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        LastChatRequest = request;
        AskCallCount++;

        if (AskHandler is not null)
        {
            return await AskHandler(request, cancellationToken);
        }

        return new ChatResult($"{DefaultAnswerPrefix}{ExtractQuestion(request.Prompt)}", UsedNumbers(request.Prompt));
    }

    /// <summary>
    /// 質問はプロンプトの QUESTION 区画に入っている。
    /// 送られた質問を既定の答えに含めることで、E2E が「送った質問への答えが出た」ことを確かめられる（決定 17）。
    /// </summary>
    private static string ExtractQuestion(string prompt) => Slice(prompt, "<<<QUESTION", "QUESTION>>>");
}
