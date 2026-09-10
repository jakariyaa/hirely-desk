namespace CvPlatform.Core.Entities;

public class Position : IVersioned
{
    public Guid Id { get; set; }
    public Guid? OwnerId { get; set; }
    public string Title { get; set; } = default!;
    public string ShortDescription { get; set; } = "";
    public string? Company { get; set; }
    public string? Level { get; set; }
    public bool IsPublic { get; set; }
    public int MaxProjects { get; set; } = 3;
    public long Version { get; set; }

    public ApplicationUser Owner { get; set; } = default!;
    public List<PositionAttribute> Attributes { get; set; } = [];
    public List<AccessRule> AccessRules { get; set; } = [];
}
