using CvPlatform.Core.Entities;

namespace CvPlatform.Application.Search;

public interface IFullTextMatcher
{
    bool IsSupported(Core.Data.IAppDbContext db);
    IQueryable<Position> MatchPositions(IQueryable<Position> query, string term);
    IQueryable<Cv> MatchCvs(IQueryable<Cv> query, string term);
}
