using System.Text.Json;

namespace NGB.Tools;

public static class JsonTools
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    
    public static JsonElement J<T>(T value) => JsonSerializer.SerializeToElement(value);
    
    public static JsonElement Jobj(object? value) => JsonSerializer.SerializeToElement(value, Json);
}
