namespace NGB.Core.Base.Paging;

public abstract class PageSizeBase
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private int _pageSize = DefaultPageSize;

    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = ClampPageSize(value);
    }

    public bool DisablePaging { get; init; }

    private static int ClampPageSize(int value)
    {
        if (value <= 0)
            return DefaultPageSize;
        
        if (value > MaxPageSize)
            return MaxPageSize;
        
        return value;
    }
}
