using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using NGB.BackgroundJobs.Hosting;
using NGB.BackgroundJobs.Infrastructure;
using NGB.Tools.Exceptions;

namespace NGB.BackgroundJobs.Tests.Infrastructure;

public sealed class HangfireToolsAndHostingBootstrapFullCoverageTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=ngb-jobs;Username=ngb;Password=ngb";

    [Fact]
    public async Task HangfireTools_WhenDatabaseExists_OnlyChecksUsingPostgresDatabase()
    {
        var connection = new RecordingDbConnection(databaseExists: true);

        await HangfireTools.EnsureDatabaseExistsAsync(ConnectionString, new RecordingProviderFactory(connection));

        new DbConnectionStringBuilder { ConnectionString = connection.ConnectionString }
            .ContainsKey("Database").Should().BeTrue();
        connection.ConnectionString.Should().Contain("Database=postgres");
        connection.Commands.Should().ContainSingle().Which.Should().Contain("WHERE datname = @DbName");
        connection.Commands.Should().NotContain(x => x.StartsWith("CREATE DATABASE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HangfireTools_WhenDatabaseIsMissing_CreatesQuotedDatabase()
    {
        var connection = new RecordingDbConnection(databaseExists: false);

        await HangfireTools.EnsureDatabaseExistsAsync(ConnectionString, new RecordingProviderFactory(connection));

        connection.Commands.Should().Contain("CREATE DATABASE \"ngb-jobs\"");
    }

    [Fact]
    public async Task HangfireTools_CoversInvalidDatabaseFactoryAndPublicEntryPoint()
    {
        var noDatabase = () => HangfireTools.EnsureDatabaseExistsAsync("Host=localhost");
        await noDatabase.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*must specify a database*");

        var nullFactory = () => HangfireTools.EnsureDatabaseExistsAsync(ConnectionString, null!);
        await nullFactory.Should().ThrowAsync<ArgumentNullException>();

        var nullConnection = () => HangfireTools.EnsureDatabaseExistsAsync(
            ConnectionString, new RecordingProviderFactory(null));
        await nullConnection.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*did not create a connection*");
    }

    [Fact]
    public async Task HostingBootstrap_NormalizesValuesAndDelegatesInfrastructureCreation()
    {
        string? received = null;
        var options = new BackgroundJobsHostingOptions();
        var bootstrap = new BackgroundJobsHostingBootstrap(
            options, " app ", " jobs ", value =>
            {
                received = value;
                return Task.CompletedTask;
            });

        bootstrap.Options.Should().BeSameAs(options);
        bootstrap.ApplicationConnectionString.Should().Be("app");
        bootstrap.HangfireConnectionString.Should().Be("jobs");
        await bootstrap.EnsureInfrastructureAsync();
        received.Should().Be("jobs");

        var publicBootstrap = new BackgroundJobsHostingBootstrap(options, "app", "jobs");
        publicBootstrap.HangfireConnectionString.Should().Be("jobs");
    }

    [Fact]
    public void HostingBootstrap_RejectsEveryMissingDependency()
    {
        Action missingOptions = () => new BackgroundJobsHostingBootstrap(null!, "app", "jobs", _ => Task.CompletedTask);
        Action missingApplication = () => new BackgroundJobsHostingBootstrap(new(), " ", "jobs", _ => Task.CompletedTask);
        Action missingHangfire = () => new BackgroundJobsHostingBootstrap(new(), "app", " ", _ => Task.CompletedTask);
        Action missingEnsure = () => new BackgroundJobsHostingBootstrap(new(), "app", "jobs", null!);

        missingOptions.Should().Throw<NgbArgumentRequiredException>();
        missingApplication.Should().Throw<NgbArgumentRequiredException>();
        missingHangfire.Should().Throw<NgbArgumentRequiredException>();
        missingEnsure.Should().Throw<NgbArgumentRequiredException>();
    }

    private sealed class RecordingProviderFactory(RecordingDbConnection? connection) : DbProviderFactory
    {
        public override DbConnection? CreateConnection() => connection;
    }

    private sealed class RecordingDbConnection(bool databaseExists) : DbConnection
    {
        private ConnectionState _state;
        public List<string> Commands { get; } = [];
        [AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "postgres";
        public override string DataSource => "fake";
        public override string ServerVersion => "1";
        public override ConnectionState State => _state;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() => _state = ConnectionState.Closed;
        public override void Open() => _state = ConnectionState.Open;
        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            Open();
            return Task.CompletedTask;
        }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new RecordingDbCommand(this, databaseExists, Commands);
    }

    private sealed class RecordingDbCommand(
        DbConnection connection,
        bool databaseExists,
        ICollection<string> commands) : DbCommand
    {
        private readonly RecordingParameterCollection _parameters = new();
        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = connection;
        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }
        public override void Cancel() { }
        public override int ExecuteNonQuery()
        {
            commands.Add(CommandText);
            return 1;
        }
        public override object ExecuteScalar()
        {
            commands.Add(CommandText);
            return databaseExists;
        }
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new RecordingDbParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
    }

    private sealed class RecordingDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
        public override bool IsNullable { get; set; }
        [AllowNull]
        public override string ParameterName { get; set; } = string.Empty;
        [AllowNull]
        public override string SourceColumn { get; set; } = string.Empty;
        public override object? Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() { }
    }

    private sealed class RecordingParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _items = [];
        public override int Count => _items.Count;
        public override object SyncRoot => ((ICollection)_items).SyncRoot;
        public override int Add(object value) { _items.Add((DbParameter)value); return _items.Count - 1; }
        public override void AddRange(Array values) { foreach (var value in values) Add(value!); }
        public override void Clear() => _items.Clear();
        public override bool Contains(object value) => _items.Contains((DbParameter)value);
        public override bool Contains(string value) => IndexOf(value) >= 0;
        public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
        public override IEnumerator GetEnumerator() => _items.GetEnumerator();
        public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName) => _items.FindIndex(x => x.ParameterName == parameterName);
        public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _items.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _items.RemoveAt(index);
        public override void RemoveAt(string parameterName) => _items.RemoveAt(IndexOf(parameterName));
        protected override DbParameter GetParameter(int index) => _items[index];
        protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
        protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value)
        {
            var index = IndexOf(parameterName);
            if (index < 0) _items.Add(value); else _items[index] = value;
        }
    }
}
