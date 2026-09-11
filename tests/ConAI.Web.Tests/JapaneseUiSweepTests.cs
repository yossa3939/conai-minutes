using System.Net;
using System.Text.RegularExpressions;

namespace ConAI.Web.Tests;

public class JapaneseUiSweepTests
{
    /// <summary>
    /// ASP.NET Core Identity の既定ページが持つ英語の定型文。
    /// 識別子や URL に当たらないよう、空白を含む 2 語以上だけを並べる。
    /// </summary>
    private static readonly string[] EnglishPhrases =
    [
        "Log in",
        "Log out",
        "Sign in",
        "Remember me",
        "Forgot your password",
        "Resend email confirmation",
        "Use another service",
        "Create a new account",
        "Register as a new user",
        "Manage your account",
        "Change your account settings",
        "Set your password",
        "Add a new password",
        "You do not have a local username",
        "Change password",
        "New password",
        "Current password",
        "Confirm password",
        "Two-factor authentication",
        "Set up authenticator app",
        "Scan the QR Code",
        "Learn how to enable QR code generation",
        "Reset authenticator key",
        "Generate recovery codes",
        "Recovery codes",
        "Put the recovery codes in a safe place",
        "If you lose your device",
        "Forget this browser",
        "Personal Data",
        "Delete your account",
        "This action cannot be undone",
        "Download your data",
        "External logins",
        "Associated Logins",
        "Add another service to log in",
        "Please check your email",
        "Thank you for confirming",
        "has been reset",
        "Access denied",
        "You do not have access",
        "locked out",
        "successfully logged out",
        "An error occurred while processing your request",
        "Request ID",
        "Development Mode",
        "Privacy Policy",
        "field is required",
        "must be at least",
        "do not match"
    ];

    /// <summary>
    /// Bootstrap にしか無いクラス名とファイル名。
    /// `btn-` や `text-muted` のような接頭辞で探すと、こちらの btn-primary や
    /// Tailwind の text-muted-foreground に当たるので、完全な名前で並べる。
    /// </summary>
    private static readonly string[] BootstrapTokens =
    [
        "bootstrap",
        "btn btn-",
        "btn-lg",
        "btn-sm",
        "btn-close",
        "btn-outline",
        "btn-danger",
        "btn-success",
        "btn-info",
        "btn-warning",
        "form-control",
        "form-floating",
        "form-check",
        "form-group",
        "form-select",
        "text-danger",
        "col-md-",
        "col-sm-",
        "col-lg-",
        "container-fluid",
        "navbar",
        "nav-item",
        "nav-pills",
        "nav-tabs",
        "list-group",
        "alert alert-",
        "data-bs-",
        "d-none",
        "ConAI.Web.styles.css",
        "site.css",
        "site.js"
    ];

    /// <summary>画面に英語が混ざらないよう、描画結果に残ってはいけない語。</summary>
    private static readonly string[] RenderedEnglish =
    [
        "Hello",
        "Logout",
        "Login",
        "Register",
        "Forgot your password",
        "Error.",
        "Privacy",
        "field is required"
    ];

    [Fact]
    public void 画面のテンプレートに英語の文が残っていない()
    {
        var hits = new List<string>();

        foreach (var page in RazorPages())
        {
            var source = File.ReadAllText(page);
            hits.AddRange(EnglishPhrases
                .Where(phrase => source.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                .Select(phrase => $"{Relative(page)}: {phrase}"));
        }

        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));
    }

    /// <summary>
    /// StatusMessage はテンプレートではなく code-behind の文字列がそのまま画面に出る。
    /// 定型句リストを code-behind の全文に当てると開発者向けのログ・例外・コメントに
    /// 誤検知するため、ここでは「利用者に見える文字列は日本語を含む」ことを直接求める。
    /// </summary>
    [Fact]
    public void コードビハインドの_StatusMessage_が日本語で書かれている()
    {
        var hits = new List<string>();

        foreach (var file in CodeBehinds())
        {
            var source = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(source, @"StatusMessage\s*=\s*\$?""([^""]+)"""))
            {
                var message = match.Groups[1].Value;
                if (!Regex.IsMatch(message, @"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]"))
                {
                    hits.Add($"{Relative(file)}: {message}");
                }
            }
        }

        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));
    }

    [Fact]
    public void Bootstrap_の痕跡が残っていない()
    {
        var hits = new List<string>();

        foreach (var file in BootstrapScanTargets())
        {
            var source = File.ReadAllText(file);
            hits.AddRange(BootstrapTokens
                .Where(token => source.Contains(token, StringComparison.OrdinalIgnoreCase))
                .Select(token => $"{Relative(file)}: {token}"));
        }

        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));

        var removed = new[]
        {
            Path.Combine(WebRoot(), "wwwroot", "lib", "bootstrap"),
            Path.Combine(WebRoot(), "wwwroot", "css", "site.css"),
            Path.Combine(WebRoot(), "wwwroot", "css", "conai.css"),
            Path.Combine(WebRoot(), "wwwroot", "js", "site.js"),
            Path.Combine(WebRoot(), "Pages", "Shared", "_Layout.cshtml.css")
        };

        Assert.DoesNotContain(removed, path => File.Exists(path) || Directory.Exists(path));

        Assert.True(File.Exists(Path.Combine(
            WebRoot(), "wwwroot", "lib", "jquery", "dist", "jquery.min.js")));
    }

    [Fact]
    public void 画面のテンプレートにインラインのスタイルとスクリプトが無い()
    {
        var hits = new List<string>();

        foreach (var page in RazorPages())
        {
            var source = File.ReadAllText(page);

            if (Regex.IsMatch(source, @"\sstyle\s*="))
            {
                hits.Add($"{Relative(page)}: style 属性");
            }

            if (Regex.IsMatch(source, @"<style\b", RegexOptions.IgnoreCase))
            {
                hits.Add($"{Relative(page)}: <style> 要素");
            }

            if (Regex.IsMatch(source, @"<script(?![^>]*\ssrc\s*=)[^>]*>", RegexOptions.IgnoreCase))
            {
                hits.Add($"{Relative(page)}: src の無い <script>");
            }

            if (Regex.IsMatch(source, @"<(link|script)[^>]*\s(href|src)\s*=\s*[""']https?://", RegexOptions.IgnoreCase))
            {
                hits.Add($"{Relative(page)}: 外部 URL の読み込み");
            }
        }

        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));

        var appCss = File.ReadAllText(Path.Combine(WebRoot(), "Styles", "app.css"));
        Assert.DoesNotContain("fonts.googleapis.com", appCss);
        Assert.DoesNotContain("@import url(", appCss);
    }

    [Fact]
    public async Task 代表的な画面の本文に英語の定型文が残っていない()
    {
        string[] anonymous = ["/", "/Identity/Account/Login"];
        string[] authenticated =
        [
            "/Meetings",
            "/Meetings/Create",
            "/Templates",
            "/Templates/Create",
            "/Notifications",
            "/Notifications/Create",
            "/Identity/Account/Manage/Index",
            "/Identity/Account/Manage/ChangePassword"
        ];

        using var factory = new ConAIWebApplicationFactory();
        await IdentityTestUsers.EnsureAsync(factory, "sweep-a");

        var hits = new List<string>();

        using (var client = factory.CreateClient())
        {
            foreach (var path in anonymous)
            {
                hits.AddRange(await CollectAsync(client, path));
            }
        }

        using (var client = factory.CreateClientAs("sweep-a"))
        {
            foreach (var path in authenticated)
            {
                hits.AddRange(await CollectAsync(client, path));
            }
        }

        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));
    }

    [Fact]
    public async Task ログイン画面にメール確認の再送への導線が無い()
    {
        using var factory = new ConAIWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/Identity/Account/Login");

        // パスワード再設定への導線は PasswordResetPageTests が検査する。
        // メール確認の再送は未設定環境で完了しない操作のため、導線は出さないままにする
        Assert.DoesNotContain("ResendEmailConfirmation", html);
    }

    [Fact]
    public void 塗りボタンの文字と背景の対比が_4_5_対_1_以上ある()
    {
        var appCss = File.ReadAllText(Path.Combine(WebRoot(), "Styles", "app.css"));

        var background = HexOf(appCss, "--color-primary");
        var foreground = HexOf(appCss, "--color-on-primary");

        Assert.Equal("#0F766E", background);
        Assert.Equal("#FFFFFF", foreground);
        Assert.True(ContrastRatio(background, foreground) >= 4.5);
    }

    private static async Task<IEnumerable<string>> CollectAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        var found = new List<string>();

        if (!html.Contains("<html lang=\"ja\">", StringComparison.Ordinal))
        {
            found.Add($"{path}: <html lang=\"ja\"> が無い");
        }

        var text = MainText(html);
        found.AddRange(RenderedEnglish
            .Where(phrase => text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .Select(phrase => $"{path}: {phrase}"));

        return found;
    }

    /// <summary>
    /// main 要素の中からタグと属性を落として、読者の目に入る文字だけを取り出す。
    /// href="/Identity/Account/Login" のような URL は属性なので、ここで消える。
    /// </summary>
    private static string MainText(string html)
    {
        var start = html.IndexOf("<main", StringComparison.OrdinalIgnoreCase);
        var openEnd = start < 0 ? -1 : html.IndexOf('>', start);
        var end = openEnd < 0 ? -1 : html.IndexOf("</main>", openEnd, StringComparison.OrdinalIgnoreCase);
        Assert.True(end > openEnd, "main 要素が見つからない");

        var inner = html[(openEnd + 1)..end];
        var withoutBlocks = Regex.Replace(
            inner,
            @"<(script|style)\b.*?</\1>",
            " ",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var withoutTags = Regex.Replace(withoutBlocks, "<[^>]*>", " ");
        return WebUtility.HtmlDecode(withoutTags);
    }

    private static string HexOf(string css, string variable)
    {
        var match = Regex.Match(css, Regex.Escape(variable) + @":\s*(#[0-9A-Fa-f]{6})\s*;");
        Assert.True(match.Success, $"{variable} が app.css に無い");
        return match.Groups[1].Value.ToUpperInvariant();
    }

    private static double ContrastRatio(string a, string b)
    {
        var first = RelativeLuminance(a);
        var second = RelativeLuminance(b);
        var lighter = Math.Max(first, second);
        var darker = Math.Min(first, second);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string hex) =>
        (0.2126 * Channel(hex, 1)) + (0.7152 * Channel(hex, 3)) + (0.0722 * Channel(hex, 5));

    private static double Channel(string hex, int offset)
    {
        var value = Convert.ToInt32(hex.Substring(offset, 2), 16) / 255.0;
        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static IEnumerable<string> RazorPages() =>
        Directory
            .EnumerateFiles(WebRoot(), "*.cshtml", SearchOption.AllDirectories)
            .Where(IsTracked)
            .OrderBy(path => path, StringComparer.Ordinal);

    private static IEnumerable<string> CodeBehinds() =>
        Directory
            .EnumerateFiles(WebRoot(), "*.cshtml.cs", SearchOption.AllDirectories)
            .Where(IsTracked)
            .OrderBy(path => path, StringComparer.Ordinal);

    private static IEnumerable<string> BootstrapScanTargets()
    {
        foreach (var page in RazorPages())
        {
            yield return page;
        }

        yield return Path.Combine(WebRoot(), "Styles", "app.css");
        yield return Path.Combine(WebRoot(), "ConAI.Web.csproj");

        var scripts = Path.Combine(WebRoot(), "wwwroot", "js");
        foreach (var script in Directory
            .EnumerateFiles(scripts, "*.js", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            yield return script;
        }
    }

    /// <summary>ビルド生成物と依存パッケージを除く。走査の対象はリポジトリが持つファイルだけ。</summary>
    private static bool IsTracked(string path)
    {
        var relative = Relative(path);
        return !relative.Contains("/bin/", StringComparison.Ordinal)
            && !relative.Contains("/obj/", StringComparison.Ordinal)
            && !relative.Contains("/node_modules/", StringComparison.Ordinal);
    }

    private static string Relative(string path) =>
        Path.GetRelativePath(RepositoryRoot(), path).Replace(Path.DirectorySeparatorChar, '/');

    private static string WebRoot() => Path.Combine(RepositoryRoot(), "src", "ConAI.Web");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ConAI.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
