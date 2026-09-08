namespace CvPlatform.Application.Common;

/// <summary>Offset pagination request with a 1-based page number and a capped page size.</summary>
public sealed record PageRequest
{
    public const int MaxPageSize = 100;

    public PageRequest(int Page = 1, int PageSize = 20)
    {
        if (Page < 1)
            throw new ArgumentOutOfRangeException(nameof(Page));
        if (PageSize is < 1 or > MaxPageSize)
            throw new ArgumentOutOfRangeException(nameof(PageSize));

        this.Page = Page;
        this.PageSize = PageSize;
    }

    public int Page { get; }

    public int PageSize { get; }

    public int Skip => (Page - 1) * PageSize;
}
