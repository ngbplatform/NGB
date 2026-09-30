using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace NGB.Runtime.Tests.Contracts;

/// <summary>
/// Keeps declaration-like runtime contracts executable without maintaining a manual list of every DTO property.
/// A type automatically drops out of this harness as soon as it gains a real method; its logic must then receive
/// focused positive/negative/boundary tests.
/// </summary>
public sealed class ProductionContractSurfaceCoverageTests
{
    private static readonly string[] ContractAssemblies =
    [
        "NGB.Contracts",
        "NGB.Application.Abstractions",
        "NGB.Metadata",
        "NGB.OperationalRegisters",
        "NGB.ReferenceRegisters",
        "NGB.Persistence",
        "NGB.Runtime"
    ];

    [Fact]
    public void SimpleContractTypes_AllConstructorsAndGettersAreExecutable()
    {
        var failures = new List<string>();

        foreach (var assemblyName in ContractAssemblies)
        {
            var assembly = Assembly.Load(assemblyName);
            foreach (var type in assembly.GetTypes().Where(IsSimpleContractType).OrderBy(type => type.FullName))
            {
                CoverType(type, failures);
            }
        }

        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
    }

    private static bool IsSimpleContractType(Type type)
    {
        if (type.IsAbstract || type.IsInterface || type.IsEnum || type.IsPointer || type.IsByRef || type.ContainsGenericParameters)
            return false;
        if (typeof(Delegate).IsAssignableFrom(type) || type.Name.Contains('<', StringComparison.Ordinal))
            return false;

        var realMethods = type
            .GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static |
                        BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => !method.IsSpecialName)
            .Where(method => method.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
            .Where(method => method.Name is not ("ToString" or "GetHashCode" or "Equals" or "PrintMembers" or "<Clone>$"))
            .ToArray();

        if (realMethods.Length != 0)
            return false;

        return type.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Length > 0
               || type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length > 0;
    }

    private static void CoverType(Type type, ICollection<string> failures)
    {
        var instances = new List<object?>();
        var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(constructor => !IsRecordCopyConstructor(type, constructor))
            .ToArray();

        if (type.IsValueType && constructors.Length == 0)
            instances.Add(Activator.CreateInstance(type));

        foreach (var constructor in constructors)
        {
            try
            {
                var arguments = constructor.GetParameters()
                    .Select(parameter => CreateValue(parameter.ParameterType, parameter.Name, new HashSet<Type>()))
                    .ToArray();
                instances.Add(constructor.Invoke(arguments));
            }
            catch (Exception exception)
            {
                var failure = Unwrap(exception);
                failures.Add($"{type.FullName} constructor: {failure.GetType().Name}: {failure.Message}{Environment.NewLine}{failure.StackTrace}");
            }
        }

        if (instances.Count == 0 && !type.IsValueType)
        {
            try
            {
                instances.Add(RuntimeHelpers.GetUninitializedObject(type));
            }
            catch (Exception exception)
            {
                failures.Add($"{type.FullName} allocation: {exception.GetType().Name}: {exception.Message}");
            }
        }

        foreach (var property in type.GetProperties(
                     BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var getter = property.GetGetMethod(nonPublic: true);
            if (getter is null || property.GetIndexParameters().Length != 0)
                continue;

            var targets = getter.IsStatic ? new object?[] { null } : instances.ToArray();
            foreach (var target in targets)
            {
                try
                {
                    getter.Invoke(target, null);
                }
                catch (Exception exception)
                {
                    failures.Add($"{type.FullName}.{property.Name}: {Unwrap(exception).GetType().Name}: {Unwrap(exception).Message}");
                }
            }
        }
    }

    private static object? CreateValue(Type type, string? parameterName, ISet<Type> stack)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
            return CreateValue(nullable, parameterName, stack);

        if (type == typeof(string)) return ValidString(parameterName);
        if (type == typeof(Guid)) return Guid.CreateVersion7();
        if (type == typeof(DateTime)) return new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        if (type == typeof(DateTimeOffset)) return new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        if (type == typeof(DateOnly)) return new DateOnly(2026, 1, 1);
        if (type == typeof(TimeOnly)) return new TimeOnly(12, 0);
        if (type == typeof(TimeSpan)) return TimeSpan.FromSeconds(1);
        if (type == typeof(bool)) return true;
        if (type == typeof(byte)) return (byte)1;
        if (type == typeof(short)) return (short)1;
        if (type == typeof(int)) return 1;
        if (type == typeof(long)) return 1L;
        if (type == typeof(float)) return 1f;
        if (type == typeof(double)) return 1d;
        if (type == typeof(decimal)) return 1m;
        if (type == typeof(CancellationToken)) return CancellationToken.None;
        if (type == typeof(Exception)) return new InvalidOperationException("test");
        if (type == typeof(object)) return new object();
        if (type == typeof(JsonElement)) return JsonSerializer.SerializeToElement("value");
        if (type == typeof(Assembly)) return typeof(ProductionContractSurfaceCoverageTests).Assembly;
        if (type == typeof(Type)) return typeof(ProductionContractSurfaceCoverageTests);
        if (type.FullName == "NGB.Runtime.Reporting.ReportDefinitionRuntimeModel")
            return RuntimeHelpers.GetUninitializedObject(type);
        if (type.IsEnum) return Enum.GetValues(type).GetValue(0);
        if (type.IsArray)
        {
            var elementType = type.GetElementType()!;
            var array = Array.CreateInstance(elementType, 1);
            array.SetValue(CreateValue(elementType, parameterName, stack), 0);
            return array;
        }
        if (typeof(Delegate).IsAssignableFrom(type)) return CreateDelegate(type);

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();
            if (definition == typeof(Task<>))
            {
                var result = CreateValue(arguments[0], parameterName, stack);
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(arguments[0]).Invoke(null, [result]);
            }

            if (definition == typeof(ValueTask<>))
                return Activator.CreateInstance(type, CreateValue(arguments[0], parameterName, stack));

            if (definition == typeof(Dictionary<,>) || ImplementsGeneric(type, typeof(IReadOnlyDictionary<,>)))
                return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(arguments));

            if (definition == typeof(HashSet<>) || ImplementsGeneric(type, typeof(IReadOnlySet<>)))
                return Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(arguments[0]));

            if (ImplementsGeneric(type, typeof(IEnumerable<>)) || definition == typeof(IReadOnlyList<>) ||
                definition == typeof(IReadOnlyCollection<>) || definition == typeof(ICollection<>) ||
                definition == typeof(IList<>))
            {
                var array = Array.CreateInstance(arguments[0], 1);
                array.SetValue(CreateValue(arguments[0], parameterName, stack), 0);
                return array;
            }
        }

        if (type == typeof(Task)) return Task.CompletedTask;
        if (type == typeof(ValueTask)) return ValueTask.CompletedTask;
        if (!type.IsValueType && (type.IsInterface || type.IsAbstract)) return null;
        if (!stack.Add(type)) return type.IsValueType ? Activator.CreateInstance(type) : null;

        try
        {
            var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(candidate => !IsRecordCopyConstructor(type, candidate))
                .OrderBy(info => info.GetParameters().Length)
                .FirstOrDefault();
            if (constructor is null)
                return type.IsValueType ? Activator.CreateInstance(type) : RuntimeHelpers.GetUninitializedObject(type);

            var arguments = constructor.GetParameters()
                .Select(parameter => CreateValue(parameter.ParameterType, parameter.Name, stack))
                .ToArray();
            return constructor.Invoke(arguments);
        }
        finally
        {
            stack.Remove(type);
        }
    }

    private static object CreateDelegate(Type delegateType)
    {
        var invoke = delegateType.GetMethod("Invoke")!;
        var parameters = invoke.GetParameters()
            .Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name))
            .ToArray();
        Expression body = invoke.ReturnType == typeof(void)
            ? Expression.Empty()
            : Expression.Default(invoke.ReturnType);
        return Expression.Lambda(delegateType, body, parameters).Compile();
    }

    private static bool ImplementsGeneric(Type type, Type genericDefinition)
        => type.IsGenericType && type.GetGenericTypeDefinition() == genericDefinition
           || type.GetInterfaces().Any(candidate =>
               candidate.IsGenericType && candidate.GetGenericTypeDefinition() == genericDefinition);

    private static bool IsRecordCopyConstructor(Type type, ConstructorInfo constructor)
    {
        var parameters = constructor.GetParameters();
        return parameters.Length == 1 && parameters[0].ParameterType == type;
    }

    private static string ValidString(string? parameterName)
        => parameterName?.Contains("email", StringComparison.OrdinalIgnoreCase) == true
            ? "user@example.com"
            : parameterName?.Contains("json", StringComparison.OrdinalIgnoreCase) == true
                ? "{}"
                : "value";

    private static Exception Unwrap(Exception exception)
        => exception is TargetInvocationException { InnerException: not null } target
            ? target.InnerException!
            : exception;
}
