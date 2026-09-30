using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using NGB.PostgreSql.Bootstrap;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.PostgreSql.Tests.Bootstrap;

public sealed class PostgresDatabaseProvisionerFullCoverageTests
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=ngb-jobs;Username=ngb;Password=ngb";

    [Fact]
    public async Task EnsureDatabaseExistsAsync_WhenDatabaseExists_OnlyChecksMaintenanceDatabase()
    {
        var connection = new RecordingDbConnection(databaseExists: true);
        var sut = new PostgresDatabaseProvisioner(new RecordingProviderFactory(connection));

        await sut.EnsureDatabaseExistsAsync(ConnectionString);

        new DbConnectionStringBuilder { ConnectionString = connection.ConnectionString }
            .ContainsKey("Database").Should().BeTrue();
        connection.ConnectionString.Should().Contain("Database=postgres");
        connection.Commands.Should().Contain(x => x.Contains("pg_advisory_lock", StringComparison.Ordinal));
        connection.Commands.Should().Contain(x => x.Contains("pg_advisory_unlock", StringComparison.Ordinal));
        connection.Commands.Should().Contain(x => x.Contains("WHERE datname = @DatabaseName", StringComparison.Ordinal));
        connection.Commands.Should().NotContain(x => x.StartsWith("CREATE DATABASE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnsureDatabaseExistsAsync_WhenDatabaseIsMissing_CreatesQuotedDatabase()
    {
        var connection = new RecordingDbConnection(databaseExists: false);
        var sut = new PostgresDatabaseProvisioner(new RecordingProviderFactory(connection));

        await sut.EnsureDatabaseExistsAsync(ConnectionString);

        connection.Commands.Should().Contain("CREATE DATABASE \"ngb-jobs\"");
    }

    [Fact]
    public async Task EnsureDatabaseExistsAsync_ValidatesInputsFactoryAndCancellation()
    {
        var sut = new PostgresDatabaseProvisioner();
        var blank = () => sut.EnsureDatabaseExistsAsync(" ");
        var noDatabase = () => sut.EnsureDatabaseExistsAsync("Host=localhost");
        await blank.Should().ThrowAsync<NgbArgumentRequiredException>();
        await noDatabase.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*must specify a database*");

        Action nullFactory = () => _ = new PostgresDatabaseProvisioner(null!);
        nullFactory.Should().Throw<ArgumentNullException>();

        var nullConnection = new PostgresDatabaseProvisioner(new RecordingProviderFactory(null));
        var missingConnection = () => nullConnection.EnsureDatabaseExistsAsync(ConnectionString);
        await missingConnection.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*did not create a connection*");

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = () => new PostgresDatabaseProvisioner(new RecordingProviderFactory(null))
            .EnsureDatabaseExistsAsync(ConnectionString, cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(PostgresErrorCodes.DuplicateDatabase, null)]
    [InlineData(PostgresErrorCodes.UniqueViolation, "pg_database_datname_index")]
    public async Task EnsureDatabaseExistsAsync_ToleratesExternalCreatorWinningTheRace(
        string sqlState,
        string? constraint)
    {
        var error = Pg(sqlState, constraint);
        var connection = new RecordingDbConnection(
            databaseExists: false,
            createError: error,
            existenceResults: [false, true]);
        var sut = new PostgresDatabaseProvisioner(new RecordingProviderFactory(connection));

        await sut.EnsureDatabaseExistsAsync(ConnectionString);

        connection.Commands.Should().Contain(x => x.StartsWith("CREATE DATABASE", StringComparison.Ordinal));
        connection.Commands.Should().Contain(x => x.Contains("pg_advisory_unlock", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnsureDatabaseExistsAsync_RethrowsDuplicateWhenDatabaseStillDoesNotExist()
    {
        var error = Pg(PostgresErrorCodes.DuplicateDatabase, constraint: null);
        var connection = new RecordingDbConnection(
            databaseExists: false,
            createError: error,
            existenceResults: [false, false]);
        var sut = new PostgresDatabaseProvisioner(new RecordingProviderFactory(connection));

        var act = () => sut.EnsureDatabaseExistsAsync(ConnectionString);

        (await act.Should().ThrowAsync<PostgresException>()).Which.Should().BeSameAs(error);
        connection.Commands.Should().Contain(x => x.Contains("pg_advisory_unlock", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnsureDatabaseExistsAsync_TreatsNonBooleanExistenceScalarAsMissing()
    {
        var connection = new RecordingDbConnection(
            databaseExists: false,
            existenceResults: [1]);
        var sut = new PostgresDatabaseProvisioner(new RecordingProviderFactory(connection));

        await sut.EnsureDatabaseExistsAsync(ConnectionString);

        connection.Commands.Should().Contain("CREATE DATABASE \"ngb-jobs\"");
    }

    private static PostgresException Pg(string sqlState, string? constraint)
        => new("error", "ERROR", "ERROR", sqlState, "", "", 0, 0, "", "", "public", "pg_database",
            "datname", "text", constraint, "file", "1", "routine");

    private sealed class RecordingProviderFactory(RecordingDbConnection? connection) : DbProviderFactory
    {
        public override DbConnection? CreateConnection() => connection;
    }

    private sealed class RecordingDbConnection : DbConnection
    {
        private readonly bool _databaseExists;
        private readonly PostgresException? _createError;
        private readonly Queue<object?> _existenceResults;
        private ConnectionState _state;

        public RecordingDbConnection(
            bool databaseExists,
            PostgresException? createError = null,
            IReadOnlyList<object?>? existenceResults = null)
        {
            _databaseExists = databaseExists;
            _createError = createError;
            _existenceResults = new Queue<object?>(existenceResults ?? [databaseExists]);
        }

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
        protected override DbCommand CreateDbCommand() => new RecordingDbCommand(
            this,
            () => _existenceResults.Count > 0 ? _existenceResults.Dequeue() : _databaseExists,
            _createError,
            Commands);
    }

    private sealed class RecordingDbCommand(
        DbConnection connection,
        Func<object?> databaseExists,
        PostgresException? createError,
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
            if (createError is not null && CommandText.StartsWith("CREATE DATABASE", StringComparison.Ordinal))
                throw createError;
            return 1;
        }
        public override object ExecuteScalar()
        {
            commands.Add(CommandText);
            return databaseExists()!;
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
