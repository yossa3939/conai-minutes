using System.ComponentModel.DataAnnotations;

namespace ConAI.Web.Configuration;

public sealed class LiveOptions
{
    public const string SectionName = "Live";

    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();

    [Range(1, 100)]
    public int MaxSessionsPerUser { get; set; } = 1;
}
