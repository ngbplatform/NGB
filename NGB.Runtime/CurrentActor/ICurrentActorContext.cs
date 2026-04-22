namespace NGB.Runtime.CurrentActor;

/// <summary>
/// Provides the current execution actor for the ongoing request or operation.
/// <para>
/// Implementations are infrastructure / host specific (e.g., ASP.NET from JWT claims).
/// </para>
/// </summary>
public interface ICurrentActorContext
{
    ActorIdentity? Current { get; }
}
