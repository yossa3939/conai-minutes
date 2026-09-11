using System.ComponentModel.DataAnnotations;

namespace ConAI.Web.Configuration;

public sealed class SmtpOptions : IValidatableObject
{
    public const string SectionName = "Smtp";

    public const string StartTlsSecurity = "StartTls";
    public const string SslOnConnectSecurity = "SslOnConnect";
    public const string NoneSecurity = "None";

    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;

    // MailKit 既定の 120 秒は応答しないサーバを前に長すぎる。Notifications:TimeoutSeconds（10）は
    // 接続 → TLS ハンドシェイク → 認証 → 送信の多段には短いため、その中間の 30 を既定にする
    public int TimeoutSeconds { get; set; } = 30;

    public string Security { get; set; } = StartTlsSecurity;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "議事録";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var hasHost = !string.IsNullOrWhiteSpace(Host);
        var hasFromAddress = !string.IsNullOrWhiteSpace(FromAddress);

        // メールを使わない運用があるため、未設定（両方空）はエラーにしない。
        // 片方だけの半端な設定は送信時に必ず失敗するので、起動時に止める
        if (hasHost != hasFromAddress)
        {
            yield return new ValidationResult(
                "Smtp:Host と Smtp:FromAddress は両方を設定してください。",
                new[] { nameof(Host), nameof(FromAddress) });
            yield break;
        }

        if (!IsConfigured)
        {
            yield break;
        }

        if (Port is < 1 or > 65535)
        {
            yield return new ValidationResult(
                $"Smtp:Port は 1〜65535 の範囲で設定してください。実際の値: '{Port}'",
                new[] { nameof(Port) });
        }

        if (TimeoutSeconds is < 1 or > 300)
        {
            yield return new ValidationResult(
                $"Smtp:TimeoutSeconds は 1〜300 の範囲で設定してください。実際の値: '{TimeoutSeconds}'",
                new[] { nameof(TimeoutSeconds) });
        }

        if (!IsKnownSecurity(Security))
        {
            yield return new ValidationResult(
                $"Smtp:Security は '{StartTlsSecurity}'、'{SslOnConnectSecurity}'、'{NoneSecurity}' のいずれかにしてください。実際の値: '{Security}'",
                new[] { nameof(Security) });
        }

        // None は暗号化なしで送る設定のため、認証情報を渡すと平文で流れる。
        // 認証なしで中継するサーバ（UserName が空）は正しい構成なので弾かない
        if (string.Equals(Security, NoneSecurity, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(UserName))
        {
            yield return new ValidationResult(
                $"Smtp:Security が '{NoneSecurity}' のときは Smtp:UserName を設定しないでください。認証情報を平文のまま送ることになります。",
                new[] { nameof(Security), nameof(UserName) });
        }

        // FromAddress に [EmailAddress] 属性を付けない。未設定運用の空文字で落ちるため、
        // 設定済みのときだけここで形式を見て、打ち間違いを起動時に止める
        if (!new EmailAddressAttribute().IsValid(FromAddress))
        {
            yield return new ValidationResult(
                $"Smtp:FromAddress はメールアドレスの形式で設定してください。実際の値: '{FromAddress}'",
                new[] { nameof(FromAddress) });
        }
    }

    private bool IsKnownSecurity(string value) =>
        string.Equals(value, StartTlsSecurity, StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, SslOnConnectSecurity, StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, NoneSecurity, StringComparison.OrdinalIgnoreCase);
}
