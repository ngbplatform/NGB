using System.Reflection;

namespace NGB.PostgreSql.Migrations.Evolve;

/// <summary>
/// Helper to make migration pack discovery deterministic.
///
/// In .NET, referenced assemblies are not always loaded until a type from them is used.
/// Migrator tools and demo hosts should proactively load referenced assemblies so
/// <see cref="SchemaMigrator.DiscoverPacks"/> can see all pack contributors.
/// </summary>
public static class MigrationAssemblyDiscovery
{
    public static IReadOnlyCollection<Assembly> LoadForPackDiscovery(Assembly? entryAssembly = null)
        => LoadForPackDiscoveryCore(
            entryAssembly ?? Assembly.GetEntryAssembly(),
            AppDomain.CurrentDomain.GetAssemblies(),
            Assembly.Load);

    internal static IReadOnlyCollection<Assembly> LoadForPackDiscoveryCore(
        Assembly? entryAssembly,
        IEnumerable<Assembly> initiallyLoadedAssemblies,
        Func<AssemblyName, Assembly> assemblyLoader)
    {
        var loaded = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        foreach (var asm in initiallyLoadedAssemblies)
        {
            loaded.TryAdd(asm.GetName().Name!, asm);
        }

        if (entryAssembly is null)
            return loaded.Values.ToArray();

        var queue = new Queue<Assembly>();
        queue.Enqueue(entryAssembly);

        while (queue.Count > 0)
        {
            var asm = queue.Dequeue();
            foreach (var reference in asm.GetReferencedAssemblies())
            {
                var key = reference.Name!;
                if (loaded.ContainsKey(key))
                    continue;

                try
                {
                    var refAsm = assemblyLoader(reference);
                    var refName = refAsm.GetName().Name!;

                    if (loaded.TryAdd(refName, refAsm))
                        queue.Enqueue(refAsm);
                }
                catch
                {
                    // Best effort: ignore load errors (native deps, optional assemblies, etc.).
                }
            }
        }

        return loaded.Values.ToArray();
    }
}
