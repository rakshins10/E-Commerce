namespace ECommerce.Common.Pagination;

/// <summary>
/// A request for one page of a larger result set.
/// </summary>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Items per page.</param>
/// <param name="SortBy">Field name to sort by, or <see langword="null"/> for the endpoint's default order.</param>
/// <param name="SortDescending">Sort direction.</param>
/// <remarks>
/// <para>
/// <b>Server-side paging, sorting, and filtering are mandatory</b> on every list endpoint in this system — the
/// admin panel's data-dense tables depend on it. Returning everything and paging in the browser works until the
/// table has 50,000 rows, at which point it fails all at once and in production.
/// </para>
/// <para>
/// <b><see cref="PageSize"/> is clamped, not trusted.</b> An unclamped page size is a denial-of-service vector:
/// <c>?pageSize=10000000</c> is a free way to exhaust server memory. <see cref="Normalise"/> enforces the bounds
/// centrally so no endpoint can forget.
/// </para>
/// <para>
/// <b>Offset paging is used here, and it has a known limit.</b> <c>OFFSET n</c> makes the database walk and
/// discard <c>n</c> rows, so deep pages get progressively slower, and a row inserted between requests can shift
/// items across page boundaries. It is the right choice for admin tables, where users jump to specific page
/// numbers. For an infinite-scroll feed, keyset ("seek") pagination — <c>WHERE id &gt; @last ORDER BY id</c> — is
/// the correct pattern: constant time at any depth, and stable under concurrent inserts. Being able to say which
/// one you would use and why is the point.
/// </para>
/// </remarks>
public sealed record PageRequest(int Page = 1, int PageSize = 20, string? SortBy = null, bool SortDescending = false)
{
    /// <summary>Largest page size any endpoint will serve, regardless of what the caller asks for.</summary>
    public const int MaxPageSize = 200;

    /// <summary>Page size used when the caller does not specify one.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>
    /// Returns a request with page and size coerced into legal bounds. Call this before using the values.
    /// </summary>
    public PageRequest Normalise() => this with
    {
        Page = Page < 1 ? 1 : Page,
        PageSize = PageSize switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => PageSize,
        },
    };

    /// <summary>Rows to skip. Use only after <see cref="Normalise"/>.</summary>
    public int Skip => (Page - 1) * PageSize;
}

/// <summary>
/// One page of results, plus the metadata a client needs to render pagination controls.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <remarks>
/// <see cref="TotalCount"/> requires a second <c>COUNT(*)</c> query. That is a deliberate cost: without it a
/// client cannot show "page 3 of 47" or render a page-number control. Where the count is expensive and the UI
/// only needs "is there more?", the cheaper trick is to request <c>PageSize + 1</c> rows and report whether the
/// extra one came back.
/// </remarks>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < TotalPages;

    /// <summary>An empty page — used for a valid query that matched nothing.</summary>
    public static PagedResult<T> Empty(PageRequest request) => new([], request.Page, request.PageSize, 0);
}
