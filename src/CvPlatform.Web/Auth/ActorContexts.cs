using System.Security.Claims;
using CvPlatform.Application.Authorization;

namespace CvPlatform.Web.Auth;

public static class ActorContexts
{
    public static ActorContext FromUser(ClaimsPrincipal user)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is null || !Guid.TryParse(id, out var userId))
            throw new InvalidOperationException("Authenticated user has no valid NameIdentifier.");
        var isAdmin = user.IsInRole(Roles.Admin);
        var isRecruiter = isAdmin || user.IsInRole(Roles.Recruiter);
        var isCandidate = user.IsInRole(Roles.Candidate);
        return new ActorContext(userId, isAdmin, isRecruiter, isCandidate);
    }

    public static ActorContext? TryFromUser(ClaimsPrincipal user)
    {
        var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is null || !Guid.TryParse(id, out var userId))
            return null;
        var isAdmin = user.IsInRole(Roles.Admin);
        var isRecruiter = isAdmin || user.IsInRole(Roles.Recruiter);
        var isCandidate = user.IsInRole(Roles.Candidate);
        return new ActorContext(userId, isAdmin, isRecruiter, isCandidate);
    }
}
