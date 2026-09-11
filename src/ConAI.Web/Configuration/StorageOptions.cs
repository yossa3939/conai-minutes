using System.ComponentModel.DataAnnotations;

namespace ConAI.Web.Configuration;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    [Required]
    public string DataRoot { get; set; } = "App_Data";
}
