using NGB.Tools.Exceptions;

namespace NGB.Api.Sso;

public sealed class KeycloakAdminClientException(
    string operation,
    int statusCode,
    IReadOnlyDictionary<string, object?>? context = null)
    : NgbInfrastructureException(
        message: "Keycloak Admin REST request failed.",
        errorCode: Code,
        context: BuildContext(operation, statusCode, context))
{
    public const string Code = "ngb.keycloak.admin_request_failed";

    private static IReadOnlyDictionary<string, object?> BuildContext(
        string operation,
        int statusCode,
        IReadOnlyDictionary<string, object?>? context)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["operation"] = operation,
            ["statusCode"] = statusCode
        };

        if (context is not null)
        {
            foreach (var (key, value) in context)
            {
                result[key] = value;
            }
        }

        return result;
    }
}
