namespace ConAI.Web.Configuration;

public sealed class StoragePaths
{
    public StoragePaths(string root)
    {
        Root = root;
        UploadsRoot = Path.Combine(root, "uploads");
        KeysRoot = Path.Combine(root, "keys");
        DatabaseFile = Path.Combine(root, "conai.db");
    }

    public string Root { get; }
    public string UploadsRoot { get; }
    public string KeysRoot { get; }
    public string DatabaseFile { get; }

    public static StoragePaths FromConfiguration(IConfiguration configuration, string contentRootPath)
    {
        var configured = configuration["Storage:DataRoot"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            configured = "App_Data";
        }

        var root = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(contentRootPath, configured);

        return new StoragePaths(Path.GetFullPath(root));
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(UploadsRoot);
        Directory.CreateDirectory(KeysRoot);
    }
}
