using CvPlatform.Application.Discussions;
using Microsoft.Extensions.Logging;

namespace CvPlatform.Application.Discussions;

public sealed class DiscussionNotifier(ILogger<DiscussionNotifier>? logger = null) : IDiscussionNotifier
{
    public event Action<Guid, DiscussionPostDto>? PostAdded;

    public void Notify(Guid positionId, DiscussionPostDto post)
    {
        var handlers = PostAdded?.GetInvocationList();
        if (handlers is null)
            return;
        foreach (Action<Guid, DiscussionPostDto> handler in handlers.Cast<Action<Guid, DiscussionPostDto>>())
        {
            try
            {
                handler(positionId, post);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Discussion subscriber failed for {PositionId}", positionId);
            }
        }
    }

    public Task NotifyAsync(Guid positionId, DiscussionPostDto post, CancellationToken ct = default)
    {
        Notify(positionId, post);
        return Task.CompletedTask;
    }
}
