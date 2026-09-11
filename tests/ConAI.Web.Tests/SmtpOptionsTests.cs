using System.ComponentModel.DataAnnotations;
using ConAI.Web.Configuration;

namespace ConAI.Web.Tests;

public class SmtpOptionsTests
{
    private static List<ValidationResult> Validate(SmtpOptions options) =>
        options.Validate(new ValidationContext(options)).ToList();

    [Fact]
    public void HostとFromAddressが両方空なら検証を通り未設定扱いになる()
    {
        var options = new SmtpOptions();

        var results = Validate(options);

        Assert.Empty(results);
        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void Hostだけ設定すると検証に失敗する()
    {
        var options = new SmtpOptions { Host = "smtp.example.com" };

        var results = Validate(options);

        Assert.Contains(results, r => r.ErrorMessage!.Contains("Smtp:Host と Smtp:FromAddress"));
    }

    [Fact]
    public void FromAddressだけ設定すると検証に失敗する()
    {
        var options = new SmtpOptions { FromAddress = "from@example.com" };

        var results = Validate(options);

        Assert.Contains(results, r => r.ErrorMessage!.Contains("Smtp:Host と Smtp:FromAddress"));
    }

    [Fact]
    public void HostとFromAddressを設定すると検証を通り設定済み扱いになる()
    {
        var options = new SmtpOptions { Host = "smtp.example.com", FromAddress = "from@example.com" };

        var results = Validate(options);

        Assert.Empty(results);
        Assert.True(options.IsConfigured);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(70000)]
    public void ポート番号が範囲外なら検証に失敗する(int port)
    {
        var options = new SmtpOptions
        {
            Host = "smtp.example.com",
            FromAddress = "from@example.com",
            Port = port
        };

        var results = Validate(options);

        Assert.Contains(results, r => r.ErrorMessage!.Contains("Smtp:Port"));
    }

    [Fact]
    public void TimeoutSecondsの既定は30で検証を通る()
    {
        // MailKit 既定の 120 秒より短くするための値のため、既定のままでも検証に落ちない
        var options = new SmtpOptions
        {
            Host = "smtp.example.com",
            FromAddress = "from@example.com"
        };

        var results = Validate(options);

        Assert.Empty(results);
        Assert.Equal(30, options.TimeoutSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(301)]
    public void タイムアウト秒数が範囲外なら検証に失敗する(int timeoutSeconds)
    {
        var options = new SmtpOptions
        {
            Host = "smtp.example.com",
            FromAddress = "from@example.com",
            TimeoutSeconds = timeoutSeconds
        };

        var results = Validate(options);

        Assert.Contains(results, r => r.ErrorMessage!.Contains("Smtp:TimeoutSeconds"));
    }

    [Fact]
    public void 未設定運用ならタイムアウト秒数が範囲外でも検証を通る()
    {
        // Port と同じ扱い。メールを使わない運用のために未設定では検証しない
        var options = new SmtpOptions { TimeoutSeconds = 0 };

        var results = Validate(options);

        Assert.Empty(results);
    }

    [Fact]
    public void Securityが未知の値なら検証に失敗する()
    {
        var options = new SmtpOptions
        {
            Host = "smtp.example.com",
            FromAddress = "from@example.com",
            Security = "Ftp"
        };

        var results = Validate(options);

        Assert.Contains(results, r => r.ErrorMessage!.Contains("Smtp:Security"));
    }

    [Fact]
    public void Securityは大文字小文字を無視して受け付ける()
    {
        var options = new SmtpOptions
        {
            Host = "smtp.example.com",
            FromAddress = "from@example.com",
            Security = "starttls"
        };

        var results = Validate(options);

        Assert.Empty(results);
    }

    [Fact]
    public void パスワードは空でも検証を通る()
    {
        // 認証なしで中継する SMTP サーバがあるため、パスワードの有無は検証しない
        var options = new SmtpOptions
        {
            Host = "smtp.example.com",
            FromAddress = "from@example.com",
            Password = ""
        };

        var results = Validate(options);

        Assert.Empty(results);
    }

    [Fact]
    public void SecurityがNoneでUserNameに値があれば検証に失敗する()
    {
        // None は暗号化なしで送る設定のため、認証情報を渡す構成は資格情報が
        // 平文で流れる。起動時に止めないと送信時まで気付けない
        var options = new SmtpOptions
        {
            Host = "smtp.example.com",
            FromAddress = "from@example.com",
            Security = "None",
            UserName = "user@example.com"
        };

        var results = Validate(options);

        // Security の値そのものが未知のときのメッセージにも "Smtp:Security" は出るため、
        // この規則だけが出す "Smtp:UserName" で照合する
        Assert.Contains(results, r => r.ErrorMessage!.Contains("Smtp:UserName"));
    }

    [Fact]
    public void SecurityがNoneでもUserNameが空なら検証を通る()
    {
        // 認証なしで中継する SMTP サーバは None と UserName 空の組み合わせが正しい
        var options = new SmtpOptions
        {
            Host = "smtp.example.com",
            FromAddress = "from@example.com",
            Security = "None",
            UserName = ""
        };

        var results = Validate(options);

        Assert.Empty(results);
    }

    [Fact]
    public void FromAddressがメールアドレスの形式でなければ検証に失敗する()
    {
        // 打ち間違いを送信時ではなく起動時に止める
        var options = new SmtpOptions
        {
            Host = "smtp.example.com",
            FromAddress = "not-an-email"
        };

        var results = Validate(options);

        Assert.Contains(results, r => r.ErrorMessage!.Contains("Smtp:FromAddress"));
    }
}
