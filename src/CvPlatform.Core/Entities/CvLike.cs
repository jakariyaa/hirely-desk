namespace CvPlatform.Core.Entities;

public class CvLike
{
    public Guid CvId { get; set; }
    public Guid RecruiterId { get; set; }

    public Cv Cv { get; set; } = default!;
    public ApplicationUser Recruiter { get; set; } = default!;
}
