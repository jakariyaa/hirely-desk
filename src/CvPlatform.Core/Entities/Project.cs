namespace CvPlatform.Core.Entities;

public class Project : IVersioned
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public string Name { get; set; } = default!;
    public DateOnly? PeriodStart { get; set; }
    public DateOnly? PeriodEnd { get; set; }
    public string DescriptionMarkdown { get; set; } = "";
    public long Version { get; set; }

    public Profile Profile { get; set; } = default!;
    public List<ProjectTag> Tags { get; set; } = [];
}
