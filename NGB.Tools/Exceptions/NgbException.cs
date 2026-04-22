namespace NGB.Tools.Exceptions;

/// <summary>
/// Base class for NGB domain/runtime exceptions.
///
/// Aimed for:
/// - stable error codes;
/// - structured context;
/// - consistent mapping to HTTP/gRPC;
/// </summary>
public abstract class NgbException : Exception, INgbError
{
    protected NgbException(
        string message,
        string errorCode,
        NgbErrorKind kind,
        IReadOnlyDictionary<string, object?>? context = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        Kind = kind;
        Context = context ?? new Dictionary<string, object?>();

        // Best-effort enrichment for 3rd-party loggers that capture Exception.Data.
        try
        {
            Data["ngb.error_code"] = ErrorCode;
            Data["ngb.kind"] = Kind.ToString();

            foreach (var kv in Context)
                Data[$"ngb.ctx.{kv.Key}"] = kv.Value;
        }
        catch
        {
            // Never throw from an exception constructor.
        }
    }

    public string ErrorCode { get; }

    public NgbErrorKind Kind { get; }

    public IReadOnlyDictionary<string, object?> Context { get; }

    public override string ToString()
    {
        // Keep base Exception.ToString() but add the NGB contract fields.
        return $"{base.ToString()}{Environment.NewLine}ErrorCode: {ErrorCode}{Environment.NewLine}Kind: {Kind}{Environment.NewLine}Context: {SerializeContext()}";
    }

    private string SerializeContext()
    {
        if (Context.Count == 0)
            return "{}";

        try
        {
            return System.Text.Json.JsonSerializer.Serialize(Context);
        }
        catch
        {
            // Never throw from ToString().
            return $"{{count:{Context.Count}}}";
        }
    }
}
