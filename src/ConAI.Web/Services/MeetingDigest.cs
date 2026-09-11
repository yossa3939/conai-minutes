namespace ConAI.Web.Services;

/// <summary>
/// 1 段目に渡す会議 1 件ぶんのダイジェスト。
/// Gemini のクライアントはこの型を受け取らない。作るのは MinutesSelector、読むのは PromptService である。
/// </summary>
public sealed record MeetingDigest(int Number, string Title, DateTime? HeldAt, string Excerpt);
