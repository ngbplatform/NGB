using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;

namespace NGB.Runtime.IntegrationTests.Reporting;

internal static class ComposableReportingIntegrationTestHelpers
{
    public static IHost CreateHost(string connectionString, FixedReportVariantAccessContext? actor = null)
        => IntegrationHostFactory.Create(
            connectionString,
            services =>
            {
                if (actor is null)
                    return;

                services.Replace(ServiceDescriptor.Scoped<IReportVariantAccessContext>(_ => actor));
            });

    public static async Task<(Guid cashId, Guid revenueId, Guid expensesId)> SeedMinimalCoAAsync(IHost host)
        => await ReportingTestHelpers.SeedMinimalCoAAsync(host);

    public static async Task<Guid> CreatePostedAccountingDocumentAsync(
        IHost host,
        string number,
        DateTime dateUtc,
        string debitCode,
        string creditCode,
        decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

        var documentId = await drafts.CreateDraftAsync(
            typeCode: "it_doc_a",
            number: number,
            dateUtc: dateUtc,
            manageTransaction: true,
            ct: CancellationToken.None);

        await posting.PostAsync(
            documentId,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, dateUtc, chart.Get(debitCode), chart.Get(creditCode), amount);
            },
            CancellationToken.None);

        return documentId;
    }

    public static async Task<ReportExecutionResponseDto> ExecuteLedgerAnalysisAsync(
        IHost host,
        ReportExecutionRequestDto request)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<IReportEngine>();
        return await engine.ExecuteAsync("accounting.ledger.analysis", request, CancellationToken.None);
    }

    public static decimal ReadDecimalCell(ReportCellDto cell)
    {
        cell.Value.Should().NotBeNull();
        var value = cell.Value!.Value;
        return value.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number => value.GetDecimal(),
            System.Text.Json.JsonValueKind.String when decimal.TryParse(value.GetString(), out var parsed) => parsed,
            _ => throw new XunitException($"Expected a decimal cell but found '{value.ValueKind}' with payload '{value}'.")
        };
    }

    internal sealed class FixedReportVariantAccessContext(string authSubject) : IReportVariantAccessContext
    {
        public string? AuthSubject { get; } = authSubject;
        public string? Email { get; } = $"{authSubject}@example.test";
        public string? DisplayName { get; } = authSubject;
        public bool IsActive => true;
    }
}
