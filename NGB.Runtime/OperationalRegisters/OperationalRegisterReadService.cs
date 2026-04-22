using NGB.Core.Dimensions;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Tools.Extensions;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.OperationalRegisters;

public sealed class OperationalRegisterReadService(
    IOperationalRegisterMovementsQueryReader movementsQueryReader,
    IOperationalRegisterTurnoversReader turnoversReader,
    IOperationalRegisterBalancesReader balancesReader)
    : IOperationalRegisterReadService
{
    private const int MaxPageSize = 5000;

    public async Task<OperationalRegisterMovementsPage> GetMovementsPageAsync(
        OperationalRegisterMovementsPageRequest request,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        if (request.RegisterId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(request.RegisterId));

        if (request.ToInclusive < request.FromInclusive)
            throw new NgbArgumentOutOfRangeException(nameof(request.ToInclusive), request.ToInclusive, "To must be on or after From.");

        request.FromInclusive.EnsureMonthStart(nameof(request.FromInclusive));
        request.ToInclusive.EnsureMonthStart(nameof(request.ToInclusive));

        var pageSize = NormalizePageSize(request.PageSize);

        var dims = request.Dimensions is { Count: > 0 }
            ? new DimensionBag(request.Dimensions)
            : DimensionBag.Empty;

        var dimsForQuery = dims.IsEmpty ? request.Dimensions : dims.Items;

        var rows = await movementsQueryReader.GetByMonthsAsync(
            request.RegisterId,
            request.FromInclusive,
            request.ToInclusive,
            dimensions: dimsForQuery,
            dimensionSetId: request.DimensionSetId,
            documentId: request.DocumentId,
            isStorno: request.IsStorno,
            afterMovementId: request.Cursor?.AfterMovementId,
            limit: pageSize + 1,
            ct: ct);

        var hasMore = rows.Count > pageSize;

        var lines = hasMore
            ? rows.Take(pageSize).ToList()
            : rows.ToList();

        var nextCursor = hasMore && lines.Count > 0
            ? new OperationalRegisterMovementsPageCursor(lines[^1].MovementId)
            : null;

        return new OperationalRegisterMovementsPage(
            request.RegisterId,
            request.FromInclusive,
            request.ToInclusive,
            lines,
            hasMore,
            nextCursor);
    }

    public Task<OperationalRegisterMonthlyProjectionPage> GetTurnoversPageAsync(
        OperationalRegisterMonthlyProjectionPageRequest request,
        CancellationToken ct = default)
        => GetProjectionPageAsync(request, turnoversReader.GetPageByMonthsAsync, ct);

    public Task<OperationalRegisterMonthlyProjectionPage> GetBalancesPageAsync(
        OperationalRegisterMonthlyProjectionPageRequest request,
        CancellationToken ct = default)
        => GetProjectionPageAsync(request, balancesReader.GetPageByMonthsAsync, ct);

    private static int NormalizePageSize(int pageSize)
    {
        if (pageSize <= 0)
            return 1;

        return pageSize > MaxPageSize ? MaxPageSize : pageSize;
    }

    private static async Task<OperationalRegisterMonthlyProjectionPage> GetProjectionPageAsync(
        OperationalRegisterMonthlyProjectionPageRequest request,
        Func<Guid, DateOnly, DateOnly, IReadOnlyList<DimensionValue>?, Guid?, DateOnly?, Guid?, int, CancellationToken, Task<IReadOnlyList<OperationalRegisterMonthlyProjectionReadRow>>> reader,
        CancellationToken ct)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        if (request.RegisterId == Guid.Empty)
            throw new NgbArgumentRequiredException(nameof(request.RegisterId));

        if (request.ToInclusive < request.FromInclusive)
            throw new NgbArgumentOutOfRangeException(nameof(request.ToInclusive), request.ToInclusive, "To must be on or after From.");

        request.FromInclusive.EnsureMonthStart(nameof(request.FromInclusive));
        request.ToInclusive.EnsureMonthStart(nameof(request.ToInclusive));

        var pageSize = NormalizePageSize(request.PageSize);

        var dims = request.Dimensions is { Count: > 0 }
            ? new DimensionBag(request.Dimensions)
            : DimensionBag.Empty;

        var dimsForQuery = dims.IsEmpty ? request.Dimensions : dims.Items;

        var rows = await reader(
            request.RegisterId,
            request.FromInclusive,
            request.ToInclusive,
            dimsForQuery,
            request.DimensionSetId,
            request.Cursor?.AfterPeriodMonth,
            request.Cursor?.AfterDimensionSetId,
            pageSize + 1,
            ct);

        var page = rows.ToList();

        var hasMore = page.Count > pageSize;
        if (hasMore)
            page = page.Take(pageSize).ToList();

        var nextCursor = hasMore && page.Count > 0
            ? new OperationalRegisterMonthlyProjectionPageCursor(page[^1].PeriodMonth, page[^1].DimensionSetId)
            : null;

        return new OperationalRegisterMonthlyProjectionPage(
            request.RegisterId,
            request.FromInclusive,
            request.ToInclusive,
            page,
            hasMore,
            nextCursor);
    }
}
