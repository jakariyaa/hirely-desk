namespace CvPlatform.Application.Authorization;

public sealed record ActorContext(Guid UserId, bool IsAdmin, bool IsRecruiter = false, bool IsCandidate = false)
{
    public bool IsPrivileged => IsAdmin || IsRecruiter;
}
