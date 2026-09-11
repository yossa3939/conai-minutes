using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace ConAI.Web.Notifications;

public static class WebhookJson
{
    /// <summary>
    /// 既定の encoder は日本語を \uXXXX に変換する。
    /// JSON としては正しいが、送信内容を目で確かめるときに読めないので素の文字で送る。
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        // リンクが無いときに "url": null を送ると、Discord は 400 を返す
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
