using System.Text.Json;

namespace NGB.Mcp;

internal static class McpResultWriter
{
    internal static async Task<JsonElement> SerializeAsync<T>(T value, int maxBytes, CancellationToken ct)
    {
        await using var stream = new BoundedStream(maxBytes);
        await JsonSerializer.SerializeAsync(stream, value, McpJson.Options, ct);
        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return document.RootElement.Clone();
    }

    private sealed class BoundedStream(int maxBytes) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            RequireCapacity(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            RequireCapacity(buffer.Length);
            base.Write(buffer);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        private void RequireCapacity(int count)
        {
            if (count > maxBytes - Position)
                throw new McpReadException("result_too_large", "Narrow the query or request a smaller page.");
        }
    }
}
