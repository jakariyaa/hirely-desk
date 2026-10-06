namespace CvPlatform.Application.Discussions;

public interface IDiscussionNotifier
{
    event Action<Guid, DiscussionPostDto>? PostAdded;
    Task NotifyAsync(Guid positionId, DiscussionPostDto post, CancellationToken ct = default);
}
