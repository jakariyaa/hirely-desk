using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;

namespace CvPlatform.Application.Users;

public interface IUserAdministrationService
{
    Task<Result> DeleteUserAsync(ActorContext actor, Guid userId, CancellationToken ct = default);
}
