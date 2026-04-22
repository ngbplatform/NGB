namespace NGB.Runtime.CurrentActor;

public sealed class NullCurrentActorContext : ICurrentActorContext
{
    public ActorIdentity? Current => null;
}
