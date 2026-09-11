using System.ComponentModel.DataAnnotations;

namespace ConAI.Web.Data;

public class MeetingFile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MeetingId { get; set; }

    public Meeting? Meeting { get; set; }

    public MeetingFileKind Kind { get; set; }

    [StringLength(255)]
    public string OriginalFileName { get; set; } = string.Empty;

    [StringLength(10)]
    public string Extension { get; set; } = string.Empty;

    [StringLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
