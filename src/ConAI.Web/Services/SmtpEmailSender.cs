using ConAI.Web.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ConAI.Web.Services;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly IOptions<SmtpOptions> _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        var options = _options.Value;
        if (!options.IsConfigured)
        {
            // メール確認は必須ではない（RequireConfirmedAccount = false）ため、未設定を理由に
            // 登録などの操作を落とさない。送れないことに気付けるよう警告だけ残す。
            // 送信の可否が利用者に見える必要がある再設定画面は、呼ぶ前に IsConfigured を確かめている
            _logger.LogWarning("Smtp:Host と Smtp:FromAddress が未設定のため、メールを送信しませんでした。");
            return;
        }

        using var client = new SmtpClient();
        try
        {
            // 宛先アドレスの形式が不正だと MailboxAddress.Parse が落ちる。
            // 失敗のログを確実に残すため、メッセージの組み立ても try の中で行う
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
            message.To.Add(MailboxAddress.Parse(email));
            message.Subject = subject;
            message.Body = new TextPart("html") { Text = htmlMessage };

            // MailKit の Timeout はミリ秒。既定の 120 秒のままだと応答しないサーバに送信が 2 分滞留する
            client.Timeout = options.TimeoutSeconds * 1000;

            await client.ConnectAsync(options.Host, options.Port, ToSecureSocketOptions(options.Security));

            // 認証なしで中継する SMTP サーバがあるため、UserName が空なら認証を省く
            if (!string.IsNullOrWhiteSpace(options.UserName))
            {
                await client.AuthenticateAsync(options.UserName, options.Password);
            }

            await client.SendAsync(message);
        }
        catch (Exception exception)
        {
            // パスワード・本文・宛先アドレスはログに出さない。失敗の事実と宛先ホストまで
            _logger.LogError(exception, "SMTP send failed. Host: {Host}", options.Host);
            throw;
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(true);
            }
        }

        _logger.LogInformation("SMTP send succeeded. Host: {Host}", options.Host);
    }

    /// <summary>設定の Security 文字列を MailKit の暗号化方式へ変換する。大文字小文字は区別しない。</summary>
    public static SecureSocketOptions ToSecureSocketOptions(string security)
    {
        if (string.Equals(security, SmtpOptions.StartTlsSecurity, StringComparison.OrdinalIgnoreCase))
        {
            return SecureSocketOptions.StartTls;
        }

        if (string.Equals(security, SmtpOptions.SslOnConnectSecurity, StringComparison.OrdinalIgnoreCase))
        {
            return SecureSocketOptions.SslOnConnect;
        }

        if (string.Equals(security, SmtpOptions.NoneSecurity, StringComparison.OrdinalIgnoreCase))
        {
            return SecureSocketOptions.None;
        }

        // 到達しないはずの経路。SmtpOptions の検証で弾く前提だが、
        // public な変換なので未知の値は既定値へ黙って落とさない
        throw new ArgumentException(
            $"Smtp:Security は '{SmtpOptions.StartTlsSecurity}'、'{SmtpOptions.SslOnConnectSecurity}'、'{SmtpOptions.NoneSecurity}' のいずれかにしてください。実際の値: '{security}'",
            nameof(security));
    }
}
