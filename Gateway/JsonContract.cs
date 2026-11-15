using System.Text.Json;
using System.Text.Json.Serialization;

namespace OtLab.Gateway;

/// <summary>
/// Ustawienia serializacji kontraktu HMI <-> brama: nazwy pól i wartości enumów
/// w camelCase (np. HmiCommLost -> "hmiCommLost"), pola null są pomijane.
/// </summary>
public static class JsonContract
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
