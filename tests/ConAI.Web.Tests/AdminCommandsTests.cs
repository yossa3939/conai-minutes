using ConAI.Web.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

/// <summary>
/// AdminCommands.Parse の引数解釈。DB も UserManager も使わない純粋な単体。
/// </summary>
public class AdminCommandsTests
{
    [Fact]
    public void 引数が空ならnullを返し_hostArgs_も空になる()
    {
        var request = AdminCommands.Parse([], out var hostArgs);

        Assert.Null(request);
        Assert.Empty(hostArgs);
    }

    [Fact]
    public void 先頭が未知の語ならnullを返し_hostArgs_はそのまま残る()
    {
        var args = new[] { "--Storage:DataRoot=x", "余分な語" };

        var request = AdminCommands.Parse(args, out var hostArgs);

        Assert.Null(request);
        Assert.Equal(args, hostArgs);
    }

    [Fact]
    public void reset_password_にメールを渡すとコマンドとメールが入り_hostArgs_は空になる()
    {
        var request = AdminCommands.Parse(["reset-password", "a@example.test"], out var hostArgs);

        Assert.NotNull(request);
        Assert.Equal("reset-password", request.Name);
        Assert.Equal("a@example.test", request.Email);
        Assert.Empty(hostArgs);
    }

    [Fact]
    public void reset_password_の後に設定指定が続いても_hostArgs_からコマンドの語が除かれる()
    {
        var request = AdminCommands.Parse(
            ["reset-password", "a@example.test", "--Storage:DataRoot=x"],
            out var hostArgs);

        Assert.NotNull(request);
        Assert.Equal("a@example.test", request.Email);
        Assert.Equal(new[] { "--Storage:DataRoot=x" }, hostArgs);
    }

    [Fact]
    public void reset_password_単独なら_Email_はnullになる()
    {
        var request = AdminCommands.Parse(["reset-password"], out var hostArgs);

        Assert.NotNull(request);
        Assert.Equal("reset-password", request.Name);
        Assert.Null(request.Email);
        Assert.Empty(hostArgs);
    }

    [Fact]
    public void list_users_なら_Name_に入り_Email_はnullになる()
    {
        var request = AdminCommands.Parse(["list-users"], out var hostArgs);

        Assert.NotNull(request);
        Assert.Equal("list-users", request.Name);
        Assert.Null(request.Email);
        Assert.Empty(hostArgs);
    }
}

/// <summary>
/// AdminCommands.RunAsync の動作。ConAIWebApplicationFactory の DB に対して、
/// UserManager を通した実際のパスワード再設定を確かめる。
/// </summary>
public class AdminCommandsRunTests
{
    /// <summary>出力を List に溜め、ReadPassword は構築時に渡した入力のキューから順に返す。</summary>
    private sealed class RecordingAdminConsole(params string?[] inputs) : IAdminConsole
    {
        public List<string> Lines { get; } = [];

        private readonly Queue<string?> pendingInputs = new(inputs);

        public void WriteLine(string message) => Lines.Add(message);

        public string? ReadPassword(string prompt) =>
            pendingInputs.Count > 0 ? pendingInputs.Dequeue() : null;
    }

    private static async Task<UserManager<IdentityUser>> CreateUserManagerAsync(
        ConAIWebApplicationFactory factory,
        string userId,
        string email)
    {
        await IdentityTestUsers.EnsureAsync(factory, userId, email);

        var scope = factory.CreateScope();
        return scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    }

    [Fact]
    public async Task 実在するメールのパスワードを再設定すると_新しいパスワードで認証できる()
    {
        using var factory = new ConAIWebApplicationFactory();
        var userManager = await CreateUserManagerAsync(factory, "admin-reset-ok", "reset-ok@example.test");
        var console = new RecordingAdminConsole("New-Password-1!", "New-Password-1!");

        var exitCode = await AdminCommands.RunAsync(
            new AdminCommandRequest("reset-password", "reset-ok@example.test"),
            userManager,
            console);

        Assert.Equal(0, exitCode);
        var user = await userManager.FindByEmailAsync("reset-ok@example.test");
        Assert.NotNull(user);
        // 元のパスワードで認証できなくなることも、置き換わったことの証明の一部
        Assert.True(await userManager.CheckPasswordAsync(user, "New-Password-1!"));
        Assert.False(await userManager.CheckPasswordAsync(user, IdentityTestUsers.DefaultPassword));
    }

    [Fact]
    public async Task 存在しないメールなら失敗し_既存ユーザーのパスワードは変わらない()
    {
        using var factory = new ConAIWebApplicationFactory();
        var userManager = await CreateUserManagerAsync(factory, "admin-missing-guard", "guard@example.test");
        var console = new RecordingAdminConsole("New-Password-1!", "New-Password-1!");

        var exitCode = await AdminCommands.RunAsync(
            new AdminCommandRequest("reset-password", "nobody@example.test"),
            userManager,
            console);

        Assert.Equal(1, exitCode);
        var user = await userManager.FindByEmailAsync("guard@example.test");
        Assert.NotNull(user);
        Assert.True(await userManager.CheckPasswordAsync(user, IdentityTestUsers.DefaultPassword));
    }

    [Fact]
    public async Task ポリシーを満たさないパスワードなら失敗し_理由を出して元のままである()
    {
        using var factory = new ConAIWebApplicationFactory();
        var userManager = await CreateUserManagerAsync(factory, "admin-policy", "policy@example.test");
        var console = new RecordingAdminConsole("abc", "abc");

        var exitCode = await AdminCommands.RunAsync(
            new AdminCommandRequest("reset-password", "policy@example.test"),
            userManager,
            console);

        Assert.Equal(1, exitCode);
        // Identity のエラーが JapaneseIdentityErrorDescriber の日本語で出ていること
        Assert.Contains(console.Lines, line => line.Contains("パスワード"));
        var user = await userManager.FindByEmailAsync("policy@example.test");
        Assert.NotNull(user);
        Assert.True(await userManager.CheckPasswordAsync(user, IdentityTestUsers.DefaultPassword));
    }

    [Fact]
    public async Task 確認入力が一致しなければ失敗し_元のパスワードのままである()
    {
        using var factory = new ConAIWebApplicationFactory();
        var userManager = await CreateUserManagerAsync(factory, "admin-mismatch", "mismatch@example.test");
        var console = new RecordingAdminConsole("New-Password-1!", "Different-2!");

        var exitCode = await AdminCommands.RunAsync(
            new AdminCommandRequest("reset-password", "mismatch@example.test"),
            userManager,
            console);

        Assert.Equal(1, exitCode);
        var user = await userManager.FindByEmailAsync("mismatch@example.test");
        Assert.NotNull(user);
        Assert.True(await userManager.CheckPasswordAsync(user, IdentityTestUsers.DefaultPassword));
    }

    [Fact]
    public async Task 入力が中断されたら失敗し_元のパスワードのままである()
    {
        using var factory = new ConAIWebApplicationFactory();
        var userManager = await CreateUserManagerAsync(factory, "admin-cancelled", "cancelled@example.test");
        var console = new RecordingAdminConsole((string?)null);

        var exitCode = await AdminCommands.RunAsync(
            new AdminCommandRequest("reset-password", "cancelled@example.test"),
            userManager,
            console);

        Assert.Equal(1, exitCode);
        var user = await userManager.FindByEmailAsync("cancelled@example.test");
        Assert.NotNull(user);
        Assert.True(await userManager.CheckPasswordAsync(user, IdentityTestUsers.DefaultPassword));
    }

    [Fact]
    public async Task 確認入力が中断されたら中止と表示し_元のパスワードのままである()
    {
        using var factory = new ConAIWebApplicationFactory();
        var userManager = await CreateUserManagerAsync(factory, "admin-confirm-cancelled", "confirm-cancelled@example.test");
        var console = new RecordingAdminConsole("New-Password-1!", (string?)null);

        var exitCode = await AdminCommands.RunAsync(
            new AdminCommandRequest("reset-password", "confirm-cancelled@example.test"),
            userManager,
            console);

        Assert.Equal(1, exitCode);
        // 中断なのに「入力が一致しません。」と問い詰められてはならない
        Assert.Contains("中止しました。", console.Lines);
        Assert.DoesNotContain(console.Lines, line => line.Contains("入力が一致しません"));
        var user = await userManager.FindByEmailAsync("confirm-cancelled@example.test");
        Assert.NotNull(user);
        Assert.True(await userManager.CheckPasswordAsync(user, IdentityTestUsers.DefaultPassword));
    }

    [Fact]
    public async Task list_users_はメールアドレスを出し_パスワードハッシュを出さない()
    {
        using var factory = new ConAIWebApplicationFactory();
        var userManager = await CreateUserManagerAsync(factory, "admin-listed", "listed@example.test");

        var console = new RecordingAdminConsole();
        var exitCode = await AdminCommands.RunAsync(
            new AdminCommandRequest("list-users", null),
            userManager,
            console);

        Assert.Equal(0, exitCode);
        var user = await userManager.FindByEmailAsync("listed@example.test");
        Assert.NotNull(user);
        var hash = user.PasswordHash;
        Assert.False(string.IsNullOrEmpty(hash));
        Assert.Contains("listed@example.test", console.Lines);
        // ハッシュは 1 行たりとも出力に現れてはいけない
        Assert.All(console.Lines, line => Assert.DoesNotContain(hash, line));
    }
}

/// <summary>
/// ConsoleAdminConsole.ReadPassword のキー処理。Console.ReadKey から切り離した
/// Func&lt;ConsoleKeyInfo&gt; 受けのオーバーロードに対して、キー列を順に返すデリゲートで確かめる。
/// </summary>
public class ConsoleAdminConsoleTests
{
    /// <summary>渡したキーを順に返す。キー列が足りなければ Dequeue が例外を出し、テストを失敗させる。</summary>
    private static Func<ConsoleKeyInfo> Keys(params ConsoleKeyInfo[] keys)
    {
        var queue = new Queue<ConsoleKeyInfo>(keys);
        return () => queue.Dequeue();
    }

    [Fact]
    public void Enterで確定するとそれまでに入力した文字列が返る()
    {
        var password = ConsoleAdminConsole.ReadPassword(Keys(
            new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false),
            new ConsoleKeyInfo('b', ConsoleKey.B, false, false, false),
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)));

        Assert.Equal("ab", password);
    }

    [Fact]
    public void Escapeを押すとnullが返る()
    {
        var password = ConsoleAdminConsole.ReadPassword(Keys(
            new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false),
            new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false)));

        Assert.Null(password);
    }

    [Fact]
    public void Backspaceで直前の1文字が消える()
    {
        var password = ConsoleAdminConsole.ReadPassword(Keys(
            new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false),
            new ConsoleKeyInfo('b', ConsoleKey.B, false, false, false),
            new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false),
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)));

        Assert.Equal("a", password);
    }

    [Fact]
    public void 入力が1文字もない状態でBackspaceを押しても落ちず_その後の入力は正しく返る()
    {
        var password = ConsoleAdminConsole.ReadPassword(Keys(
            new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false),
            new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false),
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)));

        Assert.Equal("a", password);
    }

    [Fact]
    public void 制御文字はパスワードに含まれない()
    {
        var password = ConsoleAdminConsole.ReadPassword(Keys(
            new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false),
            new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false),
            new ConsoleKeyInfo('\u0001', ConsoleKey.A, false, false, true),
            new ConsoleKeyInfo('\u007f', ConsoleKey.Delete, false, false, false),
            new ConsoleKeyInfo('b', ConsoleKey.B, false, false, false),
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)));

        Assert.Equal("ab", password);
    }

    [Fact]
    public void KeyCharがNULのキーはパスワードに含まれない()
    {
        var password = ConsoleAdminConsole.ReadPassword(Keys(
            new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false),
            new ConsoleKeyInfo('\0', ConsoleKey.F1, false, false, false),
            new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)));

        Assert.Equal("a", password);
    }
}
