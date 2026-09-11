using System.ComponentModel.DataAnnotations;
using ConAI.Web.Gemini;

namespace ConAI.Web.Configuration;

public sealed class GeminiOptions : IValidatableObject
{
    public const string SectionName = "Gemini";
    public const string GoogleProvider = "Google";
    public const string FakeProvider = "Fake";
    public const string ApiKeyEnvironmentVariable = "GEMINI_API_KEY";

    public string Provider { get; set; } = GoogleProvider;

    public string ApiKey { get; set; } = string.Empty;

    // モデル名に既定値を置かない。置くと appsettings.json の版数と二重の正になり、
    // 設定を落とした環境が古い版で黙って動いてしまう。未設定は起動時に落とす。
    [Required]
    public string LiveModel { get; set; } = string.Empty;

    [Required]
    public string LiveTranslateModel { get; set; } = string.Empty;

    [Required]
    public string GenerateModel { get; set; } = string.Empty;

    [Required]
    public string SelectModel { get; set; } = string.Empty;

    // 空文字は「未指定」で、そのときは ThinkingConfig を送らない（モデル任せ）。
    // 議事録への回答（Chat）はモデルを GenerateModel と共用するが、thinking level は
    // 用途別に振れるようあえて別のキーにしている（設定漏れではない）。
    public string GenerateThinkingLevel { get; set; } = string.Empty;

    public string SelectThinkingLevel { get; set; } = string.Empty;

    public string ChatThinkingLevel { get; set; } = string.Empty;

    [Range(1, 2048)]
    public int InlineLimitMb { get; set; } = 20;

    public bool UseSessionResumption { get; set; }

    public long InlineLimitBytes => (long)InlineLimitMb * 1024 * 1024;

    public bool IsFake => string.Equals(Provider, FakeProvider, StringComparison.OrdinalIgnoreCase);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // thinking level は Provider の種類に関係なく検証する。綴りの誤りが黙って未指定で動くと、
        // 指定したつもりの強さで議事録が出ない状態を利用者が検知できなくなるため。
        var allowedList = string.Join("/", ThinkingLevels.AllowedValues.Select(value => $"'{value}'"));
        foreach (var (propertyName, value) in new (string PropertyName, string Value)[]
        {
            (nameof(GenerateThinkingLevel), GenerateThinkingLevel),
            (nameof(SelectThinkingLevel), SelectThinkingLevel),
            (nameof(ChatThinkingLevel), ChatThinkingLevel)
        })
        {
            if (ThinkingLevels.IsAllowed(value))
            {
                continue;
            }

            yield return new ValidationResult(
                $"Gemini:{propertyName} は {allowedList} のいずれか、または空（未指定）にしてください。実際の値: '{value}'",
                new[] { propertyName });
        }

        var isGoogle = string.Equals(Provider, GoogleProvider, StringComparison.OrdinalIgnoreCase);
        if (!isGoogle && !IsFake)
        {
            yield return new ValidationResult(
                $"Gemini:Provider は '{GoogleProvider}' または '{FakeProvider}' のいずれかにしてください。実際の値: '{Provider}'",
                new[] { nameof(Provider) });
            yield break;
        }

        if (isGoogle && string.IsNullOrWhiteSpace(ApiKey))
        {
            yield return new ValidationResult(
                "Gemini:ApiKey が未設定です。user-secrets、環境変数 Gemini__ApiKey、または環境変数 GEMINI_API_KEY のいずれかで設定してください。",
                new[] { nameof(ApiKey) });
        }
    }
}
