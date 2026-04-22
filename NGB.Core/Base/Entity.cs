namespace NGB.Core.Base;

public abstract class Entity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
}
