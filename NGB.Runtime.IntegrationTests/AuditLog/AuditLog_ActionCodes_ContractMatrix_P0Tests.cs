using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using NGB.Core.AuditLog;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_ActionCodes_ContractMatrix_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static IReadOnlyList<string> GetAllActionCodes()
    {
        return typeof(AuditActionCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
    }

    [Fact]
    public void ActionCodes_AreUnique_AndMatchNamingConvention()
    {
        var codes = GetAllActionCodes();
        codes.Should().NotBeEmpty();

        codes.Distinct(StringComparer.Ordinal).Should().HaveCount(codes.Count);

        var rx = new Regex(@"^[a-z0-9]+(\.[a-z0-9_]+)+$", RegexOptions.Compiled);

        foreach (var c in codes)
        {
            c.Should().NotBeNullOrWhiteSpace();
            c.Should().Be(c.Trim(), "action codes must not contain leading/trailing whitespace");
            rx.IsMatch(c).Should().BeTrue($"action code '{c}' must match naming convention");
        }
    }

    [Fact]
    public async Task ActionCodes_CanBePersisted_ThroughAuditLogService()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var codes = GetAllActionCodes();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            for (var i = 0; i < codes.Count; i++)
            {
                var code = codes[i];

                await audit.WriteAsync(
                    entityKind: AuditEntityKind.Document,
                    entityId: Guid.CreateVersion7(),
                    actionCode: code,
                    changes: new[]
                    {
                        new AuditFieldChange(
                            FieldPath: "p0_contract",
                            OldValueJson: null,
                            NewValueJson: i.ToString(CultureInfo.InvariantCulture))
                    },
                    metadata: new { contract = "action_codes", idx = i },
                    correlationId: Guid.CreateVersion7(),
                    ct: CancellationToken.None);
            }

            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(new AuditLogQuery(Limit: 500), CancellationToken.None);

            events.Should().HaveCount(codes.Count);

            events.Select(e => e.ActionCode)
                .Distinct(StringComparer.Ordinal)
                .Should().BeEquivalentTo(codes);

            events.Should().OnlyContain(e =>
                e.Changes.Count == 1 && e.Changes[0].FieldPath == "p0_contract");
        }
    }
}
