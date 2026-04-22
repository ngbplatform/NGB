using NGB.Core.Dimensions;

namespace NGB.OperationalRegisters.Contracts;

/// <summary>
/// Page request for operational register movements.
///
/// The underlying query supports filtering by:
/// - month range,
/// - dimension values (AND semantics),
/// - optional DimensionSetId,
/// - optional DocumentId,
/// - optional IsStorno.
///
/// Paging uses a keyset cursor based on monotonically increasing MovementId.
/// </summary>
public sealed record OperationalRegisterMovementsPageRequest(
    Guid RegisterId,
    DateOnly FromInclusive,
    DateOnly ToInclusive,
    IReadOnlyList<DimensionValue>? Dimensions = null,
    Guid? DimensionSetId = null,
    Guid? DocumentId = null,
    bool? IsStorno = null,
    OperationalRegisterMovementsPageCursor? Cursor = null,
    int PageSize = 200);
