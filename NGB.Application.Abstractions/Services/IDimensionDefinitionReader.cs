namespace NGB.Application.Abstractions.Services;

public interface IDimensionDefinitionReader
{
    Task<IReadOnlyDictionary<string, Guid>> GetDimensionIdsByCodesAsync(
        IReadOnlyCollection<string> dimensionCodes,
        CancellationToken ct);
}