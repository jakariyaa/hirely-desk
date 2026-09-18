using CvPlatform.Application.Authorization;
using CvPlatform.Application.Common;
using CvPlatform.Application.Users;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using CvPlatform.Web.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CvPlatform.Web.Users;

public sealed class UserAdministrationService(
    IAppDbContextFactory factory,
    UserManager<ApplicationUser> users) : IUserAdministrationService
{
    public async Task<Result> DeleteUserAsync(ActorContext actor, Guid userId, CancellationToken ct = default)
    {
        if (!actor.IsAdmin)
            return Result.Failure(ErrorCodes.Forbidden, "Admin access required.");

        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
            return Result.Failure(ErrorCodes.NotFound, "User was not found.");
        if (await users.IsInRoleAsync(user, SeedData.AdminRole))
            return Result.Failure(ErrorCodes.Forbidden, "Admin accounts cannot be deleted.");
        if (string.Equals(user.Email, SeedData.AdminEmail, StringComparison.OrdinalIgnoreCase))
            return Result.Failure(ErrorCodes.Forbidden, "Seeded admin cannot be deleted.");

        await using var db = factory.CreateDbContext();
        if (await db.Users.CountAsync(ct) <= 1)
            return Result.Failure(ErrorCodes.Forbidden, "Cannot delete the last user.");

        var result = await users.DeleteAsync(user);
        return result.Succeeded
            ? Result.Success()
            : Result.Failure(ErrorCodes.Conflict, string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
