using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Npgsql;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// Test-only PostgreSQL wire proxy. Drops the server's successful COMMIT response, after
/// PostgreSQL has committed but before Npgsql can acknowledge it. No SQL is rewritten.
/// TLS and pooling are disabled only for this isolated fixture connection.
/// </summary>
internal sealed class LostCommitResponseProxy : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly NpgsqlConnectionStringBuilder _upstream;
    private readonly Task _forwarding;

    public TaskCompletionSource CommitResponseDropped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string ConnectionString { get; }

    public LostCommitResponseProxy(string connectionString)
    {
        _upstream = new(connectionString);
        _listener.Start();
        var client = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Host = "127.0.0.1",
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port,
            Pooling = false, SslMode = SslMode.Disable,
            GssEncryptionMode = GssEncryptionMode.Disable
        };
        ConnectionString = client.ConnectionString;
        _forwarding = ForwardAsync();
    }

    private async Task ForwardAsync()
    {
        try
        {
            using var downstream = await _listener.AcceptTcpClientAsync(_stop.Token);
            using var upstream = new TcpClient();
            await upstream.ConnectAsync(_upstream.Host!, _upstream.Port, _stop.Token);
            await using var incoming = downstream.GetStream();
            await using var outgoing = upstream.GetStream();
            var requests = incoming.CopyToAsync(outgoing, _stop.Token);
            var responses = ForwardResponsesAsync(outgoing, incoming);
            await Task.WhenAny(requests, responses);
            _stop.Cancel();
            // Closing both sockets unblocks either pending read immediately.
            downstream.Close(); upstream.Close();
            try { await Task.WhenAll(requests, responses); }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task ForwardResponsesAsync(Stream server, Stream client)
    {
        var header = new byte[5];
        while (!_stop.IsCancellationRequested)
        {
            await server.ReadExactlyAsync(header, _stop.Token);
            var size = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1));
            if (size is < 4 or > 16 * 1024 * 1024) throw new InvalidDataException("Invalid test protocol frame.");
            var body = new byte[size - 4];
            await server.ReadExactlyAsync(body, _stop.Token);
            if (header[0] == (byte)'C' && body.AsSpan().SequenceEqual("COMMIT\0"u8))
            {
                CommitResponseDropped.TrySetResult();
                return;
            }
            await client.WriteAsync(header, _stop.Token);
            await client.WriteAsync(body, _stop.Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); _listener.Stop();
        try { await _forwarding.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { _stop.Dispose(); }
    }
}
