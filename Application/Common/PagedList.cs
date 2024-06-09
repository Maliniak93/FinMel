namespace Application.Common;

/// <summary>
/// Represents the paged list.
/// </summary>
/// <typeparam name="T">The type of the response.</typeparam>
public class PagedList<T>
{

    public PagedList(IReadOnlyCollection<T> items, int totalCount, int page, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        TotalPages = (int) Math.Ceiling(totalCount / (double) pageSize);
        Page = page;
        PageSize = pageSize;
    }

    public static PagedList<T> Empty => new PagedList<T>(Array.Empty<T>(), 0, 0, 0);

    public IReadOnlyCollection<T> Items { get; }

    public int TotalCount { get; }
    public int TotalPages { get; }

    public int Page { get; }

    public int PageSize { get; }

    public bool HasNextPage => Page * PageSize < TotalCount;

    public bool HasPreviousPage => Page > 1;
}

