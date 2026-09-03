namespace CvPlatform.Core.Entities;

public class DiscussionPost
{
    public Guid Id { get; set; }
    public Guid PositionId { get; set; }
    public Guid AuthorId { get; set; }
    public DateTime CreatedAt { get; set; }
    public string TextMarkdown { get; set; } = "";

    public Position Position { get; set; } = default!;
    public ApplicationUser Author { get; set; } = default!;
}
