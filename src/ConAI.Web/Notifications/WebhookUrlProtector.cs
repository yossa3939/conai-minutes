using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace ConAI.Web.Notifications;

public interface IWebhookUrlProtector
{
    string Protect(string url);

    /// <summary>復号できたら true。鍵を失った 1 件だけを無効化したいので、例外ではなく false を返す。</summary>
    bool TryUnprotect(string protectedUrl, out string url);

    /// <summary>同じ宛先かどうかを DB 側で見るための指紋。</summary>
    string Fingerprint(string url);
}

public sealed class WebhookUrlProtector : IWebhookUrlProtector
{
    public const string Purpose = "ConAI.Webhook.Url";

    private readonly IDataProtector _protector;

    public WebhookUrlProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Protect(string url) => _protector.Protect(url);

    public bool TryUnprotect(string protectedUrl, out string url)
    {
        try
        {
            url = _protector.Unprotect(protectedUrl);
            return true;
        }
        catch (CryptographicException)
        {
            url = string.Empty;
            return false;
        }
    }

    // 鍵付きハッシュにしない。鍵を失ったときに復号だけでなく重複判定まで壊れると、
    // 同じ宛先を登録し直せなくなる。
    public string Fingerprint(string url) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
}
