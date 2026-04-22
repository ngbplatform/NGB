using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Documents;
using NGB.Definitions;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Documents.GeneralJournalEntry.Policies;
using Xunit;

namespace NGB.Runtime.Tests.Documents.GeneralJournalEntry;

public sealed class GeneralJournalEntry_Definitions_Patch9Tests
{
    [Fact]
    public void GJE_definition_is_registered_by_platform_contributor()
    {
        var services = new ServiceCollection();
        services.AddNgbRuntime();

        using var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<DefinitionsRegistry>();

        registry.TryGetDocument(AccountingDocumentTypeCodes.GeneralJournalEntry, out var def).Should().BeTrue();
        def.Should().NotBeNull();

        def!.NumberingPolicyType.Should().Be(typeof(GeneralJournalEntryNumberingPolicy));
        def.ApprovalPolicyType.Should().Be(typeof(GeneralJournalEntryApprovalPolicy));

        def.Metadata.Tables.Should().NotBeEmpty();
        def.Metadata.Tables.Select(t => t.TableName).Should().Contain(new[]
        {
            "doc_general_journal_entry",
            "doc_general_journal_entry__lines",
            "doc_general_journal_entry__allocations"
        });
    }
}
