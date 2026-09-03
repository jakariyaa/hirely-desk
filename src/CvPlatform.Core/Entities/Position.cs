using NpgsqlTypes;

namespace CvPlatform.Core.Entities;

public class Position : IVersioned
{
    public Guid Id { get; set; }
    public string Title { get; set; } = default!;
    public string ShortDescription { get; set; } = "";
    public string? Company { get; set; }
    public string? Level { get; set; }
    public bool IsPublic { get; set; }
    public int MaxProjects { get; set; } = 3;
    public NpgsqlTsVector SearchVector { get; set; } = null!;
    public long Version { get; set; }

    public List<PositionAttribute> Attributes { get; set; } = [];
    public List<AccessRule> AccessRules { get; set; } = [];
}
