using System.Text;
using ConAI.Web.Configuration;
using ConAI.Web.Data;
using ConAI.Web.Gemini;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Services;

public interface IMinutesGenerationService
{
    Task<MinutesGenerationOutcome> GenerateAsync(Guid meetingId, CancellationToken cancellationToken);
}

/// <summary>生成結果と、文字起こしを会議へ書き戻してよいか。Media から起こしたときだけ真になる。</summary>
public sealed record MinutesGenerationOutcome(GenerationResult Result, bool TranscribedFromMedia);

public sealed class MinutesGenerationService : IMinutesGenerationService
{
    private static readonly HashSet<string> PlainTextExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".txt", ".md" };

    private readonly IMeetingService _meetings;
    private readonly IFileStorageService _storage;
    private readonly IPromptService _prompts;
    private readonly IMinutesTemplateService _templates;
    private readonly IReferenceDocumentConverter _converter;
    private readonly IGeminiContentClient _gemini;
    private readonly GeminiOptions _options;

    public MinutesGenerationService(
        IMeetingService meetings,
        IFileStorageService storage,
        IPromptService prompts,
        IMinutesTemplateService templates,
        IReferenceDocumentConverter converter,
        IGeminiContentClient gemini,
        IOptions<GeminiOptions> options)
    {
        _meetings = meetings;
        _storage = storage;
        _prompts = prompts;
        _templates = templates;
        _converter = converter;
        _gemini = gemini;
        _options = options.Value;
    }

    public async Task<MinutesGenerationOutcome> GenerateAsync(Guid meetingId, CancellationToken cancellationToken)
    {
        var meeting = await _meetings.GetForGenerationAsync(meetingId, cancellationToken)
            ?? throw new InvalidOperationException($"会議 {meetingId} が見つかりません。");

        var hasTranscription = !string.IsNullOrWhiteSpace(meeting.Transcription);
        var references = new List<string>();
        var parts = new List<GeminiFilePart>();

        foreach (var file in meeting.Files.OrderBy(file => file.CreatedAt))
        {
            if (file.Kind == MeetingFileKind.Reference)
            {
                await AddReferenceAsync(meeting.Id, file, references, parts, cancellationToken);
                continue;
            }

            if (!hasTranscription)
            {
                var bytes = await _storage.ReadAllBytesAsync(meeting.Id, file.Id, file.Extension, cancellationToken);
                parts.Add(new GeminiFilePart(file.ContentType, bytes, file.OriginalFileName));
            }
        }

        // 会議が自分のテンプレートを指していればその本文、そうでなければ既定の本文になる
        var minutesTemplate = await _templates.ResolveForGenerationAsync(meeting, cancellationToken);

        var context = new PromptContext(
            Title: meeting.Title,
            HeldAt: meeting.HeldAt,
            // 非翻訳モードでは出力言語を指示せず、文字起こしの主要言語に任せる。
            OutputLanguage: meeting.TranslateMode ? SupportedLanguages.DisplayName(meeting.TargetLanguage) : null,
            IncludeTranslatedTranscription: meeting.TranslateMode,
            Transcription: meeting.Transcription,
            References: references,
            MinutesTemplate: minutesTemplate);

        var prompt = hasTranscription
            ? _prompts.BuildMinutesFromTranscriptPrompt(context)
            : _prompts.BuildTranscribeAndMinutesPrompt(context);

        var request = new GenerationRequest(
            _options.GenerateModel,
            _prompts.BuildSystemInstruction(context),
            prompt,
            parts,
            IncludeTranscription: !hasTranscription,
            IncludeTranslatedTranscription: meeting.TranslateMode);

        var result = await _gemini.GenerateAsync(request, cancellationToken);
        return new MinutesGenerationOutcome(result, TranscribedFromMedia: !hasTranscription);
    }

    private async Task AddReferenceAsync(
        Guid meetingId,
        MeetingFile file,
        List<string> references,
        List<GeminiFilePart> parts,
        CancellationToken cancellationToken)
    {
        var bytes = await _storage.ReadAllBytesAsync(meetingId, file.Id, file.Extension, cancellationToken);

        if (_converter.CanConvert(file.Extension))
        {
            await using var content = new MemoryStream(bytes, writable: false);
            references.Add($"### {file.OriginalFileName}{Environment.NewLine}{_converter.ExtractText(file.Extension, content)}");
            return;
        }

        if (PlainTextExtensions.Contains(file.Extension))
        {
            references.Add($"### {file.OriginalFileName}{Environment.NewLine}{Encoding.UTF8.GetString(bytes)}");
            return;
        }

        references.Add($"### {file.OriginalFileName}（添付として送付）");
        parts.Add(new GeminiFilePart(file.ContentType, bytes, file.OriginalFileName));
    }
}
