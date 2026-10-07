using System.Text.Json.Serialization;

namespace Skarbiec.Portfolio.Features.Metals;

[JsonConverter(typeof(JsonStringEnumConverter<WeightUnit>))]
public enum WeightUnit
{
    Gram,
    TroyOunce,
}
