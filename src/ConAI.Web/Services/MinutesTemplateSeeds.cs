namespace ConAI.Web.Services;

public interface IMinutesTemplateSeeds
{
    string StandardName { get; }

    string StandardBody { get; }

    string ConciseName { get; }

    string ConciseBody { get; }
}

/// <summary>組み込み 2 件の種。利用者ごとのコピーを作るときだけ読む。</summary>
public sealed class MinutesTemplateSeeds : IMinutesTemplateSeeds
{
    private readonly Lazy<string> _standard;
    private readonly Lazy<string> _concise;

    public MinutesTemplateSeeds(IWebHostEnvironment environment)
    {
        var candidate = Path.Combine(environment.ContentRootPath, "Prompts", "Templates");
        var directory = Directory.Exists(candidate)
            ? candidate
            : Path.Combine(AppContext.BaseDirectory, "Prompts", "Templates");
        _standard = new Lazy<string>(() => File.ReadAllText(Path.Combine(directory, "standard.md")));
        _concise = new Lazy<string>(() => File.ReadAllText(Path.Combine(directory, "concise.md")));
    }

    public string StandardName => "標準";

    public string StandardBody => _standard.Value;

    public string ConciseName => "簡潔";

    public string ConciseBody => _concise.Value;
}
