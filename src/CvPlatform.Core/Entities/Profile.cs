namespace CvPlatform.Core.Entities;

public class Profile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; } = default!;
    public List<ProfileAttributeValue> AttributeValues { get; set; } = [];
    public List<Project> Projects { get; set; } = [];
}
