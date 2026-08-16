using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using NGB.Persistence.UnitOfWork;

namespace NGB.PostgreSql.Tests.TestDoubles;

internal sealed class RecordingUnitOfWork(RecordingDbConnection connection, bool hasActiveTransaction = false) : IUnitOfWork
{
    public DbConnection Connection => connection;
    public DbTransaction? Transaction => null;
    public bool HasActiveTransaction { get; set; } = hasActiveTransaction;

    public Task EnsureConnectionOpenAsync(CancellationToken ct = default)
    {
        connection.Open();
        return Task.CompletedTask;
    }

    public Task BeginTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
    public void EnsureActiveTransaction()
    {
        if (!HasActiveTransaction)
            throw new InvalidOperationException("No active recording transaction.");
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class RecordingDbConnection(
    Func<string, DbDataReader>? readerFactory = null,
    Func<string, int>? nonQuery = null,
    Func<string, object?>? scalar = null,
    Func<CancellationToken, Task>? beforeOpen = null,
    Func<DbConnection, DbTransaction>? transactionFactory = null) : DbConnection
{
    private ConnectionState _state;

    public List<RecordingDbCommand> Commands { get; } = [];

    [AllowNull]
    public override string ConnectionString { get; set; } = "recording";
    public override string Database => "recording";
    public override string DataSource => "recording";
    public override string ServerVersion => "1";
    public override ConnectionState State => _state;
    public override void ChangeDatabase(string databaseName) { }
    public override void Close() => _state = ConnectionState.Closed;
    public override void Open() => _state = ConnectionState.Open;
    public override async Task OpenAsync(CancellationToken cancellationToken)
    {
        if (beforeOpen is not null)
            await beforeOpen(cancellationToken);

        Open();
    }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        => transactionFactory?.Invoke(this) ?? new RecordingDbTransaction(this);

    protected override DbCommand CreateDbCommand()
    {
        var command = new RecordingDbCommand(this, readerFactory, nonQuery, scalar);
        Commands.Add(command);
        return command;
    }
}

internal sealed class RecordingDbCommand(
    DbConnection connection,
    Func<string, DbDataReader>? readerFactory,
    Func<string, int>? nonQuery,
    Func<string, object?>? scalar) : DbCommand
{
    private readonly RecordingDbParameterCollection _parameters = new();

    [AllowNull]
    public override string CommandText { get; set; } = string.Empty;
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get; set; } = connection;
    protected override DbParameterCollection DbParameterCollection => _parameters;
    public IReadOnlyList<DbParameter> ParametersSnapshot => _parameters.Items;
    protected override DbTransaction? DbTransaction { get; set; }
    public override void Cancel() { }
    public override int ExecuteNonQuery() => nonQuery?.Invoke(CommandText) ?? 1;
    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        => Task.FromResult(ExecuteNonQuery());
    public override object? ExecuteScalar() => scalar?.Invoke(CommandText);
    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
        => Task.FromResult(ExecuteScalar());
    public override void Prepare() { }
    protected override DbParameter CreateDbParameter() => new RecordingDbParameter();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        => readerFactory?.Invoke(CommandText) ?? EmptyReader();

    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
        CommandBehavior behavior,
        CancellationToken cancellationToken)
        => Task.FromResult(ExecuteDbDataReader(behavior));

    private static DbDataReader EmptyReader() => new DataTable().CreateDataReader();
}

internal sealed class RecordingDbTransaction(
    DbConnection connection,
    bool throwOnRollback = false) : DbTransaction
{
    public bool Committed { get; private set; }
    public bool RolledBack { get; private set; }
    public bool Disposed { get; private set; }
    public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
    protected override DbConnection DbConnection => connection;
    public override void Commit() => Committed = true;
    public override void Rollback()
    {
        if (throwOnRollback)
            throw new InvalidOperationException("Simulated rollback failure.");

        RolledBack = true;
    }

    public override Task CommitAsync(CancellationToken cancellationToken = default)
    {
        Commit();
        return Task.CompletedTask;
    }

    public override Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        Rollback();
        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class RecordingDbParameter : DbParameter
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

internal sealed class RecordingDbParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _items = [];

    public IReadOnlyList<DbParameter> Items => _items;

    public override int Count => _items.Count;
    public override object SyncRoot => ((ICollection)_items).SyncRoot;
    public override int Add(object value)
    {
        _items.Add((DbParameter)value);
        return _items.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (var value in values)
            Add(value!);
    }

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
        if (index < 0)
            _items.Add(value);
        else
            _items[index] = value;
    }
}
