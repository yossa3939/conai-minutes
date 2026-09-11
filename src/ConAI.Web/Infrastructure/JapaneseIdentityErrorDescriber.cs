using Microsoft.AspNetCore.Identity;

namespace ConAI.Web.Infrastructure;

/// <summary>
/// ASP.NET Core Identity のエラー文言を日本語で返す。
/// ASP.NET Core は日本語のリソースを同梱していないため、文言そのものを差し替える。
/// </summary>
public sealed class JapaneseIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError() => new()
    {
        Code = nameof(DefaultError),
        Description = "不明なエラーが発生しました。"
    };

    public override IdentityError ConcurrencyFailure() => new()
    {
        Code = nameof(ConcurrencyFailure),
        Description = "ほかの操作と競合しました。画面を開き直して、もう一度お試しください。"
    };

    public override IdentityError PasswordMismatch() => new()
    {
        Code = nameof(PasswordMismatch),
        Description = "パスワードが正しくありません。"
    };

    public override IdentityError InvalidToken() => new()
    {
        Code = nameof(InvalidToken),
        Description = "トークンが正しくありません。はじめからやり直してください。"
    };

    public override IdentityError RecoveryCodeRedemptionFailed() => new()
    {
        Code = nameof(RecoveryCodeRedemptionFailed),
        Description = "回復コードを使用できませんでした。"
    };

    public override IdentityError LoginAlreadyAssociated() => new()
    {
        Code = nameof(LoginAlreadyAssociated),
        Description = "このログイン情報は、すでに別のアカウントに結び付いています。"
    };

    public override IdentityError InvalidUserName(string? userName) => new()
    {
        Code = nameof(InvalidUserName),
        Description = $"ユーザー名「{userName}」は使用できません。英数字だけが使えます。"
    };

    public override IdentityError InvalidEmail(string? email) => new()
    {
        Code = nameof(InvalidEmail),
        Description = $"メールアドレス「{email}」の形式が正しくありません。"
    };

    public override IdentityError DuplicateUserName(string userName) => new()
    {
        Code = nameof(DuplicateUserName),
        Description = $"ユーザー名「{userName}」は、すでに使われています。"
    };

    public override IdentityError DuplicateEmail(string email) => new()
    {
        Code = nameof(DuplicateEmail),
        Description = $"メールアドレス「{email}」は、すでに使われています。"
    };

    public override IdentityError InvalidRoleName(string? role) => new()
    {
        Code = nameof(InvalidRoleName),
        Description = $"ロール名「{role}」は使用できません。"
    };

    public override IdentityError DuplicateRoleName(string role) => new()
    {
        Code = nameof(DuplicateRoleName),
        Description = $"ロール名「{role}」は、すでに使われています。"
    };

    public override IdentityError UserAlreadyHasPassword() => new()
    {
        Code = nameof(UserAlreadyHasPassword),
        Description = "パスワードは、すでに設定されています。"
    };

    public override IdentityError UserLockoutNotEnabled() => new()
    {
        Code = nameof(UserLockoutNotEnabled),
        Description = "このアカウントでは、ロックアウトが有効になっていません。"
    };

    public override IdentityError UserAlreadyInRole(string role) => new()
    {
        Code = nameof(UserAlreadyInRole),
        Description = $"このアカウントは、すでにロール「{role}」に属しています。"
    };

    public override IdentityError UserNotInRole(string role) => new()
    {
        Code = nameof(UserNotInRole),
        Description = $"このアカウントは、ロール「{role}」に属していません。"
    };

    public override IdentityError PasswordTooShort(int length) => new()
    {
        Code = nameof(PasswordTooShort),
        Description = $"パスワードは {length} 文字以上で入力してください。"
    };

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) => new()
    {
        Code = nameof(PasswordRequiresUniqueChars),
        Description = $"パスワードには {uniqueChars} 種類以上の文字を使ってください。"
    };

    public override IdentityError PasswordRequiresNonAlphanumeric() => new()
    {
        Code = nameof(PasswordRequiresNonAlphanumeric),
        Description = "パスワードには記号を 1 文字以上含めてください。"
    };

    public override IdentityError PasswordRequiresDigit() => new()
    {
        Code = nameof(PasswordRequiresDigit),
        Description = "パスワードには数字を 1 文字以上含めてください。"
    };

    public override IdentityError PasswordRequiresLower() => new()
    {
        Code = nameof(PasswordRequiresLower),
        Description = "パスワードには英小文字を 1 文字以上含めてください。"
    };

    public override IdentityError PasswordRequiresUpper() => new()
    {
        Code = nameof(PasswordRequiresUpper),
        Description = "パスワードには英大文字を 1 文字以上含めてください。"
    };
}
