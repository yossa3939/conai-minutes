using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ConAI.Web.Infrastructure;

/// <summary>管理コマンドの画面入出力。テストから差し替えるために切り出している。</summary>
public interface IAdminConsole
{
    void WriteLine(string message);

    /// <summary>入力中はエコーしない。入力が中断されたら null を返す。</summary>
    string? ReadPassword(string prompt);
}

/// <summary>
/// コンソールでの入出力。ReadPassword は 1 文字ずつ読み、入力した文字も代用の「*」も
/// 出さない。文字数が画面に漏れるのを避けるため。
/// </summary>
public sealed class ConsoleAdminConsole : IAdminConsole
{
    public void WriteLine(string message) => Console.WriteLine(message);

    public string? ReadPassword(string prompt)
    {
        Console.Write(prompt);
        var password = ReadPassword(() => Console.ReadKey(intercept: true));
        Console.WriteLine();
        return password;
    }

    /// <summary>キー入力からパスワードを組み立てる。Console から切り離してテストできるようにしている。</summary>
    public static string? ReadPassword(Func<ConsoleKeyInfo> readKey)
    {
        var chars = new List<char>();
        while (true)
        {
            var key = readKey();
            if (key.Key == ConsoleKey.Enter)
            {
                return new string([.. chars]);
            }

            if (key.Key == ConsoleKey.Escape)
            {
                return null;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (chars.Count > 0)
                {
                    chars.RemoveAt(chars.Count - 1);
                }
                continue;
            }

            // F キーのように文字を伴わない入力と、Tab・Ctrl+英字・DEL といった制御文字は読み飛ばす。
            // 制御文字を許すと Web のログイン画面から入力できないパスワードが設定できてしまい、
            // ログインできない状態を直すためのコマンドの目的に反する
            if (key.KeyChar is not '\0' && !char.IsControl(key.KeyChar))
            {
                chars.Add(key.KeyChar);
            }
        }
    }
}

/// <summary>解釈済みの管理コマンド。Name は AdminCommands のコマンド名定数のいずれか。</summary>
public sealed record AdminCommandRequest(string Name, string? Email);

public static class AdminCommands
{
    public const string ListUsersCommand = "list-users";
    public const string ResetPasswordCommand = "reset-password";

    /// <summary>
    /// args の先頭が管理コマンドならその要求を返し、コマンドの語を除いた残りを hostArgs へ出す。
    /// 先頭が管理コマンドでなければ null を返し、args をそのまま hostArgs へ渡す。
    /// </summary>
    public static AdminCommandRequest? Parse(string[] args, out string[] hostArgs)
    {
        if (args.Length == 0)
        {
            hostArgs = args;
            return null;
        }

        if (args[0] == ListUsersCommand)
        {
            hostArgs = args[1..];
            return new AdminCommandRequest(ListUsersCommand, null);
        }

        if (args[0] == ResetPasswordCommand)
        {
            // 第 2 語が「-」で始まるならメールではなく設定指定と見て、コマンドの語だけ除く
            if (args.Length >= 2 && !args[1].StartsWith('-'))
            {
                hostArgs = args[2..];
                return new AdminCommandRequest(ResetPasswordCommand, args[1]);
            }

            hostArgs = args[1..];
            return new AdminCommandRequest(ResetPasswordCommand, null);
        }

        hostArgs = args;
        return null;
    }

    /// <summary>管理コマンドを実行し、プロセスの終了コード（成功 0 / 失敗 1）を返す。</summary>
    public static async Task<int> RunAsync(
        AdminCommandRequest request,
        UserManager<IdentityUser> userManager,
        IAdminConsole console)
    {
        switch (request.Name)
        {
            case ListUsersCommand:
                return await ListUsersAsync(userManager, console);
            case ResetPasswordCommand:
                return await ResetPasswordAsync(request, userManager, console);
            default:
                return 1;
        }
    }

    private static async Task<int> ListUsersAsync(UserManager<IdentityUser> userManager, IAdminConsole console)
    {
        var users = await userManager.Users.ToListAsync();
        if (users.Count == 0)
        {
            console.WriteLine("登録されているユーザーはいません。");
            return 0;
        }

        foreach (var user in users)
        {
            console.WriteLine(user.Email ?? user.UserName ?? user.Id);
        }

        console.WriteLine($"{users.Count} 件のユーザーが登録されています。");
        return 0;
    }

    private static async Task<int> ResetPasswordAsync(
        AdminCommandRequest request,
        UserManager<IdentityUser> userManager,
        IAdminConsole console)
    {
        if (string.IsNullOrEmpty(request.Email))
        {
            console.WriteLine("使い方: dotnet run --project src/ConAI.Web -- reset-password <メールアドレス>");
            return 1;
        }

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            console.WriteLine($"{request.Email} は登録されていません。");
            console.WriteLine("list-users で登録済みのメールアドレスを確認できます。");
            return 1;
        }

        var password = console.ReadPassword("新しいパスワード: ");
        if (string.IsNullOrEmpty(password))
        {
            console.WriteLine("中止しました。");
            return 1;
        }

        var confirmation = console.ReadPassword("もう一度入力してください: ");
        if (string.IsNullOrEmpty(confirmation))
        {
            console.WriteLine("中止しました。");
            return 1;
        }

        if (confirmation != password)
        {
            console.WriteLine("入力が一致しません。変更していません。");
            return 1;
        }

        // ハッシュを直接書き換えず、UserManager の正規の経路を通す。
        // パスワードポリシーと JapaneseIdentityErrorDescriber の文言を効かせるため
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                console.WriteLine(error.Description);
            }

            return 1;
        }

        console.WriteLine($"{request.Email} のパスワードを再設定しました。");
        return 0;
    }
}
