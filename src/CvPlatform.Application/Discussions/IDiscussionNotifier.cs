namespace CvPlatform.Application.Discussions;

public interface IDiscussionNotifier
{
    event Action<Guid, DiscussionPostDto>? PostAdded;
    void Notify(Guid positionId, DiscussionPostDto post);
    Task NotifyAsync(Guid positionId, DiscussionPostDto post, CancellationToken ct = default);
}
