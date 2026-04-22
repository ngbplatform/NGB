using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.OperationalRegisters;

internal sealed class OperationalRegisterMovementsBuilder(Guid documentId) : IOperationalRegisterMovementsBuilder
{
    private readonly Dictionary<Guid, List<OperationalRegisterMovement>> _map = new();

    public IReadOnlyDictionary<Guid, IReadOnlyList<OperationalRegisterMovement>> MovementsByRegister
        => _map.ToDictionary(
            static x => x.Key,
            static x => (IReadOnlyList<OperationalRegisterMovement>)x.Value);

    public void Add(string registerCode, OperationalRegisterMovement movement)
    {
        if (string.IsNullOrWhiteSpace(registerCode))
            throw new NgbArgumentRequiredException(nameof(registerCode));
        
        if (movement is null)
            throw new NgbArgumentRequiredException(nameof(movement));
        
        if (movement.DocumentId != documentId)
            throw new NgbInvariantViolationException($"Operational Register movement must have DocumentId='{documentId}', but found '{movement.DocumentId}'.");

        var registerId = OperationalRegisterId.FromCode(registerCode);
        if (!_map.TryGetValue(registerId, out var list))
        {
            list = new List<OperationalRegisterMovement>();
            _map.Add(registerId, list);
        }
        list.Add(movement);
    }

    public IReadOnlyDictionary<Guid, IReadOnlyList<OperationalRegisterMovement>> Build()
        => MovementsByRegister;
}
