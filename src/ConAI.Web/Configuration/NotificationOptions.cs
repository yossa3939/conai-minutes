using System.ComponentModel.DataAnnotations;

namespace ConAI.Web.Configuration;

public sealed class NotificationOptions : IValidatableObject
{
    public const string SectionName = "Notifications";
    public const string HttpProvider = "Http";
    public const string FakeProvider = "Fake";

    public string Provider { get; set; } = HttpProvider;

    /// <summary>通知に載せるリンクの基点。空なら通知にリンクを入れない。</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>通知に載せる議事録冒頭の文字数。0 なら抜粋を載せない。</summary>
    [Range(0, 4000)]
    public int ExcerptChars { get; set; } = 1500;

    [Range(0, 5)]
    public int MaxRetries { get; set; } = 3;

    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 10;

    [Range(1, 50)]
    public int MaxEndpointsPerUser { get; set; } = 10;

    /// <summary>
    /// 送信ワーカーが同時に扱う会議の数。
    /// 1 件の会議が再送を繰り返しても、後ろに並んだ他の利用者の通知を止めないための幅。
    /// </summary>
    [Range(1, 32)]
    public int MaxConcurrentMeetings { get; set; } = 4;

    public bool IsFake => string.Equals(Provider, FakeProvider, StringComparison.OrdinalIgnoreCase);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var isHttp = string.Equals(Provider, HttpProvider, StringComparison.OrdinalIgnoreCase);
        if (!isHttp && !IsFake)
        {
            yield return new ValidationResult(
                $"Notifications:Provider は '{HttpProvider}' または '{FakeProvider}' のいずれかにしてください。実際の値: '{Provider}'",
                new[] { nameof(Provider) });
            yield break;
        }

        // 空は「リンクを入れない」という有効な設定なので通す。値があるときだけ形を見る。
        if (string.IsNullOrWhiteSpace(PublicBaseUrl))
        {
            yield break;
        }

        if (!Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            yield return new ValidationResult(
                $"Notifications:PublicBaseUrl は http または https の絶対 URL にしてください。実際の値: '{PublicBaseUrl}'",
                new[] { nameof(PublicBaseUrl) });
        }
    }
}
