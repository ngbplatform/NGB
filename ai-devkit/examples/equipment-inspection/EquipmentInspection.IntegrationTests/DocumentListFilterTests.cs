using System.Net;
using System.Net.Http.Json;
using Dapper;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Persistence.Documents.Universal;
using NGB.PostgreSql.Documents;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class DocumentListFilterTests
{
    [Fact]
    public async Task Registered_filter_contributor_binds_values_and_keeps_metadata_and_authorization_checks()
    {
        var probe = new FilterProbe();
        await using var host = new InspectionHost(configureServices: services =>
        {
            var original = services.Single(item => item.ImplementationType == typeof(EquipmentDefinitions));
            services[services.IndexOf(original)] = ServiceDescriptor.Singleton<IDefinitionsContributor, FilterDefinitions>();
            services.AddSingleton<IPostgresDocumentListFilterSqlContributor>(new ResultFilter(probe));
        });
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        const string endpoint = "/api/documents/" + EquipmentDefinitions.InspectionCode;
        var first = await PostAsync<DocumentDto>(admin, endpoint, Payload(("display", "First"), ("result", "Passed")));
        await PostAsync<DocumentDto>(admin, endpoint, Payload(("display", "Second"), ("result", "Rejected")));

        var selected = await admin.GetFromJsonAsync<PageResponseDto<DocumentDto>>(
            endpoint + "?limit=10&filters.result_label=PASSED", Json);
        Assert.Equal(first.Id, Assert.Single(selected!.Items).Id);
        Assert.True(probe.Calls > 0);
        var literal = Uri.EscapeDataString("passed' OR TRUE --");
        var empty = await admin.GetFromJsonAsync<PageResponseDto<DocumentDto>>(
            endpoint + "?limit=10&filters.result_label=" + literal, Json);
        Assert.Empty(empty!.Items);
        Assert.Equal("passed' OR TRUE --", probe.LastValue);

        var before = probe.Calls;
        using var unknown = await admin.GetAsync(endpoint + "?filters.unregistered=Passed");
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(before, probe.Calls);
        using var reader = await host.CreateClientAsync("reader");
        using var denied = await reader.GetAsync(endpoint + "?filters.result_label=Passed");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(before, probe.Calls);
        var direct = await admin.GetFromJsonAsync<PageResponseDto<DocumentDto>>(
            endpoint + "?filters.result=Passed", Json);
        Assert.Equal(first.Id, Assert.Single(direct!.Items).Id);
        Assert.Equal(before, probe.Calls);
    }

    private sealed class FilterProbe
    {
        public int Calls { get; set; }
        public string? LastValue { get; set; }
    }

    private sealed class ResultFilter(FilterProbe probe) : IPostgresDocumentListFilterSqlContributor
    {
        public bool TryBuildClause(DocumentHeadDescriptor head, DocumentFilter filter, string documentAlias,
            string headAlias, string parameterName, DynamicParameters parameters, out string clause)
        {
            clause = string.Empty;
            if (head.TypeCode != EquipmentDefinitions.InspectionCode || filter.Key != "result_label")
            {
                return false;
            }
            probe.Calls++;
            probe.LastValue = Assert.Single(filter.Values);
            var normalized = filter with { Values = filter.Values.Select(value => value.ToLowerInvariant()).ToArray() };
            clause = PostgresDocumentFilterSql.BuildPredicate(
                "lower(" + PostgresDocumentFilterSql.Qualify(headAlias, "result") + ")",
                normalized, parameterName, parameters);
            return true;
        }
    }

    private sealed class FilterDefinitions : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            var original = new DefinitionsBuilder();
            new EquipmentDefinitions().Contribute(original);
            var definitions = original.Build();
            foreach (var catalog in definitions.Catalogs)
            {
                builder.AddCatalog(catalog.TypeCode, item => item.Metadata(catalog.Metadata));
            }
            var metadata = definitions.GetDocument(EquipmentDefinitions.InspectionCode).Metadata with
            {
                ListFilters =
                [
                    new("result_label", "Result ignoring case", ColumnType.String),
                    new("result", "Exact result", ColumnType.String)
                ]
            };
            builder.AddDocument(metadata.TypeCode, document => document.Metadata(metadata));
        }
    }
}
