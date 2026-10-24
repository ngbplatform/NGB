using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using Xunit;

namespace NGB.AiAssistant.AspNetCore.IntegrationTests;

public sealed partial class AssistantHttpTests
{
    [NativeOllamaFact]
    public async Task NativeOllamaUsesTheUsersLanguageWhenReformattingAnEarlierAnswer()
    {
        const string type = "sample.payment";
        var id = Guid.NewGuid();
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetTypeMetadataAsync(type, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentTypeMetadataDto(type, "Payment", EntityKind.Document));
        documents.Setup(value => value.GetByIdAsync(type, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentDto(id, "Payment PAY-0042", new RecordPayload(
                new Dictionary<string, JsonElement>
                {
                    ["amount"] = JsonSerializer.SerializeToElement(1993m),
                    ["received_on"] = JsonSerializer.SerializeToElement("2025-08-12")
                }), DocumentStatus.Posted, false));
        await using var host = await Host.StartAsync(mode: "ollama", configure: services => AddInvoiceReads(services, documents.Object));
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
        [
            new("user", "What are this payment's amount and received date?"),
            new("assistant", "Betrag: 1993,00. Erhalten am: 12. August 2025."),
            new("user", "Provide the result as a table.")
        ], new("document", type, id)));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        output.WriteLine(string.Join('\n', host.State.ModelInputs));

        Assert.DoesNotContain(events, item => item.Type == "error");
        Assert.Contains('|', answer);
        Assert.Contains("Amount", answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Received", answer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1993", answer.Replace(",", "", StringComparison.Ordinal));
        Assert.DoesNotContain("Betrag", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<br", answer, StringComparison.OrdinalIgnoreCase);
        var receivedOn = Assert.Single(events, item => item.Value?.Value == "2025-08-12").Value!;
        Assert.Contains(receivedOn.Reference, answer);
        documents.Verify(value => value.GetByIdAsync(type, id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaListsAvailableTypeNamesInsteadOfCapabilityCategories()
    {
        var catalogs = new Mock<IAuthorizedCatalogReadService>(MockBehavior.Strict);
        catalogs.Setup(value => value.GetAllMetadataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new CatalogTypeMetadataDto("sample.party", "Parties", EntityKind.Catalog),
                new CatalogTypeMetadataDto("sample.property", "Properties", EntityKind.Catalog)
            ]);
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetAllMetadataAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new DocumentTypeMetadataDto("sample.lease", "Leases", EntityKind.Document),
                new DocumentTypeMetadataDto("sample.payment", "Payments", EntityKind.Document)
            ]);
        await using var host = await Host.StartAsync(mode: "ollama", configure: services =>
        {
            services.AddScoped(_ => catalogs.Object);
            services.AddScoped(_ => documents.Object);
        });
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "What types of catalogs and documents can I access?")]));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        output.WriteLine(string.Join('\n', host.State.ModelInputs));

        Assert.DoesNotContain(events, item => item.Type == "error");
        foreach (var name in new[] { "Parties", "Properties", "Leases", "Payments" })
        {
            Assert.Contains(name, answer, StringComparison.OrdinalIgnoreCase);
        }
    }

    [NativeOllamaFact]
    public async Task NativeOllamaReturnsFiveLeasesWithExactAmountsAndNoInventedCurrency()
    {
        const string type = "sample.lease";
        var metadata = new DocumentTypeMetadataDto(type, "Lease", EntityKind.Document,
            List: new ListMetadataDto([
                new("tenant", "Tenant", DataType.String),
                new("property", "Property", DataType.String),
                new("start_on", "Start", DataType.Date),
                new("end_on", "End", DataType.Date),
                new("rent_amount", "Rent Amount", DataType.Decimal)
            ]));
        var amounts = new[] { 1457m, 153.9794m, 227.5006m, 1993m, 2795m };
        var records = amounts.Select((amount, index) => new DocumentDto(Guid.NewGuid(), $"Lease L-{index + 1:D4}",
            new RecordPayload(new Dictionary<string, JsonElement>
            {
                ["tenant"] = JsonSerializer.SerializeToElement($"Tenant {index + 1}"),
                ["property"] = JsonSerializer.SerializeToElement($"River House #{index + 101}"),
                ["start_on"] = JsonSerializer.SerializeToElement("2025-10-27"),
                ["end_on"] = JsonSerializer.SerializeToElement("2026-10-31"),
                ["rent_amount"] = JsonSerializer.SerializeToElement(amount)
            }), DocumentStatus.Posted, false)).ToArray();
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetAllMetadataAsync(It.IsAny<CancellationToken>())).ReturnsAsync([metadata]);
        documents.Setup(value => value.GetTypeMetadataAsync(type, It.IsAny<CancellationToken>())).ReturnsAsync(metadata);
        documents.Setup(value => value.GetPageAsync(type, It.Is<PageRequestDto>(request =>
                request.Limit == 5 && request.Filters == null && request.Search == null), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResponseDto<DocumentDto>(records, 0, 5, null));
        await using var host = await Host.StartAsync(mode: "ollama", configure: services => AddInvoiceReads(services, documents.Object));
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "Show five leases with their tenants, properties, start dates, end dates, and rent amounts. Use a table.")]));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        output.WriteLine(string.Join('\n', host.State.ModelInputs));

        Assert.DoesNotContain(events, item => item.Type == "error");
        Assert.Contains('|', answer);
        Assert.DoesNotContain('$', answer);
        Assert.DoesNotContain("USD", answer);
        Assert.DoesNotContain("<br", answer, StringComparison.OrdinalIgnoreCase);

        for (var index = 0; index < amounts.Length; index++)
        {
            Assert.Contains($"Tenant {index + 1}", answer);
            Assert.Contains($"River House #{index + 101}", answer);
            Assert.Contains(amounts[index].ToString(System.Globalization.CultureInfo.InvariantCulture),
                answer.Replace(",", "", StringComparison.Ordinal));
        }

        foreach (var date in new[] { "2025-10-27", "2026-10-31" })
        {
            var value = Assert.Single(events, item => item.Value?.Value == date).Value!;
            Assert.Equal(5, answer.Split(value.Reference).Length - 1);
        }

        documents.Verify(value => value.GetByIdAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaAnswersTheSelectedLeaseRentWithoutInventingCurrency()
    {
        const string type = "pm.lease";
        var id = Guid.Parse("019e13a0-8c0c-753f-8423-fb1fc31fb3fb");
        var fields = new Dictionary<string, JsonElement>
        {
            ["property_id"] = JsonSerializer.SerializeToElement(new
            {
                id = Guid.Parse("019e13a0-896e-741b-972d-2c2aa357e46e"),
                display = "100 Hudson Ave, Dataset demo, Hoboken, NJ 07030 #107"
            }),
            ["start_on_utc"] = JsonSerializer.SerializeToElement("2025-10-27"),
            ["end_on_utc"] = JsonSerializer.SerializeToElement<string?>(null),
            ["rent_amount"] = JsonSerializer.SerializeToElement(1457m),
            ["due_day"] = JsonSerializer.SerializeToElement(6),
            ["memo"] = JsonSerializer.SerializeToElement("")
        };
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetTypeMetadataAsync(type, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentTypeMetadataDto(type, "Lease", EntityKind.Document));
        documents.Setup(value => value.GetByIdAsync(type, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentDto(id,
                "100 Hudson Ave, Dataset demo, Hoboken, NJ 07030 #107 — 2025-10-27 → Open",
                new RecordPayload(fields), DocumentStatus.Posted, false));
        await using var host = await Host.StartAsync(mode: "ollama", configure: services => AddInvoiceReads(services, documents.Object));
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "What is the rent amount for this lease?")], new("document", type, id)));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        output.WriteLine(string.Join('\n', host.State.ModelInputs));

        Assert.DoesNotContain(events, item => item.Type == "error");
        Assert.Contains("1457", answer.Replace(",", "", StringComparison.Ordinal));
        Assert.DoesNotContain('$', answer);
        Assert.DoesNotContain("USD", answer);
        documents.Verify(value => value.GetByIdAsync(type, id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [NativeOllamaFact]
    public async Task NativeOllamaFindsTheSelectedPaymentWithoutARequestedLink()
    {
        await using var host = await Host.StartAsync(mode: "ollama-search", configure: services =>
        {
            var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
            documents.Setup(value => value.GetTypeMetadataAsync("pm.receivable_payment", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DocumentTypeMetadataDto("pm.receivable_payment", "Receivable Payment", EntityKind.Document));
            documents.Setup(value => value.GetByIdAsync("pm.receivable_payment", It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, Guid id, CancellationToken _) => new DocumentDto(id, PaymentDisplay,
                    new RecordPayload(new Dictionary<string, JsonElement>
                    {
                        ["amount"] = JsonSerializer.SerializeToElement(153.9794m),
                        ["received_on"] = JsonSerializer.SerializeToElement("2025-08-24"),
                        ["memo"] = JsonSerializer.SerializeToElement("Resident payment received for an additional charge.")
                    }), DocumentStatus.Posted, false));
            AddInvoiceReads(services, documents.Object);
        });
        using var client = host.Client("alex");
        client.Timeout = TimeSpan.FromMinutes(4);

        using var response = await client.PostAsJsonAsync("/api/ai-assistant/chat", new AiAssistantRequest(
            [new("user", "Find payment RP-2026-000014")], new("document", "pm.receivable_payment", host.State.PaymentId)));
        var events = ReadEvents(await response.Content.ReadAsStringAsync());
        var answer = string.Concat(events.Where(item => item.Type == "text").Select(item => item.Text));
        output.WriteLine(string.Join('\n', host.State.ModelInputs));

        Assert.DoesNotContain(events, item => item.Type == "error");
        Assert.Contains("RP-2026-000014", answer);
        Assert.DoesNotContain("not found", answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(host.State.PaymentId.ToString(), answer);
        Assert.DoesNotContain("pm.receivable_payment", answer);
        Assert.DoesNotContain('$', answer);
        Assert.DoesNotContain("USD", answer);
        Assert.NotEmpty(host.State.Searches);
        Assert.Equal(2, host.State.ModelInputs.Count);
    }
}
