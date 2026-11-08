using System.Reflection;
using System.Runtime.ExceptionServices;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

// Calls the real implementation before observing or delaying the returned task.
public class ObservingProxy<T> : DispatchProxy where T : class
{
    private T _inner = null!;
    private Func<MethodInfo, object?[]?, object?, object?> _observe = null!;

    public static T Wrap(T inner, Func<MethodInfo, object?[]?, object?, object?> observe)
    {
        var proxy = Create<T, ObservingProxy<T>>();
        var implementation = (ObservingProxy<T>)(object)proxy;
        implementation._inner = inner;
        implementation._observe = observe;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        object? result;
        try
        {
            result = targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }

        return _observe(targetMethod, args, result);
    }
}
