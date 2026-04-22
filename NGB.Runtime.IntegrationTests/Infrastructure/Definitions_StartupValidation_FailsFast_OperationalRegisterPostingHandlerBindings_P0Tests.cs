using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Definitions;
using NGB.Definitions.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.Metadata.Documents.Hybrid;
using NGB.OperationalRegisters.Contracts;
using NGB.Runtime.Definitions.Validation;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class Definitions_StartupValidation_FailsFast_OperationalRegisterPostingHandlerBindings_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task HostStart_WhenOperationalRegisterPostingHandlerTypeDoesNotImplementRequiredInterface_FailsFast()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.RemoveAll<DefinitionsRegistry>();
            services.AddSingleton(_ =>
            {
                var builder = new DefinitionsBuilder();
                builder.AddDocument("it_opreg_doc", d => d
                    .Metadata(TestDocMetadata.Create("it_opreg_doc"))
                    .OperationalRegisterPostingHandler<BadOpregPostingHandler>());
                return builder.Build();
            });
        });

        Func<Task> act = async () => await host.StartAsync();

        var ex = await act.Should().ThrowAsync<DefinitionsValidationException>();
        ex.Which.Errors.Should().Contain(e =>
            e.Contains(nameof(DocumentTypeDefinition.OperationalRegisterPostingHandlerType), StringComparison.Ordinal)
            && e.Contains(nameof(IDocumentOperationalRegisterPostingHandler), StringComparison.Ordinal));
    }

    [Fact]
    public async Task HostStart_WhenOperationalRegisterPostingHandlerTypeNotRegisteredInDi_FailsFast()
    {
        // NOTE: The handler implements the required interface but is intentionally not registered in DI.
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.RemoveAll<DefinitionsRegistry>();
            services.AddSingleton(_ =>
            {
                var builder = new DefinitionsBuilder();
                builder.AddDocument("it_opreg_doc", d => d
                    .Metadata(TestDocMetadata.Create("it_opreg_doc"))
                    .OperationalRegisterPostingHandler<GoodButNotRegisteredOpregPostingHandler>());
                return builder.Build();
            });
        });

        Func<Task> act = async () => await host.StartAsync();

        var ex = await act.Should().ThrowAsync<DefinitionsValidationException>();
        ex.Which.Errors.Should().Contain(e =>
            e.Contains("is not registered in DI", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HostStart_WhenOperationalRegisterPostingHandlerTypeIsOpenGeneric_FailsFast()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.RemoveAll<DefinitionsRegistry>();
            services.AddSingleton(_ =>
            {
                var builder = new DefinitionsBuilder();
                builder.AddDocument("it_opreg_doc", d => d
                    .Metadata(TestDocMetadata.Create("it_opreg_doc"))
                    .OperationalRegisterPostingHandler(typeof(OpenGenericOpregPostingHandler<>)));
                return builder.Build();
            });
        });

        Func<Task> act = async () => await host.StartAsync();

        var ex = await act.Should().ThrowAsync<DefinitionsValidationException>();
        ex.Which.Errors.Should().Contain(e =>
            e.Contains("closed constructed type", StringComparison.OrdinalIgnoreCase)
            && e.Contains(nameof(DocumentTypeDefinition.OperationalRegisterPostingHandlerType), StringComparison.Ordinal));
    }

    private static class TestDocMetadata
    {
        public static DocumentTypeMetadata Create(string typeCode)
            => new(
                TypeCode: typeCode,
                Tables: Array.Empty<DocumentTableMetadata>(),
                Presentation: new DocumentPresentationMetadata($"IT {typeCode}"),
                Version: new DocumentMetadataVersion(1, "it-tests"));
    }

    private sealed class BadOpregPostingHandler
    {
        // Intentionally does not implement IDocumentOperationalRegisterPostingHandler.
    }

    private sealed class GoodButNotRegisteredOpregPostingHandler : IDocumentOperationalRegisterPostingHandler
    {
        public string TypeCode => "it_opreg_doc";

        public Task BuildMovementsAsync(NGB.Core.Documents.DocumentRecord document, IOperationalRegisterMovementsBuilder builder, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class OpenGenericOpregPostingHandler<T> : IDocumentOperationalRegisterPostingHandler
    {
        public string TypeCode => "it_opreg_doc";

        public Task BuildMovementsAsync(NGB.Core.Documents.DocumentRecord document, IOperationalRegisterMovementsBuilder builder, CancellationToken ct)
            => Task.CompletedTask;
    }
}
