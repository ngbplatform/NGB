namespace NGB.Runtime.Reporting;

/// <summary>Absorbs ZIP entry headers/trailers written synchronously by the BCL, without synchronous network I/O.</summary>
internal sealed class AsyncExportBuffer(Stream destination, CancellationToken requestCancellation) : Stream
{
    private const int Capacity = 64 * 1024;
    private readonly byte[] _pending = new byte[Capacity];
    private int _count;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length > Capacity - _count)
            throw new IOException("The ZIP writer exceeded its synchronous metadata buffer.");

        buffer.CopyTo(_pending.AsSpan(_count));
        _count += buffer.Length;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        => WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        await DrainAsync(ct);
        await destination.WriteAsync(buffer, ct);
    }

    public override void Flush() { }

    public override async Task FlushAsync(CancellationToken ct)
    {
        await DrainAsync(ct);
        await destination.FlushAsync(ct);
    }

    private async ValueTask DrainAsync(CancellationToken ct)
    {
        if (_count == 0)
            return;

        await destination.WriteAsync(_pending.AsMemory(0, _count), ct);
        _count = 0;
    }

    public override async ValueTask DisposeAsync()
    {
        if (!requestCancellation.IsCancellationRequested)
            await DrainAsync(requestCancellation);

        GC.SuppressFinalize(this);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
