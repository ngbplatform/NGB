using System.Text.Json;
using FluentAssertions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.Contracts.Services;
using Npgsql;

namespace NGB.CRM.Api.IntegrationTests.Support;

internal static class CrmIntegrationTestHelpers
{
    public static RecordPayload Payload(object fields, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
    {
        var element = JsonSerializer.SerializeToElement(fields);
        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in element.EnumerateObject())
        {
            dict[property.Name] = property.Value;
        }

        return new RecordPayload(dict, parts);
    }

    public static IReadOnlyDictionary<string, RecordPartPayload> QuoteLines(params QuoteLineSeed[] rows)
    {
        var list = new List<IReadOnlyDictionary<string, JsonElement>>(rows.Length);

        foreach (var row in rows)
        {
            var lineAmount = Math.Round(row.Quantity * row.UnitPrice * (1m - row.DiscountPercent / 100m), 4);
            list.Add(new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["ordinal"] = JsonSerializer.SerializeToElement(row.Ordinal),
                ["product_id"] = JsonSerializer.SerializeToElement(row.ProductId),
                ["description"] = JsonSerializer.SerializeToElement(row.Description),
                ["quantity"] = JsonSerializer.SerializeToElement(row.Quantity),
                ["unit_price"] = JsonSerializer.SerializeToElement(row.UnitPrice),
                ["discount_percent"] = JsonSerializer.SerializeToElement(row.DiscountPercent),
                ["line_amount"] = JsonSerializer.SerializeToElement(lineAmount)
            });
        }

        return new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
        {
            ["lines"] = new(list)
        };
    }

    public static async Task<CatalogItemDto> CreateCatalogAsync(
        ICatalogService catalogs,
        string catalogType,
        object fields)
        => await catalogs.CreateAsync(catalogType, Payload(fields), CancellationToken.None);

    public static async Task<Guid> GetCatalogIdByDisplayAsync(
        ICatalogService catalogs,
        string catalogType,
        string display)
    {
        var page = await catalogs.GetPageAsync(
            catalogType,
            new PageRequestDto(Offset: 0, Limit: 50, Search: display),
            CancellationToken.None);

        var matches = page.Items
            .Where(x => string.Equals(x.Display, display, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        matches.Should().ContainSingle($"'{catalogType}' should contain '{display}'");
        return matches[0].Id;
    }

    public static async Task<int> CountDocumentsAsync(IDocumentService documents, string documentType)
    {
        var page = await documents.GetPageAsync(
            documentType,
            new PageRequestDto(Offset: 0, Limit: 1, Search: null),
            CancellationToken.None);

        return page.Total.GetValueOrDefault(page.Items.Count);
    }

    public static async Task<int> CountRowsAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    public static async Task<decimal> ScalarDecimalAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (decimal)(await cmd.ExecuteScalarAsync())!;
    }

    public static decimal SumMeasure(ReportExecutionResponseDto response, string columnCode)
    {
        var acceptedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            columnCode,
            $"{columnCode}__sum",
            $"{columnCode}__count",
            $"{columnCode}__average",
            $"total_{columnCode}",
            $"total_{columnCode}__sum",
            $"total_{columnCode}__count"
        };

        var index = response.Sheet.Columns
            .Select((column, ordinal) => new { column.Code, ordinal })
            .Single(x => acceptedCodes.Contains(x.Code))
            .ordinal;

        var totalRow = response.Sheet.Rows.SingleOrDefault(row => row.RowKind == ReportRowKind.Total);
        if (totalRow is not null)
            return CellDecimal(totalRow.Cells[index].Value);

        return response.Sheet.Rows
            .Where(row => row.RowKind == ReportRowKind.Detail)
            .Sum(row => CellDecimal(row.Cells[index].Value));
    }

    private static decimal CellDecimal(object? value)
    {
        return value switch
        {
            decimal v => v,
            double v => Convert.ToDecimal(v),
            float v => Convert.ToDecimal(v),
            int v => v,
            long v => v,
            JsonElement { ValueKind: JsonValueKind.Number } element => element.GetDecimal(),
            JsonElement { ValueKind: JsonValueKind.String } element when decimal.TryParse(element.GetString(), out var parsed) => parsed,
            string text when decimal.TryParse(text, out var parsed) => parsed,
            _ => 0m
        };
    }
}

internal readonly record struct QuoteLineSeed(
    int Ordinal,
    Guid ProductId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent);
