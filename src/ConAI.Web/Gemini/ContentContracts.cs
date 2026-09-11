namespace ConAI.Web.Gemini;

public sealed record GeminiFilePart(string MimeType, byte[] Bytes, string FileName);

public sealed record GenerationRequest(
    string Model,
    string SystemInstruction,
    string Prompt,
    IReadOnlyList<GeminiFilePart> Files,
    bool IncludeTranscription,
    bool IncludeTranslatedTranscription);

public sealed record GenerationResult(string Transcription, string Minutes, string? TranslatedTranscription);

/// <summary>
/// 1 段目の選抜。文字列だけを持つ。
/// ダイジェストをどう並べるかは PromptService の仕事で、クライアントは中身を知らない。
/// </summary>
public sealed record SelectionRequest(string Model, string SystemInstruction, string Prompt);

/// <summary>選ばれた会議の連番。プロンプトに載せた順の番号で返る。</summary>
public sealed record SelectionResult(IReadOnlyList<int> MeetingNumbers);

/// <summary>
/// 議事録への質問。添付を渡す口を持たない。
/// 根拠は議事録と文字起こしのテキストだけで、音声や画像は見に行かない。
/// </summary>
public sealed record ChatRequest(string Model, string SystemInstruction, string Prompt);

/// <summary>
/// 2 段目の答え。UsedMeetingNumbers は答えに実際に使った議事録の番号で、
/// 根拠の表示をここまで絞る。
/// </summary>
public sealed record ChatResult(string Answer, IReadOnlyList<int> UsedMeetingNumbers);

public interface IGeminiContentClient
{
    Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken);

    Task<SelectionResult> SelectAsync(SelectionRequest request, CancellationToken cancellationToken);

    Task<ChatResult> AskAsync(ChatRequest request, CancellationToken cancellationToken);
}
