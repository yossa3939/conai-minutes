namespace ConAI.Web.Configuration;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public bool AllowSelfRegistration { get; set; } = true;
}
