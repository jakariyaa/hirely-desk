namespace CvPlatform.Core.Entities;

public class CvProject
{
    public Guid CvId { get; set; }
    public Guid ProjectId { get; set; }
    public int SortOrder { get; set; }

    public Cv Cv { get; set; } = default!;
    public Project Project { get; set; } = default!;
}
