using CvPlatform.Application.Common;

namespace CvPlatform.Application.Discussions;

public sealed record DiscussionPostDto(
    Guid Id,
    Guid PositionId,
    Guid AuthorId,
    string AuthorName,
    DateTime CreatedAt,
    string TextMarkdown);

public sealed record DiscussionPostInput(string TextMarkdown);
