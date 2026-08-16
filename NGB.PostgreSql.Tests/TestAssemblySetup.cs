using System.Runtime.CompilerServices;
using NGB.PostgreSql.Dapper;

namespace NGB.PostgreSql.Tests;

internal static class TestAssemblySetup
{
    [ModuleInitializer]
    internal static void InitializeDapper() => DapperTypeHandlers.Register();
}
