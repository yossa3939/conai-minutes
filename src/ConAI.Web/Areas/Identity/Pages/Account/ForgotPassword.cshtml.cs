// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using ConAI.Web.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ForgotPasswordModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly IOptions<SmtpOptions> _smtpOptions;
        private readonly ILogger<ForgotPasswordModel> _logger;

        public ForgotPasswordModel(
            UserManager<IdentityUser> userManager,
            IEmailSender emailSender,
            IOptions<SmtpOptions> smtpOptions,
            ILogger<ForgotPasswordModel> logger)
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _smtpOptions = smtpOptions;
            _logger = logger;
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [BindProperty]
        public InputModel Input { get; set; }

        /// <summary>フォームを出すか、未設定の案内を出すかを画面が判定するための設定状態。</summary>
        public bool IsMailConfigured { get; private set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public class InputModel
        {
            [Required(ErrorMessage = "メールアドレスを入力してください。")]
            [EmailAddress(ErrorMessage = "メールアドレスの形式が正しくありません。")]
            [Display(Name = "メールアドレス")]
            public string Email { get; set; }
        }

        public void OnGet()
        {
            IsMailConfigured = _smtpOptions.Value.IsConfigured;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            IsMailConfigured = _smtpOptions.Value.IsConfigured;

            // 画面のフォームは未設定なら出ないが、POST を直接叩かれることもある。
            // 送信できなければ確認画面の文言が嘘になるため、ここでも止める
            if (!IsMailConfigured)
            {
                return Page();
            }

            if (ModelState.IsValid)
            {
                var user = await _userManager.FindByEmailAsync(Input.Email);
                if (user == null)
                {
                    // アカウントの有無を画面から読み取らせないため、存在しない場合も同じ確認画面へ返す
                    return RedirectToPage("./ForgotPasswordConfirmation");
                }

                // このアプリは RequireConfirmedAccount = false で登録するため、メール確認を通った
                // ユーザーが存在しない。確認済みかの検査を残すと全員が黙って弾かれるので外す

                // For more information on how to enable account confirmation and password reset please
                // visit https://go.microsoft.com/fwlink/?LinkID=532713
                var code = await _userManager.GeneratePasswordResetTokenAsync(user);
                code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                var callbackUrl = Url.Page(
                    "/Account/ResetPassword",
                    pageHandler: null,
                    values: new { area = "Identity", code },
                    protocol: Request.Scheme);

                try
                {
                    await _emailSender.SendEmailAsync(
                        Input.Email,
                        "パスワード再設定のご案内",
                        "パスワードの再設定が要求されました。<br />" +
                        "次のリンクを開いて、新しいパスワードを設定してください。<br />" +
                        $"<a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>パスワードを再設定する</a><br />" +
                        "<br />" +
                        "心当たりが無い場合は、このメールを破棄してください。");
                }
                catch (Exception exception)
                {
                    // ログに残すのは失敗の事実まで。本文・トークン・宛先は書き込まない
                    _logger.LogWarning(exception, "パスワード再設定メールの送信に失敗しました。");
                    // ここでエラーを画面に出すと、必ず確認画面へ行く未登録アドレスとの応答の差で
                    // アカウントの有無が読み取れてしまう。失敗はこのログだけで拾い、応答は成功時と同じにする
                }

                return RedirectToPage("./ForgotPasswordConfirmation");
            }

            return Page();
        }
    }
}
