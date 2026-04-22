using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Documents;
using NGB.Definitions;
using NGB.Metadata.Documents.Hybrid;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

public sealed class Definitions_DuplicateTypeCodes_FailFast_P0Tests
{
    [Fact]
    public void BuildServiceProvider_WhenContributorDuplicates_PlatformDocumentTypeCode_FailsFast_CaseInsensitive()
    {
        var services = new ServiceCollection();

        // Platform contributor (GJE).
        services.AddSingleton<IDefinitionsContributor, GeneralJournalEntryDefinitionsContributor>();

        // Duplicate contributor uses a different casing to verify case-insensitive type codes.
        services.AddSingleton<IDefinitionsContributor, DuplicateGjeDocumentContributor>();

        services.AddNgbDefinitions();

        var act = () =>
        {
            using var sp = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });

            // Force definitions build; contributors run when the registry is created.
            _ = sp.GetRequiredService<DefinitionsRegistry>();
        };

        act.Should().Throw<NgbConfigurationViolationException>()
            .Where(e => e.Message.Contains(AccountingDocumentTypeCodes.GeneralJournalEntry, StringComparison.OrdinalIgnoreCase))
            .WithMessage("*already registered*");
    }

    [Fact]
    public void BuildServiceProvider_WhenTwoContributorsRegisterSameDocumentTypeCode_FailsFast_CaseInsensitive()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IDefinitionsContributor, DuplicateDocContributorA>();
        services.AddSingleton<IDefinitionsContributor, DuplicateDocContributorB>();

        services.AddNgbDefinitions();

        var act = () =>
        {
            using var sp = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });

            // Force definitions build; contributors run when the registry is created.
            _ = sp.GetRequiredService<DefinitionsRegistry>();
        };

        act.Should().Throw<NgbConfigurationViolationException>()
            .Where(e => e.Message.Contains("doc.dup", StringComparison.OrdinalIgnoreCase))
            .WithMessage("*already registered*");
    }

    [Fact]
    public void BuildServiceProvider_WhenTwoContributorsRegisterSameCatalogTypeCode_FailsFast_CaseInsensitive()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IDefinitionsContributor, DuplicateCatalogContributorA>();
        services.AddSingleton<IDefinitionsContributor, DuplicateCatalogContributorB>();

        services.AddNgbDefinitions();

        var act = () =>
        {
            using var sp = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });

            // Force definitions build; contributors run when the registry is created.
            _ = sp.GetRequiredService<DefinitionsRegistry>();
        };

        act.Should().Throw<NgbConfigurationViolationException>()
            .Where(e => e.Message.Contains("cat.dup", StringComparison.OrdinalIgnoreCase))
            .WithMessage("*already registered*");
    }

    private sealed class DuplicateGjeDocumentContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            // Intentionally uses upper case to validate StringComparer.OrdinalIgnoreCase.
            builder.AddDocument(
                typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry.ToUpperInvariant(),
                configure: d => d.Metadata(new DocumentTypeMetadata(
                    TypeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
                    Tables: Array.Empty<DocumentTableMetadata>(),
                    Presentation: new DocumentPresentationMetadata("Duplicate GJE"),
                    Version: new DocumentMetadataVersion(1, "tests"))));
        }
    }

    private sealed class DuplicateDocContributorA : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(
                typeCode: "doc.dup",
                configure: d => d.Metadata(new DocumentTypeMetadata(
                    TypeCode: "doc.dup",
                    Tables: Array.Empty<DocumentTableMetadata>(),
                    Presentation: new DocumentPresentationMetadata("Dup"),
                    Version: new DocumentMetadataVersion(1, "tests"))));
        }
    }

    private sealed class DuplicateDocContributorB : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(
                typeCode: "DOC.DUP",
                configure: d => d.Metadata(new DocumentTypeMetadata(
                    TypeCode: "DOC.DUP",
                    Tables: Array.Empty<DocumentTableMetadata>(),
                    Presentation: new DocumentPresentationMetadata("Dup"),
                    Version: new DocumentMetadataVersion(1, "tests"))));
        }
    }

    private sealed class DuplicateCatalogContributorA : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddCatalog(
                typeCode: "cat.dup",
                configure: c => c.Metadata(TestCatalogMetadata.Create("cat.dup")));
        }
    }

    private sealed class DuplicateCatalogContributorB : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddCatalog(
                typeCode: "CAT.DUP",
                configure: c => c.Metadata(TestCatalogMetadata.Create("CAT.DUP")));
        }
    }

    private static class TestCatalogMetadata
    {
        public static NGB.Metadata.Catalogs.Hybrid.CatalogTypeMetadata Create(string code)
            => new(
                CatalogCode: code,
                DisplayName: "Dup Catalog",
                Tables: Array.Empty<NGB.Metadata.Catalogs.Hybrid.CatalogTableMetadata>(),
                Presentation: new NGB.Metadata.Catalogs.Hybrid.CatalogPresentationMetadata("cat_dup","name"),
                Version: new NGB.Metadata.Catalogs.Hybrid.CatalogMetadataVersion(1, "tests"));
    }
}
