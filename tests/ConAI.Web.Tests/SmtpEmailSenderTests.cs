using ConAI.Web.Configuration;
using ConAI.Web.Services;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Tests;

public class SmtpEmailSenderTests
{
    /// <summary>記録するだけのロガー。未設定のときに警告が残ることを確かめるために使う。</summary>
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    [Fact]
    public async Task 未設定なら送らずに警告を残す()
    {
        // メール確認は必須ではない（RequireConfirmedAccount = false）ので、未設定でも
        // 登録などの操作を落とさない。ただし黙って捨てると気付けないため警告は残す
        var logger = new RecordingLogger<SmtpEmailSender>();
        var sender = new SmtpEmailSender(Options.Create(new SmtpOptions()), logger);

        await sender.SendEmailAsync("to@example.com", "件名", "<p>本文</p>");

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("Smtp", entry.Message);
    }

    [Theory]
    [InlineData("StartTls", SecureSocketOptions.StartTls)]
    [InlineData("SslOnConnect", SecureSocketOptions.SslOnConnect)]
    [InlineData("None", SecureSocketOptions.None)]
    [InlineData("starttls", SecureSocketOptions.StartTls)]
    public void Securityの文字列はSecureSocketOptionsへ変換される(string security, SecureSocketOptions expected)
    {
        Assert.Equal(expected, SmtpEmailSender.ToSecureSocketOptions(security));
    }

    [Fact]
    public void Securityが未知の値なら変換時に例外になる()
    {
        Assert.ThrowsAny<ArgumentException>(() => SmtpEmailSender.ToSecureSocketOptions("Ftp"));
    }
}
