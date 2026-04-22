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
    {
        var loaded = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = asm.GetName().Name;
            if (!string.IsNullOrWhiteSpace(name))
                loaded.TryAdd(name, asm);
        }

        var entry = entryAssembly ?? Assembly.GetEntryAssembly();
        if (entry is null)
            return loaded.Values.ToArray();

        var queue = new Queue<Assembly>();
        queue.Enqueue(entry);

        while (queue.Count > 0)
        {
            var asm = queue.Dequeue();
            foreach (var reference in asm.GetReferencedAssemblies())
            {
                var key = reference.Name ?? reference.FullName;
                if (string.IsNullOrWhiteSpace(key) || loaded.ContainsKey(key))
                    continue;

                try
                {
                    var refAsm = Assembly.Load(reference);
                    var refName = refAsm.GetName().Name;
                    
                    if (!string.IsNullOrWhiteSpace(refName) && loaded.TryAdd(refName, refAsm))
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
