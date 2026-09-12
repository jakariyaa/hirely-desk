using CvPlatform.Application.Authorization;

namespace CvPlatform.Application.Home;

public interface IHomeStatsService
{
    Task<Common.Result<HomeStatsDto>> GetAsync(ActorContext actor, CancellationToken ct = default);
    Task<Common.Result<PublicHomeStatsDto>> GetPublicAsync(CancellationToken ct = default);
}
