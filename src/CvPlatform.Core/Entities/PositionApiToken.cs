namespace CvPlatform.Core.Entities;

public class PositionApiToken
{
    public const int NameMaxLength = 200;

    public Guid Id { get; set; }
    public Guid PositionId { get; set; }
    public string Name { get; set; } = "";
    public string TokenHash { get; set; } = default!;
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public Position Position { get; set; } = default!;
    public ApplicationUser? CreatedBy { get; set; }
}
