namespace CvPlatform.Core.Entities;

public class ProjectTag
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;

    public List<Project> Projects { get; set; } = [];
}
