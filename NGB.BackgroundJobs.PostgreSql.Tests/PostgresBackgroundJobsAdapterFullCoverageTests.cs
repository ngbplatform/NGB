using FluentAssertions;
using Hangfire.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using NGB.BackgroundJobs.PostgreSql.DependencyInjection;
using NGB.Persistence.BackgroundJobs;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.BackgroundJobs.PostgreSql.Tests;

public sealed class PostgresBackgroundJobsAdapterFullCoverageTests
{
    private readonly PostgresRecurringJobHashBatchReader _sut = new();

    [Fact]
    public async Task GetManyAsync_WhenNoJobIds_ReturnsEmptyWithoutOpeningConnection()
    {
        var result = await _sut.GetManyAsync(new RecurringJobHashBatchRequest(
            "Host=unused",
            "hangfire",
            []));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetManyAsync_WhenCancelled_StopsBeforeOpeningConnection()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => _sut.GetManyAsync(new RecurringJobHashBatchRequest(
            "Host=unused",
            "hangfire",
            ["job"]), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1hangfire")]
    [InlineData("-hangfire")]
    [InlineData("not-valid!")]
    public async Task GetManyAsync_WhenStorageNamespaceIsInvalid_RejectsItBeforeOpeningConnection(
        string storageNamespace)
    {
        var act = () => _sut.GetManyAsync(new RecurringJobHashBatchRequest(
            "Host=unused",
            storageNamespace,
            ["job"]));

        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*storage namespace*");
    }

    [Fact]
    public void StorageNamespace_AllowsLeadingAndEmbeddedUnderscores()
    {
        var underscoreOnly = () => PostgresRecurringJobHashBatchReader.ValidateStorageNamespace("_");
        var letterOnly = () => PostgresRecurringJobHashBatchReader.ValidateStorageNamespace("h");
        var leadingUnderscore = () => PostgresRecurringJobHashBatchReader.ValidateStorageNamespace("_hangfire_jobs");
        var leadingLetter = () => PostgresRecurringJobHashBatchReader.ValidateStorageNamespace("hangfire_jobs");
        var embeddedDigit = () => PostgresRecurringJobHashBatchReader.ValidateStorageNamespace("hangfire2_jobs");

        underscoreOnly.Should().NotThrow();
        letterOnly.Should().NotThrow();
        leadingUnderscore.Should().NotThrow();
        leadingLetter.Should().NotThrow();
        embeddedDigit.Should().NotThrow();
    }

    [Fact]
    public async Task GetManyAsync_WhenRequestIsNull_Throws()
    {
        var act = () => _sut.GetManyAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void StorageFactory_CreatesPostgresStorageAndValidatesInputs()
    {
        var storage = PostgresHangfireJobStorageFactory.Create(
            "Host=localhost;Database=jobs;Username=ngb;Password=ngb",
            "hangfire_jobs",
            prepareSchemaIfNecessary: false);

        storage.Should().BeOfType<PostgreSqlStorage>();

        Action blankConnection = () => PostgresHangfireJobStorageFactory.Create(" ", "hangfire", true);
        Action unsafeNamespace = () => PostgresHangfireJobStorageFactory.Create(
            "Host=unused",
            "hangfire;drop schema public",
            true);
        blankConnection.Should().Throw<ArgumentException>();
        unsafeNamespace.Should().Throw<NgbConfigurationViolationException>();
    }

    [Fact]
    public void DependencyInjection_RegistersAdapterAndRejectsNullCollection()
    {
        var services = new ServiceCollection();

        services.AddNgbPostgresBackgroundJobsAdapter().Should().BeSameAs(services);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IRecurringJobHashBatchReader>()
            .Should().BeOfType<PostgresRecurringJobHashBatchReader>();

        Action nullServices = () => PostgresBackgroundJobsServiceCollectionExtensions
            .AddNgbPostgresBackgroundJobsAdapter(null!);
        nullServices.Should().Throw<ArgumentNullException>();
    }
}
