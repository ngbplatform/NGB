using EquipmentInspection.PostgreSql;
using NGB.Migrator.Core;

namespace EquipmentInspection.Migrator;

public static class Program
{
    public static Task<int> Main(string[] args)
    {
        _ = typeof(EquipmentMigrationPack).Assembly;
        return PlatformMigratorCli.RunAsync(args);
    }
}
