namespace CvPlatform.Core.Entities;

public class PositionTemplate : IVersioned
{
    public Guid Id { get; set; }
    public Guid CreatedById { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Company { get; set; }
    public string? Level { get; set; }
    public int MaxProjects { get; set; } = 3;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long Version { get; set; }

    public ApplicationUser CreatedBy { get; set; } = default!;
    public List<PositionTemplateAttribute> Attributes { get; set; } = [];
    public List<PositionTemplateAccessRule> AccessRules { get; set; } = [];
}
