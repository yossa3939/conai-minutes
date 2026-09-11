namespace ConAI.Web.Services;

public static class MeetingLimits
{
    /// <summary>1 会議ぶんの文字起こし・議事録の上限。フォーム送信では日本語 1 文字が URL エンコードで 9 バイトになるため、<c>FormOptions.ValueLengthLimit</c>（既定 4 MiB）に先に当たらない値にしてある（400,000 × 9 = 3.6 MB）。参考資料の抽出上限（Upload:MaxExtractedTextChars）とは別物。</summary>
    public const int MaxTextChars = 400_000;
}
