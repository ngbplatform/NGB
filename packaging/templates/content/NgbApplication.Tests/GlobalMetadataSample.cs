// This fixture intentionally has no namespace, so metadata inspection covers
// the global namespace and every nested visibility without executing the types.
public class GlobalMetadataSample
{
    public const string PublicValue = "public";
    protected const string ProtectedValue = "protected";
    protected internal const string ProtectedInternalValue = "protected internal";
    internal const string InternalValue = "internal";
    private const string PrivateValue = "private";

    public class PublicNested
    {
    }

    protected class ProtectedNested
    {
    }

    protected internal class ProtectedInternalNested
    {
    }

    internal class InternalNested
    {
    }

    private class PrivateNested
    {
    }

    private protected class PrivateProtectedNested
    {
    }
}
