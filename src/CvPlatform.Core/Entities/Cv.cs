using CvPlatform.Core.Enums;

namespace CvPlatform.Core.Entities;

public class Cv : IVersioned
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public Guid PositionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public CvStatus Status { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? SearchText { get; set; }
    public long Version { get; set; }

    public Profile Profile { get; set; } = default!;
    public Position Position { get; set; } = default!;
    public List<CvProject> IncludedProjects { get; set; } = [];
}
