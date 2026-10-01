namespace NGB.Attachments.MinIO;

/// <summary>Service traffic and signing use separate endpoints; signed URLs are never rewritten.</summary>
public sealed class MinioAttachmentOptions
{
    public string InternalEndpoint { get; set; } = "";
    public string PublicEndpoint { get; set; } = "";
    public string Bucket { get; set; } = "ngb-attachments";
    public string Region { get; set; } = "us-east-1";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public bool AllowInsecureHttp { get; set; }
    public int RequestTimeoutSeconds { get; set; } = 30;

    public bool IsValid() => EndpointIsValid(InternalEndpoint)
        && EndpointIsValid(PublicEndpoint)
        && Bucket is { Length: >= 3 and <= 63 }
        && Bucket[0] != '-' && Bucket[^1] != '-'
        && Bucket.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')
        && !string.IsNullOrWhiteSpace(Region)
        && !string.IsNullOrWhiteSpace(AccessKey)
        && !string.IsNullOrWhiteSpace(SecretKey)
        && RequestTimeoutSeconds is >= 1 and <= 120;

    private bool EndpointIsValid(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == "https" || (AllowInsecureHttp && uri.Scheme == "http"))
        && uri is { AbsolutePath: "/", UserInfo.Length: 0, Query.Length: 0, Fragment.Length: 0 };
}
