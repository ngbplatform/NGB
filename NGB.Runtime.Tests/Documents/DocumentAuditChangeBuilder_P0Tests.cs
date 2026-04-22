using System.Text.Json;
using FluentAssertions;
using NGB.Contracts.Common;
using NGB.Metadata.Documents.Hybrid;
using NGB.Runtime.Documents;
using Xunit;

namespace NGB.Runtime.Tests.Documents;

public sealed class DocumentAuditChangeBuilder_P0Tests
{

    [Fact]
    public void BuildCreateChanges_Ignores_SystemManaged_TopLevel_Display_And_Number()
    {
        var payload = new RecordPayload(
            Fields: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["display"] = JsonSerializer.SerializeToElement("Receivable Charge RC-2026-000001 2/25/2026"),
                ["number"] = JsonSerializer.SerializeToElement("RC-2026-000001"),
                ["memo"] = JsonSerializer.SerializeToElement("February charge"),
            });

        var changes = DocumentAuditChangeBuilder.BuildCreateChanges(payload, new DocumentPresentationMetadata(ComputedDisplay: true, HasNumber: true));

        changes.Select(x => x.FieldPath).Should().Contain("memo");
        changes.Select(x => x.FieldPath).Should().NotContain(new[] { "display", "number" });
    }

    [Fact]
    public void BuildUpdateChanges_Ignores_SystemManaged_TopLevel_Display_And_Number()
    {
        var before = new RecordPayload(
            Fields: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["display"] = JsonSerializer.SerializeToElement("Receivable Charge RC-2026-000001 2/25/2026"),
                ["number"] = JsonSerializer.SerializeToElement("RC-2026-000001"),
                ["memo"] = JsonSerializer.SerializeToElement("Old memo"),
            });

        var after = new RecordPayload(
            Fields: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["display"] = JsonSerializer.SerializeToElement("Receivable Charge RC-2026-000001 2/26/2026"),
                ["number"] = JsonSerializer.SerializeToElement("RC-2026-000999"),
                ["memo"] = JsonSerializer.SerializeToElement("New memo"),
            });

        var changes = DocumentAuditChangeBuilder.BuildUpdateChanges(before, after, new DocumentPresentationMetadata(ComputedDisplay: true, HasNumber: true));

        changes.Should().ContainSingle(x => x.FieldPath == "memo");
        changes.Should().NotContain(x => x.FieldPath == "display" || x.FieldPath == "number");
    }
    [Fact]
    public void BuildCreateChanges_Includes_HeadFields_And_PartRows()
    {
        var payload = new RecordPayload(
            Fields: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["memo"] = JsonSerializer.SerializeToElement("Rent lease"),
                ["rent_amount"] = JsonSerializer.SerializeToElement(1250.00m),
            },
            Parts: new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
            {
                ["parties"] = new RecordPartPayload([
                    new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["party_id"] = JsonSerializer.SerializeToElement("11111111-1111-1111-1111-111111111111"),
                        ["role"] = JsonSerializer.SerializeToElement("PrimaryTenant"),
                        ["is_primary"] = JsonSerializer.SerializeToElement(true),
                    }
                ])
            });

        var changes = DocumentAuditChangeBuilder.BuildCreateChanges(payload);

        changes.Select(x => x.FieldPath).Should().Contain(new[]
        {
            "memo",
            "rent_amount",
            "parts.parties[1].party_id",
            "parts.parties[1].role",
            "parts.parties[1].is_primary",
        });
    }

    [Fact]
    public void BuildUpdateChanges_Returns_Only_Changed_BusinessFields()
    {
        var before = new RecordPayload(
            Fields: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["memo"] = JsonSerializer.SerializeToElement("Old memo"),
                ["due_day"] = JsonSerializer.SerializeToElement(5),
            },
            Parts: new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
            {
                ["parties"] = new RecordPartPayload([
                    new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["role"] = JsonSerializer.SerializeToElement("PrimaryTenant"),
                        ["is_primary"] = JsonSerializer.SerializeToElement(true),
                    }
                ])
            });

        var after = new RecordPayload(
            Fields: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["memo"] = JsonSerializer.SerializeToElement("New memo"),
                ["due_day"] = JsonSerializer.SerializeToElement(5),
            },
            Parts: new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
            {
                ["parties"] = new RecordPartPayload([
                    new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["role"] = JsonSerializer.SerializeToElement("CoTenant"),
                        ["is_primary"] = JsonSerializer.SerializeToElement(true),
                    }
                ])
            });

        var changes = DocumentAuditChangeBuilder.BuildUpdateChanges(before, after);

        changes.Should().HaveCount(2);
        changes.Should().Contain(x => x.FieldPath == "memo" && x.OldValueJson!.Contains("Old memo") && x.NewValueJson!.Contains("New memo"));
        changes.Should().Contain(x => x.FieldPath == "parts.parties[1].role" && x.OldValueJson!.Contains("PrimaryTenant") && x.NewValueJson!.Contains("CoTenant"));
    }

    [Fact]
    public void BuildUpdateChanges_WhenPayloadIsIdentical_ReturnsEmpty()
    {
        var payload = new RecordPayload(
            Fields: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["memo"] = JsonSerializer.SerializeToElement("Same memo"),
                ["due_day"] = JsonSerializer.SerializeToElement(5),
            },
            Parts: new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
            {
                ["parties"] = new RecordPartPayload([
                    new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["party_id"] = JsonSerializer.SerializeToElement("11111111-1111-1111-1111-111111111111"),
                        ["role"] = JsonSerializer.SerializeToElement("PrimaryTenant"),
                        ["is_primary"] = JsonSerializer.SerializeToElement(true),
                        ["ordinal"] = JsonSerializer.SerializeToElement(1),
                    }
                ])
            });

        var changes = DocumentAuditChangeBuilder.BuildUpdateChanges(payload, payload);
        changes.Should().BeEmpty();
    }

    [Fact]
    public void BuildUpdateChanges_Detects_PartRow_Add_Remove_And_Update()
    {
        var before = new RecordPayload(
            Fields: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["memo"] = JsonSerializer.SerializeToElement("Lease memo"),
            },
            Parts: new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
            {
                ["parties"] = new RecordPartPayload([
                    new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["party_id"] = JsonSerializer.SerializeToElement("11111111-1111-1111-1111-111111111111"),
                        ["role"] = JsonSerializer.SerializeToElement("PrimaryTenant"),
                        ["is_primary"] = JsonSerializer.SerializeToElement(true),
                        ["ordinal"] = JsonSerializer.SerializeToElement(1),
                    },
                    new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["party_id"] = JsonSerializer.SerializeToElement("22222222-2222-2222-2222-222222222222"),
                        ["role"] = JsonSerializer.SerializeToElement("CoTenant"),
                        ["is_primary"] = JsonSerializer.SerializeToElement(false),
                        ["ordinal"] = JsonSerializer.SerializeToElement(2),
                    }
                ])
            });

        var after = new RecordPayload(
            Fields: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["memo"] = JsonSerializer.SerializeToElement("Lease memo"),
            },
            Parts: new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
            {
                ["parties"] = new RecordPartPayload([
                    new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["party_id"] = JsonSerializer.SerializeToElement("11111111-1111-1111-1111-111111111111"),
                        ["role"] = JsonSerializer.SerializeToElement("CoTenant"),
                        ["is_primary"] = JsonSerializer.SerializeToElement(false),
                        ["ordinal"] = JsonSerializer.SerializeToElement(1),
                    },
                    new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["party_id"] = JsonSerializer.SerializeToElement("33333333-3333-3333-3333-333333333333"),
                        ["role"] = JsonSerializer.SerializeToElement("PrimaryTenant"),
                        ["is_primary"] = JsonSerializer.SerializeToElement(true),
                        ["ordinal"] = JsonSerializer.SerializeToElement(2),
                    },
                    new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["party_id"] = JsonSerializer.SerializeToElement("44444444-4444-4444-4444-444444444444"),
                        ["role"] = JsonSerializer.SerializeToElement("Guarantor"),
                        ["is_primary"] = JsonSerializer.SerializeToElement(false),
                        ["ordinal"] = JsonSerializer.SerializeToElement(3),
                    }
                ])
            });

        var changes = DocumentAuditChangeBuilder.BuildUpdateChanges(before, after);

        changes.Should().Contain(x => x.FieldPath == "parts.parties[1].role" && x.OldValueJson!.Contains("PrimaryTenant") && x.NewValueJson!.Contains("CoTenant"));
        changes.Should().Contain(x => x.FieldPath == "parts.parties[1].is_primary" && x.OldValueJson == "true" && x.NewValueJson == "false");
        changes.Should().Contain(x => x.FieldPath == "parts.parties[2].party_id" && x.OldValueJson!.Contains("22222222") && x.NewValueJson!.Contains("33333333"));
        changes.Should().Contain(x => x.FieldPath == "parts.parties[2].role" && x.OldValueJson!.Contains("CoTenant") && x.NewValueJson!.Contains("PrimaryTenant"));
        changes.Should().Contain(x => x.FieldPath == "parts.parties[2].is_primary" && x.OldValueJson == "false" && x.NewValueJson == "true");
        changes.Should().Contain(x => x.FieldPath == "parts.parties[3].party_id" && x.OldValueJson == null && x.NewValueJson!.Contains("44444444"));
        changes.Should().Contain(x => x.FieldPath == "parts.parties[3].role" && x.OldValueJson == null && x.NewValueJson!.Contains("Guarantor"));
        changes.Should().Contain(x => x.FieldPath == "parts.parties[3].is_primary" && x.OldValueJson == null && x.NewValueJson == "false");
        changes.Should().Contain(x => x.FieldPath == "parts.parties[3].ordinal" && x.OldValueJson == null && x.NewValueJson == "3");
    }

    [Fact]
    public void BuildUpdateChanges_Detects_RemovedPartRow_AsNulls()
    {
        var before = new RecordPayload(
            Parts: new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
            {
                ["parties"] = new RecordPartPayload([
                    new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["party_id"] = JsonSerializer.SerializeToElement("11111111-1111-1111-1111-111111111111"),
                        ["role"] = JsonSerializer.SerializeToElement("PrimaryTenant"),
                        ["is_primary"] = JsonSerializer.SerializeToElement(true),
                        ["ordinal"] = JsonSerializer.SerializeToElement(1),
                    },
                    new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["party_id"] = JsonSerializer.SerializeToElement("22222222-2222-2222-2222-222222222222"),
                        ["role"] = JsonSerializer.SerializeToElement("CoTenant"),
                        ["is_primary"] = JsonSerializer.SerializeToElement(false),
                        ["ordinal"] = JsonSerializer.SerializeToElement(2),
                    }
                ])
            });

        var after = new RecordPayload(
            Parts: new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
            {
                ["parties"] = new RecordPartPayload([
                    new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["party_id"] = JsonSerializer.SerializeToElement("11111111-1111-1111-1111-111111111111"),
                        ["role"] = JsonSerializer.SerializeToElement("PrimaryTenant"),
                        ["is_primary"] = JsonSerializer.SerializeToElement(true),
                        ["ordinal"] = JsonSerializer.SerializeToElement(1),
                    }
                ])
            });

        var changes = DocumentAuditChangeBuilder.BuildUpdateChanges(before, after);

        changes.Should().Contain(x => x.FieldPath == "parts.parties[2].party_id" && x.OldValueJson!.Contains("22222222") && x.NewValueJson == null);
        changes.Should().Contain(x => x.FieldPath == "parts.parties[2].role" && x.OldValueJson!.Contains("CoTenant") && x.NewValueJson == null);
        changes.Should().Contain(x => x.FieldPath == "parts.parties[2].is_primary" && x.OldValueJson == "false" && x.NewValueJson == null);
        changes.Should().Contain(x => x.FieldPath == "parts.parties[2].ordinal" && x.OldValueJson == "2" && x.NewValueJson == null);
    }
}
