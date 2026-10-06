namespace CvPlatform.Application.Common;

/// <summary>A single page of results plus the total count needed for server-side paging UIs.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
