using CvPlatform.Application.Search;
using CvPlatform.Core.Data;
using CvPlatform.Core.Entities;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace CvPlatform.Infrastructure.Search;

public sealed class PostgresFullTextMatcher : IFullTextMatcher
{
    public bool IsSupported(IAppDbContext db) =>
        db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    public IQueryable<Position> MatchPositions(IQueryable<Position> query, string term) =>
        query.Where(p => EF.Property<NpgsqlTsVector>(p, "SearchVector")
            .Matches(EF.Functions.WebSearchToTsQuery("english", term)));

    public IQueryable<Cv> MatchCvs(IQueryable<Cv> query, string term) =>
        query.Where(c => EF.Property<NpgsqlTsVector>(c, "SearchVector")
            .Matches(EF.Functions.WebSearchToTsQuery("english", term)));
}
