using ConAI.Web.Gemini;
using Google.GenAI.Types;

namespace ConAI.Web.Tests;

/// <summary>
/// Gemini Live 接続設定の組み立てを検証する。
/// Fake プロバイダは Translate が真なら無条件に Translation イベントを流すため
/// 設定の欠落を検出できず、純粋関数の戻り値を直接検査する。
/// </summary>
public sealed class LiveConnectConfigTests
{
    private static LiveSessionSettings NormalSettings() =>
        new("gemini-live-model", Translate: false, TargetLanguage: "en");

    private static LiveSessionSettings TranslationSettings(string targetLanguage = "en") =>
        new("gemini-live-translate-model", Translate: true, TargetLanguage: targetLanguage);

    [Fact]
    public void 通常モードは応答モダリティがAudioで入力文字起こしが有効()
    {
        var config = GoogleGeminiLiveClient.BuildConfig(NormalSettings(), useSessionResumption: false, resumptionHandle: null);

        Assert.NotNull(config.ResponseModalities);
        var modality = Assert.Single(config.ResponseModalities);
        Assert.Equal(Modality.Audio, modality);
        Assert.NotNull(config.InputAudioTranscription);
    }

    [Fact]
    public void 通常モードはMaxOutputTokensが1()
    {
        var config = GoogleGeminiLiveClient.BuildConfig(NormalSettings(), useSessionResumption: false, resumptionHandle: null);

        Assert.Equal(1, config.MaxOutputTokens);
    }

    [Fact]
    public void 通常モードはSystemInstructionを持つ()
    {
        var config = GoogleGeminiLiveClient.BuildConfig(NormalSettings(), useSessionResumption: false, resumptionHandle: null);

        Assert.NotNull(config.SystemInstruction);
        var text = string.Concat(config.SystemInstruction.Parts?.Select(part => part.Text) ?? []);
        Assert.Contains("書き起", text);
    }

    [Fact]
    public void 通常モードはコンテキスト圧縮を有効にする()
    {
        var config = GoogleGeminiLiveClient.BuildConfig(NormalSettings(), useSessionResumption: false, resumptionHandle: null);

        Assert.NotNull(config.ContextWindowCompression);
        Assert.NotNull(config.ContextWindowCompression.SlidingWindow);
    }

    [Fact]
    public void 通常モードは自動の発話区間検出を無効にする()
    {
        var config = GoogleGeminiLiveClient.BuildConfig(NormalSettings(), useSessionResumption: false, resumptionHandle: null);

        Assert.NotNull(config.RealtimeInputConfig);
        Assert.NotNull(config.RealtimeInputConfig.AutomaticActivityDetection);
        Assert.True(config.RealtimeInputConfig.AutomaticActivityDetection.Disabled);
    }

    [Fact]
    public void 通常モードは出力文字起こしと翻訳設定を持たない()
    {
        var config = GoogleGeminiLiveClient.BuildConfig(NormalSettings(), useSessionResumption: false, resumptionHandle: null);

        Assert.Null(config.OutputAudioTranscription);
        Assert.Null(config.TranslationConfig);
    }

    [Fact]
    public void 翻訳モードは出力文字起こしを有効にする()
    {
        var config = GoogleGeminiLiveClient.BuildConfig(TranslationSettings(), useSessionResumption: false, resumptionHandle: null);

        Assert.NotNull(config.OutputAudioTranscription);
    }

    [Fact]
    public void 翻訳モードは圧縮とSystemInstructionとMaxOutputTokensを付けない()
    {
        var config = GoogleGeminiLiveClient.BuildConfig(TranslationSettings(), useSessionResumption: false, resumptionHandle: null);

        Assert.Null(config.ContextWindowCompression);
        Assert.Null(config.SystemInstruction);
        Assert.Null(config.MaxOutputTokens);
    }

    [Fact]
    public void 翻訳モードはRealtimeInputConfigを付けない()
    {
        var config = GoogleGeminiLiveClient.BuildConfig(TranslationSettings(), useSessionResumption: false, resumptionHandle: null);

        Assert.Null(config.RealtimeInputConfig);
    }

    [Fact]
    public void 翻訳モードは翻訳先言語をTranslationConfigに渡す()
    {
        var config = GoogleGeminiLiveClient.BuildConfig(TranslationSettings("en"), useSessionResumption: false, resumptionHandle: null);

        Assert.NotNull(config.TranslationConfig);
        Assert.Equal("en", config.TranslationConfig.TargetLanguageCode);
    }

    [Fact]
    public void セッション継ぎ足しが無効ならSessionResumptionを付けない()
    {
        var config = GoogleGeminiLiveClient.BuildConfig(NormalSettings(), useSessionResumption: false, resumptionHandle: null);

        Assert.Null(config.SessionResumption);
    }

    [Fact]
    public void セッション継ぎ足しが有効ならハンドルを渡す()
    {
        var config = GoogleGeminiLiveClient.BuildConfig(NormalSettings(), useSessionResumption: true, resumptionHandle: "h1");

        Assert.NotNull(config.SessionResumption);
        Assert.Equal("h1", config.SessionResumption.Handle);
    }
}
