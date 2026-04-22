using System.Text.Json;
using NGB.Tools.Exceptions;

namespace NGB.Tools.Extensions;

public static class JsonElementExtensions
{
    /// <summary>
    /// Parses a Guid value from either:
    /// - a JSON string "{guid}", or
    /// - an object { id, display } (where id is a guid string).
    /// </summary>
    public static Guid ParseGuidOrRef(this JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.String)
            return Guid.Parse(el.GetString() ?? el.ToString());

        if (el.ValueKind == JsonValueKind.Object)
        {
            if (el.TryGetProperty("id", out var idEl) || el.TryGetProperty("Id", out idEl))
            {
                if (idEl.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    throw new NgbArgumentInvalidException("id", "Reference id must not be null.");

                if (idEl.ValueKind == JsonValueKind.String)
                    return Guid.Parse(idEl.GetString() ?? idEl.ToString());

                // Fallback for non-string id shapes.
                return Guid.Parse(idEl.ToString());
            }
        }

        // Fallback for other kinds (number/bool/etc.)
        return Guid.Parse(el.ToString());
    }
}
